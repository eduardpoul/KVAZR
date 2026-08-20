using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// Finds Audeze Maxwell headsets and opens RACE channels to them.
///
/// A cable and a dongle for the same headset are reported as <em>one</em> device with
/// two transports, not two devices. That is how the hardware actually behaves — audio
/// flows over the dongle while configuration answers only over the cable — and it is
/// what the device page shows: both rows, no choice to make.
/// </summary>
public sealed class AudezeRaceDriver : IDeviceDriver
{
    public const string Id = "audeze.race";

    private readonly IHidEnumerator _enumerator;
    private readonly IHidTransportFactory _factory;

    public AudezeRaceDriver(IHidEnumerator enumerator, IHidTransportFactory factory)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(factory);

        _enumerator = enumerator;
        _factory = factory;
    }

    public string DriverId => Id;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MaxwellInterface> interfaces = _enumerator.EnumerateRaceInterfaces();

        IReadOnlyList<DeviceInfo> devices =
        [
            .. interfaces
                .GroupBy(VariantOf)
                .Where(g => g.Key != MaxwellVariant.Unknown)
                .Select(g => Describe(g.Key, [.. g])),
        ];

        return Task.FromResult(devices);
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

        // Control only works over a cable. The dongle enumerates and carries audio but
        // answers no RACE command at all, so there is nothing to open — the caller keeps
        // the device visible in monitor-only mode with whatever it last read.
        MaxwellInterface? cable = mine.FirstOrDefault(i => !i.IsDongle);
        if (cable is null)
        {
            throw new IOException($"{device.DisplayName}: no control channel — only the dongle is present.");
        }

        var session = new RaceSession(_factory.Open(cable));
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

    private static IReadOnlyList<TransportInfo> TransportsOf(IReadOnlyList<MaxwellInterface> interfaces) =>
    [
        .. interfaces
            .OrderBy(i => i.IsDongle)
            .Select(i => new TransportInfo
            {
                Kind = i.IsDongle ? TransportKind.Dongle : TransportKind.UsbCable,
                Label = i.IsDongle ? $"Донгл 0x{i.ProductId:X4}" : $"USB-C 0x{i.ProductId:X4}",
                IsPresent = true,
                CarriesControl = !i.IsDongle,
                CarriesAudio = true,
            }),
    ];
}
