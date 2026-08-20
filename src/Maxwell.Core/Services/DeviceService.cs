using Maxwell.Core.Model;
using Maxwell.Race;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Maxwell.Core.Services;

/// <summary>
/// Owns the RACE session and the state snapshot. All device access is serialised
/// behind one gate; callers get a task, never a blocked UI thread.
/// </summary>
public sealed class DeviceService : IDeviceService, IDisposable
{
    private readonly IHidEnumerator _enumerator;
    private readonly IHidTransportFactory _factory;
    private readonly ILogger<DeviceService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private RaceSession? _session;
    private bool _disposed;

    public DeviceState State { get; private set; } = DeviceState.Empty;

    public event EventHandler<DeviceState>? StateChanged;

    public DeviceService(
        IHidEnumerator enumerator,
        IHidTransportFactory factory,
        ILogger<DeviceService> logger)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(logger);

        _enumerator = enumerator;
        _factory = factory;
        _logger = logger;
    }

    // ---------- discovery ----------

    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<MaxwellInterface> race = _enumerator.EnumerateRaceInterfaces();
            bool donglePresent = race.Any(i => i.IsDongle);

            // Control only works over a cable. The dongle enumerates and carries audio
            // but answers no RACE command, so it can be seen and not driven.
            MaxwellInterface? cable = race.FirstOrDefault(i => !i.IsDongle);

            if (cable is null)
            {
                CloseSession();
                Publish(DeviceState.Empty with
                {
                    Connection = donglePresent ? ConnectionState.MonitorOnly : ConnectionState.NotFound,
                    DonglePresent = donglePresent,
                    UpdatedAt = DateTimeOffset.Now,
                });
                return;
            }

            if (_session?.Descriptor.Path == cable.Path) return;

            CloseSession();

            try
            {
                _session = new RaceSession(_factory.Open(cable));
                _logger.OpenedDevice(cable.Transport, cable.Path);
            }
            catch (IOException ex)
            {
                // Another process — usually Audeze HQ — is holding the interface.
                _logger.CouldNotOpenDevice(ex, cable.Path);
                Publish(DeviceState.Empty with
                {
                    Connection = donglePresent ? ConnectionState.MonitorOnly : ConnectionState.NotFound,
                    DonglePresent = donglePresent,
                    UpdatedAt = DateTimeOffset.Now,
                });
                return;
            }

            Publish(State with
            {
                Connection = ConnectionState.Active,
                ControlTransport = cable.Transport,
                DeviceName = string.IsNullOrWhiteSpace(cable.ProductName) ? null : cable.ProductName,
                DonglePresent = donglePresent,
                UpdatedAt = DateTimeOffset.Now,
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- reads ----------

    public async Task RefreshBatteryAsync(CancellationToken cancellationToken = default)
    {
        RaceSession? session = _session;
        if (session is null) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int battery = await session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
            if (battery < 0) return;

            // No charging flag has been identified in the protocol, so it is inferred
            // from the trend. A single rising sample is enough while a cable is the
            // only control path we have — the cable is also what charges the headset.
            bool charging = State.BatteryPercent >= 0 && battery > State.BatteryPercent;

            Publish(State with
            {
                BatteryPercent = battery,
                IsCharging = charging || (State.IsCharging && battery >= State.BatteryPercent),
                UpdatedAt = DateTimeOffset.Now,
            });
        }
        catch (IOException ex)
        {
            _logger.BatteryReadFailed(ex);
            CloseSession();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        RaceSession? session = _session;
        if (session is null) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int battery = await session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
            int amp = await session.ReadValueAsync(MaxwellFeatures.AmpGain, cancellationToken).ConfigureAwait(false);
            int left = await session.ReadValueAsync(MaxwellFeatures.LeftGain, cancellationToken).ConfigureAwait(false);
            int right = await session.ReadValueAsync(MaxwellFeatures.RightGain, cancellationToken).ConfigureAwait(false);
            int balance = await session.ReadValueAsync(MaxwellFeatures.EarcupBalance, cancellationToken).ConfigureAwait(false);
            int noise = await session.ReadValueAsync(MaxwellFeatures.NoiseReduction, cancellationToken).ConfigureAwait(false);
            int sidetoneLevel = await session.ReadValueAsync(MaxwellFeatures.SidetoneLevel, cancellationToken).ConfigureAwait(false);
            int maxSidetone = await session.ReadValueAsync(MaxwellFeatures.MaxSidetoneLevel, cancellationToken).ConfigureAwait(false);
            int voice = await session.ReadValueAsync(MaxwellFeatures.VpLevel, cancellationToken).ConfigureAwait(false);
            int preset = await session.ReadValueAsync(MaxwellFeatures.PeqIndex, cancellationToken).ConfigureAwait(false);
            int micMute = await session.ReadValueAsync(MaxwellFeatures.MicMute, cancellationToken).ConfigureAwait(false);
            int micLevel = await session.ReadValueAsync(MaxwellFeatures.MicLevel, cancellationToken).ConfigureAwait(false);
            int chatMix = await session.ReadChatMixAsync(cancellationToken).ConfigureAwait(false);
            int sidetoneState = await session.ReadNamespace2CAsync(MaxwellFeatures.SidetoneState, cancellationToken).ConfigureAwait(false);
            int autoOff = await session.ReadNamespace2CAsync(MaxwellFeatures.AutoPowerOff, cancellationToken).ConfigureAwait(false);

            string? firmware = State.FirmwareVersion;
            if (firmware is null)
            {
                foreach (uint address in FirmwareAddresses.For(session.Descriptor.Transport))
                {
                    firmware = await session.ReadVersionStringAsync(address, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (firmware is not null) break;
                }
            }

            Publish(State with
            {
                BatteryPercent = battery >= 0 ? battery : State.BatteryPercent,
                AmpGain = amp,
                LeftGain = left,
                RightGain = right,
                EarcupBalance = balance,
                NoiseReduction = ToNoiseReduction(noise),
                SidetoneLevel = sidetoneLevel,
                MaxSidetoneLevel = maxSidetone,
                VoicePromptLevel = voice,
                EqPresetIndex = preset,

                // 0xFF is the boom mic reporting "absent" rather than "unmuted".
                MicMuted = micMute is >= 0 and <= 1 ? micMute == 1 : null,
                MicLevel = micLevel,
                ChatMix = chatMix,
                SidetoneEnabled = sidetoneState >= 0 ? sidetoneState != 0 : null,
                AutoPowerOff = autoOff,
                FirmwareVersion = firmware,
                UpdatedAt = DateTimeOffset.Now,
            });
        }
        catch (IOException ex)
        {
            _logger.FullRefreshFailed(ex);
            CloseSession();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static NoiseReductionMode? ToNoiseReduction(int raw) => raw switch
    {
        0 => NoiseReductionMode.Off,
        1 => NoiseReductionMode.Low,
        2 => NoiseReductionMode.High,
        _ => null,
    };

    // ---------- writes ----------

    public Task<bool> SetChatMixAsync(byte value, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteChatMixAsync(value, cancellationToken),
            state => state with { ChatMix = value }, cancellationToken);

    public Task<bool> SetNoiseReductionAsync(NoiseReductionMode mode, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteValueAsync(MaxwellFeatures.NoiseReduction, (byte)mode, cancellationToken),
            state => state with { NoiseReduction = mode }, cancellationToken);

    public Task<bool> SetSidetoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteNamespace2CAsync(MaxwellFeatures.SidetoneState, (byte)(enabled ? 1 : 0), cancellationToken),
            state => state with { SidetoneEnabled = enabled }, cancellationToken);

    public Task<bool> SetSidetoneLevelAsync(byte level, CancellationToken cancellationToken = default)
    {
        // The ceiling is read from the device rather than assumed; clamping to a
        // hardcoded number would silently misbehave on other firmware.
        if (State.MaxSidetoneLevel >= 0 && level > State.MaxSidetoneLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level),
                $"Sidetone level {level} exceeds the device maximum of {State.MaxSidetoneLevel}.");
        }

        return WriteAsync(s => s.WriteValueAsync(MaxwellFeatures.SidetoneLevel, level, cancellationToken),
            state => state with { SidetoneLevel = level }, cancellationToken);
    }

    public Task<bool> SetVoicePromptLevelAsync(byte level, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteValueAsync(MaxwellFeatures.VpLevel, level, cancellationToken),
            state => state with { VoicePromptLevel = level }, cancellationToken);

    public Task<bool> SetMicLevelAsync(byte level, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteValueAsync(MaxwellFeatures.MicLevel, level, cancellationToken),
            state => state with { MicLevel = level }, cancellationToken);

    public Task<bool> SetEqPresetAsync(byte index, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteValueAsync(MaxwellFeatures.PeqIndex, index, cancellationToken),
            state => state with { EqPresetIndex = index }, cancellationToken);

    public Task<bool> SetGainAsync(GainChannel channel, byte value, CancellationToken cancellationToken = default)
    {
        // Above the ceiling the coil can be damaged, and the two reserved codes are
        // dropped by the firmware without a word. Both are rejected here rather than
        // being allowed to fail silently on the device.
        if (value > RaceCommands.MaxGain)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Gain {value} exceeds the safe maximum of {RaceCommands.MaxGain}.");
        }

        if (RaceCommands.IsReservedGain(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"0x{value:X2} is a reserved firmware code and will not stick.");
        }

        byte sub = channel switch
        {
            GainChannel.Left => MaxwellFeatures.LeftGain,
            GainChannel.Right => MaxwellFeatures.RightGain,
            _ => MaxwellFeatures.AmpGain,
        };

        return WriteAsync(s => s.WriteValueAsync(sub, value, cancellationToken),
            state => channel switch
            {
                GainChannel.Left => state with { LeftGain = value },
                GainChannel.Right => state with { RightGain = value },
                _ => state with { AmpGain = value },
            },
            cancellationToken);
    }

    public Task<bool> SetEarcupBalanceAsync(byte value, CancellationToken cancellationToken = default) =>
        WriteAsync(s => s.WriteValueAsync(MaxwellFeatures.EarcupBalance, value, cancellationToken),
            state => state with { EarcupBalance = value }, cancellationToken);

    private async Task<bool> WriteAsync(
        Func<RaceSession, Task<bool>> write,
        Func<DeviceState, DeviceState> apply,
        CancellationToken cancellationToken)
    {
        RaceSession? session = _session;
        if (session is null) return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool ok = await write(session).ConfigureAwait(false);

            // Only reflect the change once the device confirmed it by reading back.
            if (ok) Publish(apply(State) with { UpdatedAt = DateTimeOffset.Now });
            return ok;
        }
        catch (IOException ex)
        {
            _logger.WriteFailed(ex);
            CloseSession();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- plumbing ----------

    private void Publish(DeviceState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void CloseSession()
    {
        _session?.Dispose();
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CloseSession();
        _gate.Dispose();
    }
}
