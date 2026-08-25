using System.Runtime.Versioning;

using Maxwell.Core.Devices;
using Maxwell.Core.Model;
using Maxwell.Race;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// Translates the capability vocabulary into RACE traffic.
///
/// This is the whole of the Audeze-specific mapping: everything below it is the tested
/// protocol layer, everything above it knows only capabilities. Keeping the seam this
/// thin is deliberate — the capability model is currently derived from one device, and
/// a second real driver will almost certainly show up something it got wrong.
/// </summary>
[SupportedOSPlatform("windows")]
/// <remarks>
/// Not an <c>IDeviceNotifier</c>, and that was measured rather than assumed. The wheel
/// under the wearer's finger does report itself, but it drives the host's volume and not
/// anything we read; the mute was the one thing worth subscribing for, and the boom
/// announces nothing on any of the three HID interfaces. A connection that raises no
/// event is better named than one that raises none while claiming it might.
/// </remarks>
internal sealed class AudezeRaceConnection : IDeviceConnection
{
    private readonly RaceSession _session;
    private readonly IReadOnlyList<TransportInfo> _transports;

    /// <summary>
    /// Whether the headset is on a cable, which nothing in the protocol says: it is read
    /// off the trend in the battery percentage, and the whole of that guess lives apart
    /// from here so it can be run against a made-up series of readings.
    /// </summary>
    private readonly ChargeTrend _charge = new();

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

    /// <summary>
    /// The amp gain, because the headset answers it and the dongle does not.
    ///
    /// Measured with the headset switched off: the battery still reads 94% and the
    /// firmware still reads 1.0.1.74, both out of the dongle, while the gain, the earcup
    /// gains and the ChatMix all answer "no reply". Anything kept in the dongle describes
    /// the dongle.
    /// </summary>
    public string Heartbeat => CapabilityIds.AudioVolume;

