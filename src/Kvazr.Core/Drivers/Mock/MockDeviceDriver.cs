using Kvazr.Core.Devices;

namespace Kvazr.Core.Drivers.Mock;

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

    private readonly int _count;

    /// <param name="count">
    /// How many stand-ins to report. Two covers the design's mouse and keyboard screens;
    /// more exists so the crowded cases can be looked at — the tray panel is specified to
    /// reach its height ceiling at six devices, and that is not checkable with two.
    /// </param>
    public MockDeviceDriver(int count = 2) => _count = Math.Clamp(count, 1, Extras.Length + 2);

    public string DriverId => Id;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<DeviceInfo> devices = [Mouse, Keyboard];
        devices.AddRange(Extras.Take(Math.Max(0, _count - 2)));

        return Task.FromResult<IReadOnlyList<DeviceInfo>>([.. devices.Take(_count)]);
    }

    /// <summary>Filler for the crowded cases. Same shapes, different names.</summary>
    private static DeviceInfo[] Extras =>
    [
        Crowd("mock:mouse2", "Logitech", "G Pro X (mock)", DeviceClass.Mouse),
        Crowd("mock:keyboard2", "Keychron", "K2 (mock)", DeviceClass.Keyboard),
        Crowd("mock:headset2", "SteelSeries", "Arctis Nova (mock)", DeviceClass.Headset),
        Crowd("mock:mouse3", "Razer", "Viper (mock)", DeviceClass.Mouse),
    ];

    private static DeviceInfo Crowd(string key, string vendor, string model, DeviceClass kind) => new()
    {
        Key = key,
        DriverId = Id,
        Vendor = vendor,
        Model = model,
        Class = kind,
        Transports = [Wireless("Bluetooth")],
    };

    public Task<IDeviceConnection> ConnectAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        IDeviceConnection connection = device.Class == DeviceClass.Keyboard
            ? new MockConnection(MockCatalogs.Keyboard, device.Transports, KeyboardSeed)
            : new MockConnection(MockCatalogs.Mouse, device.Transports, MouseSeed);

        return Task.FromResult(connection);
    }

    private static DeviceInfo Mouse => new()
    {
        Key = MouseKey,
        DriverId = Id,
        Vendor = "Logitech",
        Model = "MX Master (mock)",
        Class = DeviceClass.Mouse,
        Transports = [Wireless("Lightspeed receiver")],
    };

    private static DeviceInfo Keyboard => new()
    {
        Key = KeyboardKey,
        DriverId = Id,
        Vendor = "NuPhy",
        Model = "Air75 (mock)",
        Class = DeviceClass.Keyboard,
        Transports = [Wireless("Bluetooth, host 1")],
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
        [CapabilityIds.LinkHosts] = CapabilityValue.Choice(0),
        [CapabilityIds.PointerDpi] = CapabilityValue.Of(1600),
        [CapabilityIds.PointerDpiXy] = CapabilityValue.Numbers(1600, 1200),
        [CapabilityIds.PointerReportRate] = CapabilityValue.Choice(3),

        // A gentle S: slow movements damped, fast ones let through. Believable rather
        // than flat, because a flat curve and an unread one would look identical and
        // this control's whole job is to keep those two apart.
        [CapabilityIds.PointerResponse] = CapabilityValue.Numbers(-28, -16, -6, 0, 9, 20, 31),

        [CapabilityIds.ButtonsMap] = CapabilityValue.Vector(
        [
            CapabilityValue.Choice(1),
            CapabilityValue.Choice(2),
            CapabilityValue.Choice(3),
            CapabilityValue.Choice(4),
        ]),

        [CapabilityIds.LightOn] = CapabilityValue.Of(true),
        [CapabilityIds.LightBrightness] = CapabilityValue.Of(40),
        [CapabilityIds.LightEffect] = CapabilityValue.Choice(1),
        [CapabilityIds.LightColor] = CapabilityValue.Numbers(0x2C6FD1, 0x7B4EC4),
        [CapabilityIds.ProfilesOnboard] = CapabilityValue.Choice(0),
    };

    private static Dictionary<string, CapabilityValue> KeyboardSeed => new(StringComparer.Ordinal)
    {
        [CapabilityIds.PowerBattery] = CapabilityValue.Of(81),
        [CapabilityIds.PowerCharging] = CapabilityValue.Of(true),
        [CapabilityIds.LinkHosts] = CapabilityValue.Choice(1),
        [CapabilityIds.KeysMap] = Layout,
        [CapabilityIds.KeysLayers] = CapabilityValue.Of(4),
        [CapabilityIds.LightOn] = CapabilityValue.Of(true),
        [CapabilityIds.LightBrightness] = CapabilityValue.Of(70),
        [CapabilityIds.LightEffect] = CapabilityValue.Choice(2),
        [CapabilityIds.LightColor] = CapabilityValue.Numbers(0x3FA45B),
        [CapabilityIds.ProfilesOnboard] = CapabilityValue.Choice(0),
    };

    /// <summary>
    /// Two layers answered and two not.
    ///
    /// A real VIA read fetches layers one at a time and a keyboard that has only been
    /// asked for the first two is the ordinary case, not a fault. Leaving the upper two
    /// absent is what makes the control demonstrate the difference between a key with
    /// no assignment and a key nobody has asked about — which is the same difference
    /// the whole value hierarchy exists to preserve.
    /// </summary>
    private static CapabilityValue Layout => CapabilityValue.Vector(
    [
        Legends(MockCatalogs.BaseLayer),
        Legends(MockCatalogs.FunctionLayer),
        CapabilityValue.Absent,
        CapabilityValue.Absent,
    ]);

    private static CapabilityValue Legends(IReadOnlyList<string> layer) =>
        CapabilityValue.Vector([.. layer.Select(CapabilityValue.Of)]);
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
