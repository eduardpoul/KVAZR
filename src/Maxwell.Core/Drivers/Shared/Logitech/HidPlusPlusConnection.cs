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
        Title = "Charge",
        Subtitle = "Over HID++ through the receiver",
        Access = CapabilityAccess.Read,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private readonly DeviceInfo _device;
    private readonly string _path;
    private readonly int _input;
    private readonly int _output;
    private readonly byte _slot;

    /// <summary>The last percentage this connection believed, for the glitch re-read.</summary>
    private int _lastPercent = -1;

    public HidPlusPlusConnection(DeviceInfo device, string path, int input, int output, byte slot)
    {
        _device = device;
        _path = path;
        _input = input;
        _output = output;
        _slot = slot;
    }

    private static readonly Capability ChargingCapability = new()
    {
        Id = CapabilityIds.PowerCharging,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charging",
        Subtitle = "Over HID++ — a real flag, not a guess",
        Access = CapabilityAccess.Read,
    };

    public IReadOnlyList<Capability> Capabilities { get; } = [BatteryCapability, ChargingCapability];

    public IReadOnlyList<TransportInfo> Transports => _device.Transports;

    /// <summary>
    /// One conversation at a time per receiver.
    ///
    /// A session serialises its own device's reads, which is enough while a device owns
    /// its cable. A Lightspeed receiver does not work that way: a mouse and a keyboard
    /// paired to the same dongle are two devices sharing one HID path, and a
    /// request-and-reply protocol down a shared path cannot carry two questions at once —
    /// the replies interleave and are handed to whichever device asked last.
    ///
    /// Not theory. Once devices began polling side by side, a fully charged mouse
    /// reported 15% and raised a low-battery notification, having read an answer meant
    /// for something else. Devices on different receivers still run in parallel; only
    /// those sharing a path take turns.
    /// </summary>
    private static readonly Dictionary<string, SemaphoreSlim> Channels =
        new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim GateFor(string path)
    {
        lock (Channels)
        {
            if (Channels.TryGetValue(path, out SemaphoreSlim? gate)) return gate;

            gate = new SemaphoreSlim(1, 1);
            Channels[path] = gate;
            return gate;
        }
    }

    public async Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (capabilityId is not (CapabilityIds.PowerBattery or CapabilityIds.PowerCharging))
        {
            return CapabilityValue.Absent;
        }

        SemaphoreSlim gate = GateFor(_path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var channel = new WindowsHidChannel(_path, _input, _output);

            if (HidPlusPlusReader.Battery(channel, _slot) is not { } charge)
            {
                return CapabilityValue.Absent;
            }

            // A reading thirty points below the last one is asked again before it is
            // believed. The receiver path is shared and busy, and a single garbled
            // exchange produced a phantom 15% from a charged mouse; a genuine drop
            // simply answers the same twice.
            if (_lastPercent >= 0
                && _lastPercent - charge.Percent > 30
                && HidPlusPlusReader.Battery(channel, _slot) is { } second)
            {
                charge = second;
            }

            _lastPercent = charge.Percent;

            return capabilityId == CapabilityIds.PowerBattery
                ? CapabilityValue.Of(charge.Percent)
                : CapabilityValue.Of(charge.IsCharging);
        }
        catch (IOException)
        {
            // The receiver is momentarily someone else's, or gone. An absent reading is
            // the honest answer either way; presence is the discovery sweep's business.
            return CapabilityValue.Absent;
        }
        finally
        {
            gate.Release();
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
