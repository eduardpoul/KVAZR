using Kvazr.Core.Model;

namespace Kvazr.Core.Services;

/// <summary>
/// The single owner of the headset. Every read and write in the application goes
/// through here — RACE does not tolerate overlapping requests, so nothing else may
/// hold the device. UI code must never touch the transport directly.
/// </summary>
public interface IDeviceService
{
    /// <summary>The most recent snapshot. Never null; starts as <see cref="DeviceState.Empty"/>.</summary>
    DeviceState State { get; }

    event EventHandler<DeviceState>? StateChanged;

    /// <summary>Re-scan for hardware and reconnect if the situation changed.</summary>
    Task DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Read the battery only. Cheap enough for the polling loop.</summary>
    Task RefreshBatteryAsync(CancellationToken cancellationToken = default);

    /// <summary>Read every feature. Used when a view opens, not on a timer.</summary>
    Task RefreshAllAsync(CancellationToken cancellationToken = default);

    Task<bool> SetChatMixAsync(byte value, CancellationToken cancellationToken = default);

    Task<bool> SetNoiseReductionAsync(NoiseReductionMode mode, CancellationToken cancellationToken = default);

    Task<bool> SetSidetoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<bool> SetSidetoneLevelAsync(byte level, CancellationToken cancellationToken = default);

    Task<bool> SetVoicePromptLevelAsync(byte level, CancellationToken cancellationToken = default);

    /// <summary>Boom microphone level. Only answers while the boom mic is attached.</summary>
    Task<bool> SetMicLevelAsync(byte level, CancellationToken cancellationToken = default);

    Task<bool> SetEqPresetAsync(byte index, CancellationToken cancellationToken = default);

    /// <summary>Master, left and right amplifier gain. Rejected above the safe ceiling.</summary>
    Task<bool> SetGainAsync(GainChannel channel, byte value, CancellationToken cancellationToken = default);

    Task<bool> SetEarcupBalanceAsync(byte value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wipes the headset back to factory settings. Irreversible, and it takes any
    /// custom firmware balance with it. The flag must be passed deliberately.
    /// </summary>
    Task<bool> FactoryResetAsync(bool confirmed, CancellationToken cancellationToken = default);

    /// <summary>Drops the dongle pairing and re-enters pairing mode.</summary>
    Task<bool> StartPairingAsync(bool confirmed, CancellationToken cancellationToken = default);
}

public enum GainChannel
{
    Master,
    Left,
    Right,
}
