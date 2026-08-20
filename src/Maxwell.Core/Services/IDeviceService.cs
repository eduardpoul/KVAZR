using Maxwell.Core.Model;

namespace Maxwell.Core.Services;

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

    Task<bool> SetEqPresetAsync(byte index, CancellationToken cancellationToken = default);

    /// <summary>Master, left and right amplifier gain. Rejected above the safe ceiling.</summary>
    Task<bool> SetGainAsync(GainChannel channel, byte value, CancellationToken cancellationToken = default);

    Task<bool> SetEarcupBalanceAsync(byte value, CancellationToken cancellationToken = default);
}

public enum GainChannel
{
    Master,
    Left,
    Right,
}
