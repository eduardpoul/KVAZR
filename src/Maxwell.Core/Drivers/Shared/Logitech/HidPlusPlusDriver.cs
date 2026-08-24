using System.Runtime.Versioning;

using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Logitech;

/// <summary>
/// Logitech devices behind a Unifying or Lightspeed receiver, over HID++.
///
/// A class driver, not a vendor one in spirit: one protocol covers a decade of Logitech
/// peripherals, found by usage page rather than by model. It exists because the system
/// tier can only ever show the receiver — "USB Receiver", no battery, no name — while
/// one ping and three reads turn that into "PRO X 2, mouse, 45%".
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

    /// <summary>Where each discovered device lives: the receiver path and the slot.</summary>
    private readonly Dictionary<string, (string Path, int Input, int Output, byte Slot)> _homes = new(StringComparer.Ordinal);

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
        var devices = new List<DeviceInfo>();

        List<HidInterface> interfaces = [.. _enumerator.EnumerateAll()
            .Where(i => i.VendorId == Logitech && i.UsagePage == HidPlusPlusPage)];

        foreach (HidInterface receiver in interfaces
                     .Where(i => i.Usage == LongUsage && i.OutputReportLength >= 20))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // The short channel first: the receiver itself says how many devices
                // are connected, instantly. Pinging an empty slot costs two wake
                // timeouts, so knowing when to stop is most of this driver's speed —
                // an empty receiver finishes in one exchange.
                int? expected = null;

                if (interfaces.FirstOrDefault(i =>
                        i.Usage == ShortUsage && i.ProductId == receiver.ProductId) is { } shortIface)
                {
                    using var shortChannel = new WindowsHidChannel(
                        shortIface.Path, shortIface.InputReportLength, shortIface.OutputReportLength);
                    expected = HidPlusPlusReader.ConnectedCount(shortChannel);
                }

                if (expected == 0) continue;

                using var channel = new WindowsHidChannel(
                    receiver.Path, receiver.InputReportLength, receiver.OutputReportLength);

                // No receiver register means no receiver: this is a device wearing its
                // own HID++ interface — the mouse on its charging cable. It answers on
                // the direct index, and it answers on every slot index too, which is
                // how asking it six slot questions minted six copies of one mouse.
                if (expected is null)
                {
                    if (HidPlusPlusReader.Interrogate(channel, HidPlusPlusReader.DirectDevice) is { } direct)
                    {
                        devices.Add(Describe(receiver, direct));
                    }

                    continue;
                }

                int before = devices.Count;

                for (byte slot = 1; slot <= 6; slot++)
                {
                    if (devices.Count - before >= expected) break;
                    if (HidPlusPlusReader.Interrogate(channel, slot) is not { } paired) continue;

                    devices.Add(Describe(receiver, paired));
                }
            }
            catch (IOException)
            {
                // The receiver is there and its channel is not — another process holds
                // it exclusively, or it vanished mid-enumeration. The system tier still
                // shows the receiver itself, so nothing disappears; this tier simply has
                // nothing extra to say this sweep.
            }
        }

        return Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);
    }

    private DeviceInfo Describe(HidInterface receiver, HidPlusPlusReader.Paired paired)
    {
        // Keyed by receiver product and slot: a pairing survives replugs and reboots,
        // and the key must too, because settings hang off it. A direct device — the
        // mouse on its cable — is its own home and is keyed by its own product id.
        bool direct = paired.Slot == HidPlusPlusReader.DirectDevice;
        string key = direct
            ? $"hidpp:{receiver.ProductId:x4}:direct"
            : $"hidpp:{receiver.ProductId:x4}:{paired.Slot}";

        _homes[key] = (receiver.Path, receiver.InputReportLength, receiver.OutputReportLength, paired.Slot);

        // The receiver's usb id, deliberately: it is what the system tier lists this
        // hardware under, and sharing it is what makes the registry drop the bare
        // "USB Receiver" row in favour of the device actually behind it.
        var ids = new HashSet<string>(StringComparer.Ordinal)
        {
            $"usb:{Logitech:x4}:{receiver.ProductId:x4}",
            $"hidpp:{receiver.ProductId:x4}:{paired.Slot}",
        };

        // Matched by the device's own name only. The ids here are the receiver's, and
        // handing them to the catalogue found the receiver's entry — which promptly
        // renamed the mouse back into "USB Receiver".
        CatalogueEntry? entry = _catalogue?.Match(EmptyIds, paired.Name);

        return new DeviceInfo
        {
            Key = key,
            DriverId = Id,
            Vendor = entry?.Vendor ?? "Logitech",
            Model = entry?.Model ?? paired.Name,
            Class = entry?.Class is { } known and not DeviceClass.Unknown ? known : ClassOf(paired.Type),
            ImageKey = entry?.Slug,
            HardwareIds = ids,
            Transports =
            [
                new TransportInfo
                {
                    Kind = direct ? TransportKind.UsbCable : TransportKind.Dongle,
                    Label = direct ? "USB" : "USB dongle",
                    IsPresent = true,
                    CarriesControl = true,
                },
            ],
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

        if (!_homes.TryGetValue(device.Key, out (string Path, int Input, int Output, byte Slot) home))
        {
            throw new IOException($"No receiver recorded for {device.Key}.");
        }

        return Task.FromResult<IDeviceConnection>(new HidPlusPlusConnection(device, home.Path, home.Input, home.Output, home.Slot));
    }
}
