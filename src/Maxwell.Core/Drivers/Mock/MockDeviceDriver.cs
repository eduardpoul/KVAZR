using Maxwell.Core.Devices;

namespace Maxwell.Core.Drivers.Mock;

/// <summary>
/// A stand-in mouse and keyboard, so the shell can be built against three device
/// classes instead of one.
///
/// Development only. It must not ship: everything it reports is invented, and a device
/// list that includes hardware the user does not own is worse than an empty one. The
/// host registers it behind an explicit opt-in, never by default.
/// </summary>
public sealed class MockDeviceDriver : IDeviceDriver
{
    public const string Id = "mock";

    public const string MouseKey = $"{Id}:mouse";
    public const string KeyboardKey = $"{Id}:keyboard";

    public string DriverId => Id;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<DeviceInfo> devices = [Mouse, Keyboard];
        return Task.FromResult(devices);
    }

    public Task<IDeviceConnection> ConnectAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        IDeviceConnection connection = device.Key == KeyboardKey
            ? new MockConnection(MockCatalogs.Keyboard, Keyboard.Transports, KeyboardSeed)
            : new MockConnection(MockCatalogs.Mouse, Mouse.Transports, MouseSeed);

        return Task.FromResult(connection);
    }

    private static DeviceInfo Mouse => new()
    {
        Key = MouseKey,
        DriverId = Id,
        Vendor = "Logitech",
        Model = "MX Master (макет)",
        Class = DeviceClass.Mouse,
        Transports = [Wireless("Приёмник Lightspeed")],
    };

    private static DeviceInfo Keyboard => new()
    {
        Key = KeyboardKey,
        DriverId = Id,
        Vendor = "NuPhy",
        Model = "Air75 (макет)",
        Class = DeviceClass.Keyboard,
        Transports = [Wireless("Bluetooth, хост 1")],
    };

    private static TransportInfo Wireless(string label) => new()
    {
        Kind = TransportKind.Bluetooth,
        Label = label,
        IsPresent = true,
        CarriesControl = true,
        CarriesAudio = false,
    };

    private static Dictionary<string, CapabilityValue> MouseSeed => new(StringComparer.Ordinal)
    {
        [CapabilityIds.PowerBattery] = CapabilityValue.Of(64),
        [CapabilityIds.PowerCharging] = CapabilityValue.Of(false),
        [CapabilityIds.LinkSignal] = CapabilityValue.Of(92),
        [CapabilityIds.PointerDpi] = CapabilityValue.Of(1600),
        [CapabilityIds.PointerReportRate] = CapabilityValue.Choice(3),
        [CapabilityIds.LightBrightness] = CapabilityValue.Of(40),
        [CapabilityIds.LightEffect] = CapabilityValue.Choice(1),
    };

    private static Dictionary<string, CapabilityValue> KeyboardSeed => new(StringComparer.Ordinal)
    {
        [CapabilityIds.PowerBattery] = CapabilityValue.Of(81),
        [CapabilityIds.PowerCharging] = CapabilityValue.Of(true),
        [CapabilityIds.KeysLayers] = CapabilityValue.Of(4),
        [CapabilityIds.LightOn] = CapabilityValue.Of(true),
        [CapabilityIds.LightBrightness] = CapabilityValue.Of(70),
        [CapabilityIds.LightEffect] = CapabilityValue.Choice(2),
    };
}

/// <summary>Keeps its values in a dictionary and takes every write.</summary>
internal sealed class MockConnection : IDeviceConnection
{
    private readonly Dictionary<string, CapabilityValue> _values;

    public MockConnection(
        IReadOnlyList<Capability> capabilities,
        IReadOnlyList<TransportInfo> transports,
        Dictionary<string, CapabilityValue> seed)
    {
        Capabilities = capabilities;
        Transports = transports;
        _values = seed;
    }

    public IReadOnlyList<Capability> Capabilities { get; }

    public IReadOnlyList<TransportInfo> Transports { get; }

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.GetValueOrDefault(capabilityId, CapabilityValue.Absent));

    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        _values[capabilityId] = value;
        return Task.FromResult(true);
    }

    public void Dispose()
    {
    }
}
