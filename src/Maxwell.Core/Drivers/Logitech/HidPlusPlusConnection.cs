using System.Runtime.Versioning;

using Maxwell.Core.Devices;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Logitech;

/// <summary>
/// One paired device, read through its receiver on demand.
///
/// The channel is opened per read rather than held: a receiver is shared hardware —
/// Logitech's own software may be running beside us — and the polls are half a minute
/// apart. Paying an open per read buys never contending for the handle in between.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HidPlusPlusConnection : IDeviceConnection
{
    private static readonly Capability BatteryCapability = new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Заряд",
        Subtitle = "По HID++ через приёмник",
        Access = CapabilityAccess.Read,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private readonly DeviceInfo _device;
    private readonly string _path;
    private readonly int _input;
    private readonly int _output;
    private readonly byte _slot;

    public HidPlusPlusConnection(DeviceInfo device, string path, int input, int output, byte slot)
    {
        _device = device;
        _path = path;
        _input = input;
        _output = output;
        _slot = slot;
    }

    public IReadOnlyList<Capability> Capabilities { get; } = [BatteryCapability];

    public IReadOnlyList<TransportInfo> Transports => _device.Transports;

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (capabilityId != CapabilityIds.PowerBattery) return Task.FromResult(CapabilityValue.Absent);

        try
        {
            using var channel = new WindowsHidChannel(_path, _input, _output);

            return Task.FromResult(
                HidPlusPlusReader.Battery(channel, _slot) is { } percent
                    ? CapabilityValue.Of(percent)
                    : CapabilityValue.Absent);
        }
        catch (IOException)
        {
            // The receiver is momentarily someone else's, or gone. An absent reading is
            // the honest answer either way; presence is the discovery sweep's business.
            return Task.FromResult(CapabilityValue.Absent);
        }
    }

    /// <summary>Nothing here writes, and nothing pretends to.</summary>
    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public void Dispose()
    {
    }
}
