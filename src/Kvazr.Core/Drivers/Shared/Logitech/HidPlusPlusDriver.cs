using System.Runtime.Versioning;

using Kvazr.Core.Catalogue;
using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Windows;

namespace Kvazr.Core.Drivers.Logitech;

/// <summary>
/// Logitech devices behind a Unifying or Lightspeed receiver, over HID++.
///
/// A class driver, not a vendor one in spirit: one protocol covers a decade of Logitech
/// peripherals, found by usage page rather than by model. It exists because the system
/// tier can only ever show the receiver — "USB Receiver", no battery, no name — while
/// one ping and three reads turn that into "PRO X 2, mouse, 45%".
///
/// One device per mouse, however many ways there are to reach it. A mouse on its
/// charging cable and the same mouse on its receiver's radio are one thing on the desk,
/// and they used to be two rows in the list — keyed by route, a slot on one and a cable on
/// the other, with whichever was not in use left behind as a disconnected twin. And the
/// receiver, which hides behind the mouse only while the mouse speaks through it, came
/// back as a row of its own the moment the mouse went onto the cable. Now the device is
/// keyed by its own unit id, which it gives the same way by either route and which the
/// receiver's pairing table files it under; the routes are its transports, each present
/// or not; and a receiver that remembers a device belongs to that device whether or not it
/// is on the air.
///
/// Read-only throughout. A wrong write to a receiver can unpair the device driving the
/// cursor, and everything this driver is for is available without writing anything.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HidPlusPlusDriver : IDeviceDriver
{
    public const string Id = "logitech.hidpp";

    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort ShortUsage = 0x01;
    private const ushort LongUsage = 0x02;

    private readonly IHidEnumerator _enumerator;
    private readonly IDeviceCatalogue? _catalogue;

    private static readonly HashSet<string> EmptyIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Where each device can be spoken to right now, by key. Rebuilt every sweep: the
    /// route moves when the mouse goes from radio to cable, and a device nothing reaches
    /// has no entry at all.
    /// </summary>
    private readonly Dictionary<string, Route> _homes = new(StringComparer.Ordinal);

    public HidPlusPlusDriver(IHidEnumerator enumerator, IDeviceCatalogue? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _enumerator = enumerator;
        _catalogue = catalogue;
    }

    public string DriverId => Id;

    public DriverTier Tier => DriverTier.Class;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        List<HidInterface> interfaces = [.. _enumerator.EnumerateAll()
            .Where(i => i.VendorId == Logitech && i.UsagePage == HidPlusPlusPage)];

        var sightings = new Dictionary<string, Sighting>(StringComparer.Ordinal);
        var receivers = new List<(HidInterface Face, HidInterface Short, int Paired)>();

        foreach (HidInterface face in interfaces.Where(i => i.Usage == LongUsage && i.OutputReportLength >= 20))
        {
            cancellationToken.ThrowIfCancellationRequested();

            HidInterface? shortFace = interfaces.FirstOrDefault(i =>
                i.Usage == ShortUsage && i.ProductId == face.ProductId);

            try
            {
                // The short channel first, and closed again before the long one opens. The
                // receiver itself says how many devices are paired, instantly. A device on
                // its own cable has no such register and refuses on the long path — where
                // a long channel already open would keep the refusal and hand it to the
                // next question as its answer.
                int? paired = null;

                if (shortFace is not null)
                {
                    using var shortChannel = new WindowsHidChannel(
                        shortFace.Path, shortFace.InputReportLength, shortFace.OutputReportLength);
                    paired = HidPlusPlusReader.PairedCount(shortChannel);
                }

                if (paired is null) Cabled(face, sightings);
                else if (paired > 0) receivers.Add((face, shortFace!, paired.Value));
            }
            catch (IOException)
            {
                // Held exclusively by another process, or gone mid-enumeration. The system
                // tier still shows the hardware, so nothing disappears; this tier simply has
                // nothing extra to say this sweep.
            }
        }

        // Receivers after cables, so that a device already found on its cable is not
        // pinged over a radio it is not using. Each such ping is two wake timeouts, every
        // sweep, for as long as the mouse is charging.
        foreach ((HidInterface face, HidInterface shortFace, int paired) in receivers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Received(face, shortFace, paired, sightings);
            }
            catch (IOException)
            {
                // As above: the receiver is there and its channel is not.
            }
        }

        _homes.Clear();

        var devices = new List<DeviceInfo>();

        foreach (Sighting seen in sightings.Values)
        {
            if (seen.Live is { } route) _homes[seen.Key] = route;
            devices.Add(Describe(seen, _catalogue));
        }

        return Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);
    }

    /// <summary>
    /// A device wearing its own HID++ interface: the mouse on its charging cable. It
    /// answers on the direct index, and it answers on every slot index too, which is how
    /// asking it six slot questions once minted six copies of one mouse.
    /// </summary>
    private static void Cabled(HidInterface face, Dictionary<string, Sighting> sightings)
    {
        using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

        if (HidPlusPlusReader.Interrogate(channel, HidPlusPlusReader.DirectDevice) is not { } direct) return;

        Sighting seen = Sight(
            sightings,
            HidPlusPlusReader.UnitId(channel, HidPlusPlusReader.DirectDevice),
            $"hidpp:{face.ProductId:x4}:direct");

        seen.Cable = face.ProductId;
        seen.CableRoute = new Route(
            face.Path, face.InputReportLength, face.OutputReportLength, HidPlusPlusReader.DirectDevice);

        // The device's own answers outrank anything a receiver's table remembers.
        seen.Name = direct.Name;
        seen.Type = direct.Type ?? seen.Type;
    }

    /// <summary>
    /// Everything a receiver knows about: the devices in its pairing table, on the air or
    /// not, and which of them answer right now.
    /// </summary>
    private static void Received(
        HidInterface face, HidInterface shortFace, int paired, Dictionary<string, Sighting> sightings)
    {
        // The long channel opens first: the table is asked on the short path and answered
        // on this one, and a handle only hears what arrives while it exists.
        using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);
        using var shortChannel = new WindowsHidChannel(
            shortFace.Path, shortFace.InputReportLength, shortFace.OutputReportLength);

        IReadOnlyList<HidPlusPlusReader.Remembered> table = HidPlusPlusReader.Pairings(shortChannel, channel);

        if (table.Count == 0)
        {
            Pinged(face, channel, paired, sightings);
            return;
        }

        foreach (HidPlusPlusReader.Remembered remembered in table)
        {
            Sighting seen = Sight(sightings, remembered.Serial, $"hidpp:{face.ProductId:x4}:{remembered.Slot}");

            seen.Receiver = face.ProductId;
            seen.Slot = remembered.Slot;
            seen.Name ??= remembered.Name;
            seen.Type ??= remembered.Type;

            // On its cable, so the radio is idle and the ping could only time out.
            if (seen.CableRoute is not null) continue;

            if (HidPlusPlusReader.Interrogate(channel, remembered.Slot) is not { } onAir) continue;

            seen.AirRoute = new Route(face.Path, face.InputReportLength, face.OutputReportLength, remembered.Slot);
            seen.Name = onAir.Name;
            seen.Type = onAir.Type ?? seen.Type;
        }
    }

    /// <summary>
    /// For a receiver that keeps no pairing table this driver can read: ask the slots
    /// themselves, stopping once as many have answered as the receiver says are paired.
    /// Only what is on the air can be found this way, and is known by the unit id it gives
    /// over the air.
    /// </summary>
    private static void Pinged(HidInterface face, WindowsHidChannel channel, int paired, Dictionary<string, Sighting> sightings)
    {
        int answered = 0;

        for (byte slot = 1; slot <= 6 && answered < paired; slot++)
        {
            if (HidPlusPlusReader.Interrogate(channel, slot) is not { } onAir) continue;

            answered++;

            Sighting seen = Sight(
                sightings, HidPlusPlusReader.UnitId(channel, slot), $"hidpp:{face.ProductId:x4}:{slot}");

            seen.Receiver = face.ProductId;
            seen.Slot = slot;
            seen.AirRoute = new Route(face.Path, face.InputReportLength, face.OutputReportLength, slot);
            seen.Name ??= onAir.Name;
            seen.Type ??= onAir.Type;
        }
    }

    /// <summary>
    /// The one record for a device this sweep, found by its unit id where it gave one.
    /// Without a unit id it can only be known by where it was found, which is the route
    /// key it always had.
    /// </summary>
    private static Sighting Sight(Dictionary<string, Sighting> sightings, string? unit, string route)
    {
        string key = unit is null ? route : $"hidpp:unit:{unit}";

        if (!sightings.TryGetValue(key, out Sighting? seen))
        {
            seen = new Sighting(key);
            sightings[key] = seen;
        }

        return seen;
    }

    /// <summary>What the registry is told about one device: every route it has, each present or not.</summary>
    internal static DeviceInfo Describe(Sighting seen, IDeviceCatalogue? catalogue)
    {
        ArgumentNullException.ThrowIfNull(seen);

        // Each route's usb id, deliberately: it is what the system tier lists that hardware
        // under, and sharing it is what makes the registry drop the bare "USB Receiver" row
        // in favour of the device actually behind it. The receiver's id is claimed whenever
        // its table names this device — on the air, on the cable, or switched off — because
        // in every one of those the receiver is this mouse's and nobody else's.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var transports = new List<TransportInfo>();

        if (seen.Cable is { } cable)
        {
            ids.Add($"usb:{Logitech:x4}:{cable:x4}");
            ids.Add($"hidpp:{cable:x4}:{HidPlusPlusReader.DirectDevice}");

            transports.Add(new TransportInfo
            {
                Kind = TransportKind.UsbCable,
                Label = "USB",
                IsPresent = true,
                CarriesControl = true,
            });
        }

        if (seen.Receiver is { } receiver)
        {
            ids.Add($"usb:{Logitech:x4}:{receiver:x4}");
            ids.Add($"hidpp:{receiver:x4}:{seen.Slot}");

            transports.Add(new TransportInfo
            {
                Kind = TransportKind.Dongle,
                Label = "USB dongle",
                IsPresent = seen.AirRoute is not null,
                CarriesControl = true,
            });
        }

        // Matched by the device's own name only. The ids here are the receiver's, and
        // handing them to the catalogue found the receiver's entry — which promptly
        // renamed the mouse back into "USB Receiver".
        CatalogueEntry? entry = seen.Name is { } name ? catalogue?.Match(EmptyIds, name) : null;

        return new DeviceInfo
        {
            Key = seen.Key,
            DriverId = Id,
            Vendor = entry?.Vendor ?? "Logitech",
            Model = entry?.Model ?? seen.Name ?? "Logitech device",
            Class = entry?.Class is { } known and not DeviceClass.Unknown ? known : ClassOf(seen.Type),
            ImageKey = entry?.Slug,
            HardwareIds = ids,
            Transports = transports,
        };
    }

    private static DeviceClass ClassOf(HidPlusPlusReader.Kind? type) => type switch
    {
        HidPlusPlusReader.Kind.Keyboard or HidPlusPlusReader.Kind.Numpad => DeviceClass.Keyboard,
        HidPlusPlusReader.Kind.Mouse or HidPlusPlusReader.Kind.Touchpad or HidPlusPlusReader.Kind.Trackball =>
            DeviceClass.Mouse,
        HidPlusPlusReader.Kind.Receiver => DeviceClass.Receiver,
        _ => DeviceClass.Unknown,
    };

    public Task<IDeviceConnection> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        // A device its receiver remembers but nothing reaches: switched off, or out of
        // range. It is still this device, and still listed as such; there is simply no
        // one to talk to, and the session asks again on the next sweep.
        if (!_homes.TryGetValue(device.Key, out Route? home))
        {
            throw new IOException($"Nothing reaches {device.Key} right now.");
        }

        // Asked once, here, because this is where a device is first spoken to and
        // because none of these answers can change while it stays connected. What comes
        // back decides which rows the page has — a device that describes no resolution
        // gets no resolution row rather than an empty one.
        PointerFacts facts = new();

        try
        {
            using var channel = new WindowsHidChannel(home.Path, home.Input, home.Output);
            facts = PointerReader.Read(channel, home.Index);
        }
        catch (IOException)
        {
            // The receiver is busy or gone. The battery rows still work through their own
            // opening, and the introduction is asked again the next time it connects.
        }

        return Task.FromResult<IDeviceConnection>(
            new HidPlusPlusConnection(device, home.Path, home.Input, home.Output, home.Index, facts));
    }

    /// <summary>One way to speak to a device: an interface path and the index it answers on there.</summary>
    internal sealed record Route(string Path, int Input, int Output, byte Index);

    /// <summary>One device as a sweep met it, by whichever of its routes reached it.</summary>
    internal sealed class Sighting(string key)
    {
        public string Key { get; } = key;

        public string? Name { get; set; }

        public HidPlusPlusReader.Kind? Type { get; set; }

        /// <summary>Its own product id, when it is on its cable.</summary>
        public ushort? Cable { get; set; }

        public Route? CableRoute { get; set; }

        /// <summary>The receiver whose pairing table holds it, on the air or not.</summary>
        public ushort? Receiver { get; set; }

        public byte Slot { get; set; }

        /// <summary>Set only when it answered through the receiver this sweep.</summary>
        public Route? AirRoute { get; set; }

        /// <summary>The route to speak over: the cable when there is one, the radio otherwise.</summary>
        public Route? Live => CableRoute ?? AirRoute;
    }
}
