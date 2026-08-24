using Maxwell.Core.Abstractions;
using System.Runtime.Versioning;

using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// Finds Audeze Maxwell headsets and opens RACE channels to them.
///
/// A cable and a dongle for the same headset are reported as <em>one</em> device with
/// two transports, not two devices, and the device page shows both rows with no choice
/// to make.
///
/// Either lead carries control. The cable is preferred only because it is steadier and
/// lower latency — not because the dongle cannot drive the headset, which it does,
/// battery and settings alike.
/// </summary>
public sealed class AudezeRaceDriver : IDeviceDriver
{
    public const string Id = "audeze.race";

    private readonly IHidEnumerator _enumerator;
    private readonly IHidTransportFactory _factory;
    private readonly IBluetoothMonitor _bluetooth;

    public AudezeRaceDriver(
        IHidEnumerator enumerator,
        IHidTransportFactory factory,
        IBluetoothMonitor? bluetooth = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(factory);

        _enumerator = enumerator;
        _factory = factory;
        _bluetooth = bluetooth ?? NullBluetoothMonitor.Instance;
    }

    public string DriverId => Id;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MaxwellInterface> interfaces = _enumerator.EnumerateRaceInterfaces();

        List<DeviceInfo> devices =
        [
            .. interfaces
                .GroupBy(VariantOf)
                .Where(g => g.Key != MaxwellVariant.Unknown)
                .Select(g => Describe(g.Key, [.. g])),
        ];

        BluetoothStatus bluetooth = _bluetooth.Query();

        if (bluetooth.Present)
        {
            // Bluetooth says nothing about which edition this is, so it joins an entry
            // that a lead has already identified rather than starting one of its own.
            // Alone, it has to guess, and the Xbox edition is the common one — the guess
            // costs a label, not a capability.
            devices = devices.Count > 0
                ? [.. devices.Select(d => WithBluetooth(d, bluetooth.Address))]
                : [WithBluetooth(Describe(MaxwellVariant.Xbox, []), bluetooth.Address)];
        }

