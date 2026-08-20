using Maxwell.Core.Devices;
using Maxwell.Race;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// Translates the capability vocabulary into RACE traffic.
///
/// This is the whole of the Audeze-specific mapping: everything below it is the tested
/// protocol layer, everything above it knows only capabilities. Keeping the seam this
/// thin is deliberate — the capability model is currently derived from one device, and
/// a second real driver will almost certainly show up something it got wrong.
/// </summary>
internal sealed class AudezeRaceConnection : IDeviceConnection
{
    private readonly RaceSession _session;
    private readonly IReadOnlyList<TransportInfo> _transports;

    private int _lastBattery = -1;
    private bool _charging;
    private string? _firmware;
    private int _sidetoneMax = -1;

    /// <summary>
    /// Set once a command reboots the headset. Everything afterwards reports absence
    /// rather than talking to a handle the device has already dropped.
    /// </summary>
    private bool _stale;

    private bool _disposed;

    public AudezeRaceConnection(RaceSession session, IReadOnlyList<TransportInfo> transports)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transports);

        _session = session;
        _transports = transports;
    }

    public IReadOnlyList<Capability> Capabilities => MaxwellCapabilities.All;

    public IReadOnlyList<TransportInfo> Transports => _transports;

    // ---------- reads ----------

    public async Task<CapabilityValue> ReadAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityId);
        if (_stale) return CapabilityValue.Absent;

        return capabilityId switch
        {
            CapabilityIds.PowerBattery => await ReadBatteryAsync(cancellationToken).ConfigureAwait(false),
            CapabilityIds.PowerCharging => _lastBattery < 0 ? CapabilityValue.Absent : CapabilityValue.Of(_charging),
            CapabilityIds.DeviceFirmware => await ReadFirmwareAsync(cancellationToken).ConfigureAwait(false),

            CapabilityIds.AudioVolume => await ScalarAsync(MaxwellFeatures.AmpGain, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioGainLeft => await ScalarAsync(MaxwellFeatures.LeftGain, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioGainRight => await ScalarAsync(MaxwellFeatures.RightGain, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioBalance => await ScalarAsync(MaxwellFeatures.EarcupBalance, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioVoicePrompts => await ScalarAsync(MaxwellFeatures.VpLevel, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioEqPreset => await ChoiceAsync(MaxwellFeatures.PeqIndex, cancellationToken).ConfigureAwait(false),

            CapabilityIds.MicMute => await ReadMicMuteAsync(cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicLevel => await ScalarAsync(MaxwellFeatures.MicLevel, cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicNoiseReduction => await ChoiceAsync(MaxwellFeatures.NoiseReduction, cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicSidetoneLevel => await ScalarAsync(MaxwellFeatures.SidetoneLevel, cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicSidetoneMax => await ReadSidetoneMaxAsync(cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicSidetone => await ReadFlag2CAsync(MaxwellFeatures.SidetoneState, cancellationToken).ConfigureAwait(false),

            CapabilityIds.PowerAutoOff => await Scalar2CAsync(MaxwellFeatures.AutoPowerOff, cancellationToken).ConfigureAwait(false),
            CapabilityIds.MixChat => await ReadChatMixAsync(cancellationToken).ConfigureAwait(false),

            // Write-only or unreachable: head tracking has no reader, the EQ band editor
            // has no command at all, and the two destructive actions are not values.
            _ => CapabilityValue.Absent,
        };
    }

    private async Task<CapabilityValue> ReadBatteryAsync(CancellationToken cancellationToken)
    {
        int battery = await _session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
        if (battery < 0) return CapabilityValue.Absent;

        // No charging flag has been identified in the protocol, so it is inferred from
        // the trend. One rising sample is enough while a cable is the only control path
        // we have — the cable is also what charges the headset.
        if (_lastBattery >= 0 && battery > _lastBattery) _charging = true;
        else if (_lastBattery >= 0 && battery < _lastBattery) _charging = false;

        _lastBattery = battery;
        return CapabilityValue.Of(battery);
    }

    private async Task<CapabilityValue> ReadFirmwareAsync(CancellationToken cancellationToken)
    {
        if (_firmware is not null) return CapabilityValue.Of(_firmware);

        foreach (uint address in FirmwareAddresses.For(_session.Descriptor.Transport))
        {
            _firmware = await _session.ReadVersionStringAsync(address, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (_firmware is not null) return CapabilityValue.Of(_firmware);
        }

        return CapabilityValue.Absent;
    }

    private async Task<CapabilityValue> ReadMicMuteAsync(CancellationToken cancellationToken)
    {
        int raw = await _session.ReadValueAsync(MaxwellFeatures.MicMute, cancellationToken).ConfigureAwait(false);

        // 0xFF is the boom mic saying "I am not here", not "not muted". Reporting it as
        // unmuted would be a confident lie about a microphone that is in a drawer.
        return raw is >= 0 and <= 1 ? CapabilityValue.Of(raw == 1) : CapabilityValue.Absent;
    }

    private async Task<CapabilityValue> ReadSidetoneMaxAsync(CancellationToken cancellationToken)
    {
        int raw = await _session.ReadValueAsync(MaxwellFeatures.MaxSidetoneLevel, cancellationToken)
            .ConfigureAwait(false);

        if (raw < 0) return CapabilityValue.Absent;

        _sidetoneMax = raw;
        return CapabilityValue.Of(raw);
    }

    private async Task<CapabilityValue> ReadChatMixAsync(CancellationToken cancellationToken)
    {
        int raw = await _session.ReadChatMixAsync(cancellationToken).ConfigureAwait(false);
        return raw < 0 ? CapabilityValue.Absent : CapabilityValue.Of(raw);
    }

    private async Task<CapabilityValue> ScalarAsync(byte sub, CancellationToken cancellationToken)
    {
        int raw = await _session.ReadValueAsync(sub, cancellationToken).ConfigureAwait(false);
        return raw < 0 ? CapabilityValue.Absent : CapabilityValue.Of(raw);
    }

    private async Task<CapabilityValue> ChoiceAsync(byte sub, CancellationToken cancellationToken)
    {
        int raw = await _session.ReadValueAsync(sub, cancellationToken).ConfigureAwait(false);
        return raw < 0 ? CapabilityValue.Absent : CapabilityValue.Choice(raw);
    }

    private async Task<CapabilityValue> Scalar2CAsync(byte sub, CancellationToken cancellationToken)
    {
        int raw = await _session.ReadNamespace2CAsync(sub, cancellationToken).ConfigureAwait(false);
        return raw < 0 ? CapabilityValue.Absent : CapabilityValue.Of(raw);
    }

    private async Task<CapabilityValue> ReadFlag2CAsync(byte sub, CancellationToken cancellationToken)
    {
        int raw = await _session.ReadNamespace2CAsync(sub, cancellationToken).ConfigureAwait(false);
        return raw < 0 ? CapabilityValue.Absent : CapabilityValue.Of(raw != 0);
    }

    // ---------- writes ----------

    /// <remarks>
    /// Two kinds of rejection, kept separate on purpose. A value outside the range the
    /// driver itself declared can only come from a caller bug, so it throws. A value
    /// inside the range that the firmware still refuses is something a user can reach
    /// by dragging a slider, so it returns false and surfaces at the control.
    /// </remarks>
    public async Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityId);
        ArgumentNullException.ThrowIfNull(value);

        if (_stale) return false;

        switch (capabilityId)
        {
            case CapabilityIds.AudioVolume:
                return await WriteGainAsync(MaxwellFeatures.AmpGain, value, cancellationToken).ConfigureAwait(false);
            case CapabilityIds.AudioGainLeft:
                return await WriteGainAsync(MaxwellFeatures.LeftGain, value, cancellationToken).ConfigureAwait(false);
            case CapabilityIds.AudioGainRight:
                return await WriteGainAsync(MaxwellFeatures.RightGain, value, cancellationToken).ConfigureAwait(false);

            case CapabilityIds.AudioBalance:
                return await WriteScalarAsync(MaxwellFeatures.EarcupBalance, value, 122, 134, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.AudioVoicePrompts:
                return await WriteScalarAsync(MaxwellFeatures.VpLevel, value, 0, 255, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.AudioEqPreset:
                return await WriteScalarAsync(MaxwellFeatures.PeqIndex, value, 0, 9, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.MicLevel:
                return await WriteScalarAsync(MaxwellFeatures.MicLevel, value, 0, 255, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.MicNoiseReduction:
                return await WriteScalarAsync(MaxwellFeatures.NoiseReduction, value, 0, 2, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.MicSidetoneLevel:
                return await WriteSidetoneLevelAsync(value, cancellationToken).ConfigureAwait(false);

            case CapabilityIds.MicSidetone:
                return await _session
                    .WriteNamespace2CAsync(MaxwellFeatures.SidetoneState, (byte)(value.AsFlag() ? 1 : 0), cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.SensorsHeadTracking:
                return await _session
                    .WriteValueAsync(MaxwellFeatures.SensorsEnabled, (byte)(value.AsFlag() ? 1 : 0), cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.MixChat:
                return await WriteChatMixAsync(value, cancellationToken).ConfigureAwait(false);

            case CapabilityIds.LinkPair:
                return await RunDestructiveAsync(
                    ct => _session.StartPairingAsync(confirmed: true, ct), cancellationToken).ConfigureAwait(false);

            case CapabilityIds.MaintFactoryReset:
                return await RunDestructiveAsync(
                    ct => _session.FactoryResetAsync(confirmed: true, ct), cancellationToken).ConfigureAwait(false);

            default:
                return false;
        }
    }

    private async Task<bool> WriteGainAsync(byte sub, CapabilityValue value, CancellationToken cancellationToken)
    {
        int raw = Require(value, 0, RaceCommands.MaxGain);

        // 0x88 and 0x8E are inside the slider's travel but the firmware drops them
        // without a word, so a write that "succeeded" would leave the UI showing a
        // value the device never took.
        if (RaceCommands.IsReservedGain(raw)) return false;

        return await _session.WriteValueAsync(sub, (byte)raw, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WriteSidetoneLevelAsync(CapabilityValue value, CancellationToken cancellationToken)
    {
        // The ceiling is whatever the device reported, never a constant.
        int ceiling = _sidetoneMax >= 0 ? _sidetoneMax : 31;
        int raw = Require(value, 0, ceiling);

        return await _session.WriteValueAsync(MaxwellFeatures.SidetoneLevel, (byte)raw, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> WriteChatMixAsync(CapabilityValue value, CancellationToken cancellationToken)
    {
        int raw = Require(value, 0, RaceCommands.ChatMixMax);
        return await _session.WriteChatMixAsync((byte)raw, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WriteScalarAsync(
        byte sub,
        CapabilityValue value,
        int min,
        int max,
        CancellationToken cancellationToken)
    {
        int raw = Require(value, min, max);
        return await _session.WriteValueAsync(sub, (byte)raw, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> RunDestructiveAsync(
        Func<CancellationToken, Task<bool>> send,
        CancellationToken cancellationToken)
    {
        bool sent = await send(cancellationToken).ConfigureAwait(false);

        // The headset reboots whether or not it answered, so this handle is finished.
        _stale = true;
        return sent;
    }

    private static int Require(CapabilityValue value, int min, int max)
    {
        int raw = value switch
        {
            NumberValue n => n.Value,
            ChoiceValue c => c.Index,
            FlagValue f => f.Value ? 1 : 0,
            _ => throw new ArgumentException("Value carries no number.", nameof(value)),
        };

        return raw >= min && raw <= max
            ? raw
            : throw new ArgumentOutOfRangeException(nameof(value), raw, $"Outside the declared range {min}..{max}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _session.Dispose();
    }
}