    public async Task<CapabilityValue> ReadAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityId);
        if (_stale) return CapabilityValue.Absent;

        return capabilityId switch
        {
            CapabilityIds.PowerBattery => await ReadBatteryAsync(cancellationToken).ConfigureAwait(false),
            CapabilityIds.PowerCharging => _charge.HasReading
                ? CapabilityValue.Of(_charge.IsCharging(DateTimeOffset.UtcNow))
                : CapabilityValue.Absent,
            CapabilityIds.DeviceFirmware => await ReadFirmwareAsync(cancellationToken).ConfigureAwait(false),
            CapabilityIds.DeviceEdition => Edition(),

            CapabilityIds.AudioVolume => Awake(
                await ScalarAsync(MaxwellFeatures.AmpGain, cancellationToken).ConfigureAwait(false)),
            CapabilityIds.AudioGainLeft => await ScalarAsync(MaxwellFeatures.LeftGain, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioGainRight => await ScalarAsync(MaxwellFeatures.RightGain, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioBalance => await ScalarAsync(MaxwellFeatures.EarcupBalance, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioVoicePrompts => await ScalarAsync(MaxwellFeatures.VpLevel, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioEqPreset => await ChoiceAsync(MaxwellFeatures.PeqIndex, cancellationToken).ConfigureAwait(false),
            CapabilityIds.AudioEqGraphic => await ReadEqCurveAsync(cancellationToken).ConfigureAwait(false),

            CapabilityIds.MicNoiseReduction => await ChoiceAsync(MaxwellFeatures.NoiseReduction, cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicSidetoneLevel => await ScalarAsync(MaxwellFeatures.SidetoneLevel, cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicSidetoneMax => await ReadSidetoneMaxAsync(cancellationToken).ConfigureAwait(false),
            CapabilityIds.MicSidetone => await ReadFlag2CAsync(MaxwellFeatures.SidetoneState, cancellationToken).ConfigureAwait(false),

            CapabilityIds.PowerAutoOff => AutoOffChoice(
                await _session.ReadAutoPowerOffAsync(cancellationToken).ConfigureAwait(false)),
            CapabilityIds.MixChat => await ReadChatMixAsync(cancellationToken).ConfigureAwait(false),

            // Not values: the factory reset is an action, and nothing else here reads.
            _ => CapabilityValue.Absent,
        };
    }

    /// <summary>
    /// The timeouts offered, in seconds, in the order the choice list declares them.
    /// Real seconds rather than an invented ladder: the headset stores a sixteen-bit
    /// count, so these are the values it is actually given.
    /// </summary>
    internal static readonly int[] AutoOffSeconds = [0, 60, 300, 600, 900, 1200, 1800, 2700, 3600];

    /// <summary>
    /// Turns the headset's seconds into the option that matches it. A timeout set by some
    /// other application to a value not on this list reads as absent rather than being
    /// rounded to a neighbour — showing "30 minutes" for a device holding 25 would be a
    /// quiet lie, and the next write would make it true.
    /// </summary>
    private static CapabilityValue AutoOffChoice(int seconds)
    {
        if (seconds < 0) return CapabilityValue.Absent;

        int index = Array.IndexOf(AutoOffSeconds, seconds);
        return index < 0 ? CapabilityValue.Absent : CapabilityValue.Choice(index);
    }

    /// <summary>
    /// Xbox or PlayStation, straight from the USB product id — the two editions carry
    /// different ones on both the dongle and the cable, so this costs no request at all.
    /// A limited or anniversary edition is a paint job and shares its id with the ordinary
    /// one, so it is not claimed here.
    /// </summary>
    private CapabilityValue Edition() => _session.Descriptor.Transport switch
    {
        MaxwellTransport.XboxDongle or MaxwellTransport.XboxUsbC => CapabilityValue.Of("Xbox"),
        MaxwellTransport.PlayStationDongle or MaxwellTransport.PlayStationUsbC =>
            CapabilityValue.Of("PlayStation"),
        _ => CapabilityValue.Absent,
    };

    /// <summary>
    /// The heartbeat's answer, and what its silence means for everything inferred from a
    /// run of readings.
    ///
    /// The battery goes on arriving from the dongle while the headset sleeps, and while
    /// it sleeps nothing is polled at all — so the readings either side of a nap have an
    /// unknown time between them and possibly a cable. That is not a trend, and reading
    /// it as one lights a bolt over a headset that has simply been switched back on.
    /// </summary>
    private CapabilityValue Awake(CapabilityValue gain)
    {
        if (gain.IsAbsent) _charge.Lost();

        return gain;
    }

    private async Task<CapabilityValue> ReadBatteryAsync(CancellationToken cancellationToken)
    {
        int battery = await _session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
        if (battery < 0) return CapabilityValue.Absent;

        _charge.Saw(battery, DateTimeOffset.UtcNow);

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

    /// <summary>
    /// The bands of whichever user slot is selected, or absent.
    ///
    /// Only a user slot has bands. On a factory preset the honest answer is that there is
    /// no curve to show rather than a flat one — the six built-in presets are shaped
    /// curves themselves, and drawing them as ten zeroes would be a lie the wearer could
    /// hear.
    /// </summary>
    /// <summary>The curve last applied to the running filter, and the preset it was applied over.</summary>
    private int[]? _appliedCurve;
    private int _appliedForPreset = -1;

    private async Task<CapabilityValue> ReadEqCurveAsync(CancellationToken cancellationToken)
    {
        int preset = await _session.ReadValueAsync(MaxwellFeatures.PeqIndex, cancellationToken)
            .ConfigureAwait(false);

        // What the headset is playing, when that is something we put there.
        //
        // A curve is applied to the running filter and stored nowhere, so reading the slot
        // back reports whatever was saved before the edit — which is not what is coming out
        // of the drivers. Left that way the first poll after a drag overwrites the wearer's
        // own curve with the old one, and the plot springs back a second after they let go.
        //
        // Held only for as long as it is true: switching preset replaces the filter, and
        // the answer goes back to the device's.
        if (_appliedCurve is { } applied && preset == _appliedForPreset)
        {
            return CapabilityValue.Vector([.. applied.Select(CapabilityValue.Of)]);
        }

        _appliedCurve = null;

        int slot = RaceCommands.EqSlotOfPreset(preset);
        if (slot < 0)
        {
            // A factory preset keeps no bands on the device — but we know its curve, read
            // out of Audeze HQ's own table. Drawing it is the truth about what is playing;
            // an empty plot would only be the truth about where the numbers came from.
            return EqPresets.ByIndex(preset)?.Curve is { } factory
                ? CapabilityValue.Vector([.. factory.Select(CapabilityValue.Of)])
                : CapabilityValue.Absent;
        }

        MaxwellEqReply? reply = await _session.ReadEqBandsAsync((byte)slot, cancellationToken)
            .ConfigureAwait(false);

        if (reply?.Bands is not { } curve) return CapabilityValue.Absent;

        return CapabilityValue.Vector(
            [.. curve.Bands.Select(band => CapabilityValue.Of((int)decimal.Round(band.Gain)))]);
    }

    /// <summary>
    /// Applies a curve to the running filter.
    ///
    /// Live only: this is what the wearer hears while a slider moves, and it survives
    /// until the headset is power-cycled or its preset changes. Nothing is written to
    /// non-volatile memory, so a curve applied here is not a preset and does not pretend
    /// to be one.
    /// </summary>
    private async Task<bool> WriteEqCurveAsync(CapabilityValue value, CancellationToken cancellationToken)
    {
        if (!AirohaPeqBridge.Available) return false;

        IReadOnlyList<CapabilityValue> bands = value.AsVector();
        if (bands.Count != AirohaPeqBridge.Bands.Length) return false;

        var gains = new int[bands.Count];
        for (int i = 0; i < bands.Count; i++)
        {
            // A band nobody has answered for is not a zero. Refusing the whole write is
            // the only safe reading: filling the gap with flat would quietly undo whatever
            // is actually set on the device.
            if (bands[i] is not NumberValue number) return false;
            gains[i] = Math.Clamp(number.Value, -12, 12);
        }

        EqApplyResult result = await _session
            .ApplyEqCoefficientsAsync(AirohaPeqBridge.Blob(gains), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Accepted) return false;

        _appliedCurve = gains;
        _appliedForPreset = await _session
            .ReadValueAsync(MaxwellFeatures.PeqIndex, cancellationToken)
            .ConfigureAwait(false);

        return true;
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
                return await WriteScalarAsync(
                        MaxwellFeatures.VpLevel, value, 0, MaxwellFeatures.VpLevelMax, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.AudioEqGraphic:
                return await WriteEqCurveAsync(value, cancellationToken).ConfigureAwait(false);

            case CapabilityIds.AudioEqPreset:
                return await WriteScalarAsync(MaxwellFeatures.PeqIndex, value, 0, 9, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.MicLevel:
                return await WriteScalarAsync(MaxwellFeatures.MicLevel, value, 0, 255, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.MicNoiseReduction:
                return await WriteScalarAsync(MaxwellFeatures.NoiseReduction, value, 0, 2, cancellationToken)
                    .ConfigureAwait(false);

            case CapabilityIds.PowerAutoOff:
            {
                if (value is not ChoiceValue choice) return false;
                if (choice.Index < 0 || choice.Index >= AutoOffSeconds.Length) return false;

                return await _session
                    .WriteAutoPowerOffAsync(AutoOffSeconds[choice.Index], cancellationToken)
                    .ConfigureAwait(false);
            }

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
