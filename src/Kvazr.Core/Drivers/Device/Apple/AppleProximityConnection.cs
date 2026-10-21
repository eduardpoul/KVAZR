using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;

namespace Kvazr.Core.Drivers.Apple;

/// <summary>
/// What a pair of Apple earphones says about itself, out of the beacon it broadcasts.
///
/// Read-only, and every row of it is a number the device volunteered — there is nothing to
/// ask and nowhere to write. Apple's accessory protocol, which would answer questions and
/// take settings, runs over L2CAP; Windows gives user-mode sockets RFCOMM only, so on this
/// operating system the beacon is the whole conversation.
///
/// The device does not beacon continuously. Measured: in ears with the case shut it says
/// nothing at all for as long as you care to listen, and starts again the moment the case
/// is opened. So a reading is kept and served with its age rather than being replaced by
/// nothing — a silence is the earphones being worn, not a battery that vanished.
/// </summary>
internal sealed class AppleProximityConnection : IDeviceConnection
{
    /// <summary>
    /// How long a reading is worth showing.
    ///
    /// Generous on purpose, because of the silence above: a pair worn for an hour would
    /// blank its own page under any short window, and the number from when they were last
    /// in the case is closer to the truth than no number at all. Past this they are
    /// probably somewhere else entirely.
    /// </summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    private readonly IProximityListener _listener;
    private readonly ushort _product;

    public AppleProximityConnection(IProximityListener listener, ushort product, IReadOnlyList<TransportInfo> transports)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(transports);

        _listener = listener;
        _product = product;
        Transports = transports;
    }

    public IReadOnlyList<Capability> Capabilities { get; } =
    [
        new()
        {
            Id = CapabilityIds.PowerBattery,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Range,
            Title = "Charge",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },
        new()
        {
            Id = CapabilityIds.PowerCharging,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Charging",
            Access = CapabilityAccess.Read,
        },
        new()
        {
            Id = CapabilityIds.PowerBatteryLeft,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Left",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },

            // Read, kept, and not drawn as a row of its own. The three halves belong on
            // one line beside the device — "Left 100%  Right 100%  Case 10%" — which is
            // how the thing is actually read, and three rows saying one thing each is
            // three times the page for a third of the sense.
            IsSupporting = true,
        },
        new()
        {
            Id = CapabilityIds.PowerBatteryRight,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Right",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
            IsSupporting = true,
        },
        new()
        {
            Id = CapabilityIds.PowerBatteryCase,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Case",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
            IsSupporting = true,
        },
        new()
        {
            Id = CapabilityIds.PowerChargingLeft,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Left charging",
            Access = CapabilityAccess.Read,
            IsSupporting = true,
        },
        new()
        {
            Id = CapabilityIds.PowerChargingRight,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Right charging",
            Access = CapabilityAccess.Read,
            IsSupporting = true,
        },
        new()
        {
            Id = CapabilityIds.PowerChargingCase,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Case charging",
            Access = CapabilityAccess.Read,
            IsSupporting = true,
        },
    ];

    public IReadOnlyList<TransportInfo> Transports { get; }

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        ProximityReading? heard = _listener.Latest(_product);

        if (heard is null || DateTimeOffset.UtcNow - heard.At > Fresh)
        {
            return Task.FromResult(CapabilityValue.Absent);
        }

        CapabilityValue value = capabilityId switch
        {
            CapabilityIds.PowerBattery => Percent(heard.Lowest),
            CapabilityIds.PowerCharging => CapabilityValue.Of(heard.IsCharging),
            CapabilityIds.PowerBatteryLeft => Percent(heard.Left),
            CapabilityIds.PowerBatteryRight => Percent(heard.Right),
            CapabilityIds.PowerBatteryCase => Percent(heard.Case),
            CapabilityIds.PowerChargingLeft => CapabilityValue.Of(heard.LeftCharging),
            CapabilityIds.PowerChargingRight => CapabilityValue.Of(heard.RightCharging),
            CapabilityIds.PowerChargingCase => CapabilityValue.Of(heard.CaseCharging),
            _ => CapabilityValue.Absent,
        };

        return Task.FromResult(value);
    }

    /// <summary>Nothing here writes, and nothing pretends to.</summary>
    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    private static CapabilityValue Percent(int? level) =>
        level is { } percent ? CapabilityValue.Of(percent) : CapabilityValue.Absent;

    public void Dispose()
    {
        // The listener is shared and outlives every device on it. Nothing here owns it.
    }
}
