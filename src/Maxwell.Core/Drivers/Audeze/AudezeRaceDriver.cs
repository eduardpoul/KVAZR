using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;

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
                ? [.. devices.Select(WithBluetooth)]
                : [Describe(MaxwellVariant.Xbox, []) with { Transports = [BluetoothTransport] }];
        }

        return Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);
    }

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
            ImageKey = variant == MaxwellVariant.PlayStation ? "maxwell-ps-3q" : "maxwell-3q",
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

    private static DeviceInfo WithBluetooth(DeviceInfo device) =>
        device with { Transports = [.. device.Transports, BluetoothTransport] };

    private static IReadOnlyList<TransportInfo> TransportsOf(IReadOnlyList<MaxwellInterface> interfaces) =>
    [
        .. interfaces
            .OrderBy(i => i.IsDongle)
            .Select(i => new TransportInfo
            {
                Kind = i.IsDongle ? TransportKind.Dongle : TransportKind.UsbCable,
                Label = i.IsDongle ? $"Донгл 0x{i.ProductId:X4}" : $"USB-C 0x{i.ProductId:X4}",
                IsPresent = true,

                // Both. The dongle was marked audio-only on a premise that turned out
                // to be measurement error, not protocol.
                CarriesControl = true,
                CarriesAudio = true,
            }),
    ];
}
