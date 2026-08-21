using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// The headset as reachable over Bluetooth alone: one capability, and that is the
/// truth rather than a limitation of this code.
///
/// Over Bluetooth the Maxwell advertises only audio profiles — A2DP, AVRCP and
/// Hands-Free — with no HID service, so RACE has nothing to attach to. What does come
/// across is the battery, which Hands-Free reports and Windows keeps as a device
/// property. Reported as a device with a single capability rather than as a device in
/// a degraded state: the capability model exists precisely so that "this link offers
/// less" needs no special case.
///
/// There is an Airoha control service on the BLE side — its UUID spells "AirohaBLE" in
/// ASCII — so control over Bluetooth is very likely possible. Reaching it means a GATT
/// client and a second framing of RACE, sharing no code with the HID path, and it has
/// not been attempted.
/// </summary>
internal sealed class AudezeBluetoothConnection : IDeviceConnection
{
    private static readonly Capability[] Declared =
    [
        new()
        {
            Id = CapabilityIds.PowerBattery,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Заряд",
            Subtitle = "По Bluetooth доступен только он",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },
    ];

    private readonly IBluetoothMonitor _monitor;

    public AudezeBluetoothConnection(IBluetoothMonitor monitor, IReadOnlyList<TransportInfo> transports)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(transports);

        _monitor = monitor;
        Transports = transports;
    }

    public IReadOnlyList<Capability> Capabilities => Declared;

    public IReadOnlyList<TransportInfo> Transports { get; }

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (capabilityId != CapabilityIds.PowerBattery) return Task.FromResult(CapabilityValue.Absent);

        BluetoothStatus status = _monitor.Query();

        return Task.FromResult(status is { Present: true, HasBattery: true }
            ? CapabilityValue.Of(status.BatteryPercent)
            : CapabilityValue.Absent);
    }

    /// <summary>Nothing here is writable, and nothing pretends to be.</summary>
    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public void Dispose()
    {
    }
}