        return Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);
    }

    // Opens Windows HID handles, including the control interfaces the wearer's own wheel
    // and mute button report on.
    [SupportedOSPlatform("windows")]
    public Task<IDeviceConnection> ConnectAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        MaxwellVariant variant = VariantOfKey(device.Key);

        IReadOnlyList<MaxwellInterface> mine =
            [.. _enumerator.EnumerateRaceInterfaces().Where(i => VariantOf(i) == variant)];

        // Whichever lead is there. Opening it proves nothing about the headset being
        // awake — that question is settled by asking the device something, which the
        // session does — but refusing to open a dongle at all was the bug that kept
        // this application from ever driving the headset wirelessly.
        MaxwellInterface? target = mine.FirstOrDefault(i => !i.IsDongle)
                                   ?? (mine.Count > 0 ? mine[0] : null);

        if (target is null)
        {
            // No lead, but the headset may still be on Bluetooth, where the battery is
            // readable and nothing else is. Telling someone wearing the headset that no
            // device was found is worse than telling them what this link can do.
            if (_bluetooth.Query().Present)
            {
                return Task.FromResult<IDeviceConnection>(
                    new AudezeBluetoothConnection(_bluetooth, [BluetoothTransport]));
            }

            throw new IOException($"{device.DisplayName}: no RACE interface present.");
        }

        var session = new RaceSession(_factory.Open(target));

        // No control watcher. It was here to hear the boom being raised, and the boom
        // says nothing: ninety seconds of listening on all three interfaces caught
        // fourteen reports from the volume wheel and none from the boom. The wheel itself
        // was never worth acting on — it moves the host's volume, not the gain inside the
        // headset — so there is nothing left for it to do. MaxwellControlWatcher stays in
        // the library, unused, for whoever tries again.
        IDeviceConnection connection = new AudezeRaceConnection(session, TransportsOf(mine));

        return Task.FromResult(connection);
    }

    // ---------- shape of the product line ----------

    /// <summary>
    /// The Xbox and PlayStation editions are separate products with their own pairs of
    /// product ids, so they must not collapse into one entry in the device list.
    /// </summary>
    private enum MaxwellVariant
    {
        Unknown,
        Xbox,
        PlayStation,
    }

    private static MaxwellVariant VariantOf(MaxwellInterface iface) => iface.Transport switch
    {
        MaxwellTransport.XboxDongle or MaxwellTransport.XboxUsbC => MaxwellVariant.Xbox,
        MaxwellTransport.PlayStationDongle or MaxwellTransport.PlayStationUsbC => MaxwellVariant.PlayStation,
        _ => MaxwellVariant.Unknown,
    };

    private static MaxwellVariant VariantOfKey(string key) =>
        key.EndsWith("-ps", StringComparison.Ordinal) ? MaxwellVariant.PlayStation : MaxwellVariant.Xbox;

    private static string KeyOf(MaxwellVariant variant) =>
        variant == MaxwellVariant.PlayStation ? $"{Id}:maxwell-ps" : $"{Id}:maxwell-xbox";

    private static DeviceInfo Describe(MaxwellVariant variant, IReadOnlyList<MaxwellInterface> interfaces) =>
        new()
        {
            Key = KeyOf(variant),
            DriverId = Id,
            Vendor = "Audeze",
            Model = variant == MaxwellVariant.PlayStation ? "Maxwell (PlayStation)" : "Maxwell",
            Class = DeviceClass.Headset,
            ImageKey = "audeze-maxwell",

            // Every lead this headset is currently reachable on, so the system driver's
            // copy of the same hardware can be recognised and dropped.
            HardwareIds = new HashSet<string>(
                interfaces.Select(i => $"usb:{MaxwellInterface.VendorId:x4}:{i.ProductId:x4}"),
                StringComparer.Ordinal),


            // The headset answers for its own sound: master gain, per-earcup balance,
            // microphone level, the equaliser. Core Audio would put the Windows slider
            // for the same hardware beside all of that, and a page carrying two volumes
            // that are both correct and unrelated is a page nobody can read.
            //
            // Only the audio contributor is superseded. A battery usage or anything else
            // that turns up later is still welcome, because RACE does not answer for it.
            Supersedes = new HashSet<string>(StringComparer.Ordinal) { "audio" },
            Transports = TransportsOf(interfaces),
        };

    /// <summary>
    /// Audio and a battery reading, no control. The Maxwell publishes no HID service
    /// over Bluetooth, so this is a fact about the headset rather than a gap here.
    /// </summary>
    private static TransportInfo BluetoothTransport => new()
    {
        Kind = TransportKind.Bluetooth,
        Label = "Bluetooth",
        IsPresent = true,
        CarriesControl = false,
        CarriesAudio = true,
    };

    /// <summary>
    /// Adds the Bluetooth channel, and the address with it. The address is what tells the
    /// system driver — which finds this same headset knowing nothing about it — that the
    /// two of them are talking about one device. Without it the Maxwell appears twice
    /// while it is on Bluetooth: once with every setting, once with a battery reading.
    /// </summary>
    private static DeviceInfo WithBluetooth(DeviceInfo device, string? address) =>
        device with
        {
            Transports = [.. device.Transports, BluetoothTransport],
            HardwareIds = address is { Length: > 0 }
                ? new HashSet<string>(device.HardwareIds, StringComparer.Ordinal)
                {
                    $"bt:{address.ToLowerInvariant()}",
                }
                : device.HardwareIds,
        };

    private static IReadOnlyList<TransportInfo> TransportsOf(IReadOnlyList<MaxwellInterface> interfaces) =>
    [
        .. interfaces
            .OrderBy(i => i.IsDongle)
            .Select(i => new TransportInfo
            {
                Kind = i.IsDongle ? TransportKind.Dongle : TransportKind.UsbCable,
                // Named for a person, not for a bus analyser: the product id said
                // nothing a user could act on, and it is still on the facts card for
                // whoever does want it.
                Label = i.IsDongle ? "USB-донгл" : "USB-C",
                IsPresent = true,

                // Both. The dongle was marked audio-only on a premise that turned out
                // to be measurement error, not protocol.
                CarriesControl = true,
                CarriesAudio = true,
            }),
    ];
}
