using System.Runtime.Versioning;

using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Core.Drivers.Flydigi;

/// <summary>
/// A Flydigi pad's charge, asked through its dongle on demand.
///
/// The channel is opened per question rather than held, as the Logitech one is: the polls
/// are half a minute apart, and Flydigi's own software may want the same collection in
/// between. The collection is looked up again whenever it will not open, because the
/// dongle comes back under a new path every time the pad is switched on.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class FlydigiConnection : IDeviceConnection
{
    private static readonly Capability BatteryCapability = new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charge",
        Subtitle = "From the pad, through its dongle — in fifths",
        Access = CapabilityAccess.Read,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private static readonly Capability ChargingCapability = new()
    {
        Id = CapabilityIds.PowerCharging,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charging",
        Subtitle = "From the pad, through its dongle",
        Access = CapabilityAccess.Read,
    };

    /// <summary>
    /// How long one answer serves both rows. The charge and the charging flag are one byte
    /// and one exchange; a sweep reads them back to back and asking twice would double the
    /// traffic for the same byte.
    /// </summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(800);

    private readonly Func<HidInterface?> _find;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HidInterface? _face;
    private FlydigiV2.Charge? _last;
    private DateTime _lastAt;

    public FlydigiConnection(Func<HidInterface?> find)
    {
        ArgumentNullException.ThrowIfNull(find);

        _find = find;
    }

    public IReadOnlyList<Capability> Capabilities { get; } = [BatteryCapability, ChargingCapability];

    /// <summary>Transports belong to whoever owns the device; this only says more about it.</summary>
    public IReadOnlyList<TransportInfo> Transports => [];

    public async Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (capabilityId is not (CapabilityIds.PowerBattery or CapabilityIds.PowerCharging))
        {
            return CapabilityValue.Absent;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_last is null || DateTime.UtcNow - _lastAt > Fresh)
            {
                _last = Ask();
                _lastAt = DateTime.UtcNow;
            }

            if (_last is not { } charge) return CapabilityValue.Absent;

            return capabilityId == CapabilityIds.PowerBattery
                ? CapabilityValue.Of(charge.Percent)
                : CapabilityValue.Of(charge.IsCharging);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>One get-info, on the collection last seen, or on wherever it has moved to.</summary>
    private FlydigiV2.Charge? Ask()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            _face ??= _find();
            if (_face is not { } face) return null;

            try
            {
                using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

                byte[]? reply = channel.Request(
                    FlydigiV2.GetInfo(face.OutputReportLength),
                    r => FlydigiV2.Answers(r, FlydigiV2.GetInfoCommand),
                    Wait);

                return reply is null ? null : FlydigiV2.ReadInfo(reply)?.Battery;
            }
            catch (IOException)
            {
                // Gone from this path. The pad was switched off, or switched on again and
                // the dongle came back somewhere else; look once more before giving up.
                _face = null;
            }
        }

        return null;
    }

    /// <summary>Nothing here writes, and nothing pretends to.</summary>
    public Task<bool> WriteAsync(string capabilityId, CapabilityValue value, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public void Dispose() => _gate.Dispose();
}
