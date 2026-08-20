using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Model;

/// <summary>How much of the device is reachable right now.</summary>
public enum ConnectionState
{
    /// <summary>Nothing is plugged in.</summary>
    NotFound,

    /// <summary>
    /// Only the wireless dongle is present. It carries audio but answers no RACE
    /// command, so the app can show the device and nothing else.
    /// </summary>
    MonitorOnly,

    /// <summary>A cable is present: every feature is readable and writable.</summary>
    Active,
}

/// <summary>Microphone noise reduction, as Audeze HQ presents it.</summary>
public enum NoiseReductionMode
{
    Off = 0,
    Low = 1,
    High = 2,
}

/// <summary>
/// An immutable snapshot of the headset. Every value is either a real reading or
/// <see cref="Unknown"/> — the UI must be able to tell "off" from "not read yet",
/// because showing a confident zero for something we never asked is a lie.
/// </summary>
public sealed record DeviceState
{
    /// <summary>Sentinel for a value that has not been read.</summary>
    public const int Unknown = -1;

    public static readonly DeviceState Empty = new();

    public ConnectionState Connection { get; init; } = ConnectionState.NotFound;

    /// <summary>The transport currently used for control, when there is one.</summary>
    public MaxwellTransport? ControlTransport { get; init; }

    /// <summary>True when a dongle is present, whether or not a cable also is.</summary>
    public bool DonglePresent { get; init; }

    /// <summary>Product string reported by the HID descriptor, when one is connected.</summary>
    public string? DeviceName { get; init; }

    public int BatteryPercent { get; init; } = Unknown;

    /// <summary>Inferred from the battery trend; the device exposes no charging flag we have identified.</summary>
    public bool IsCharging { get; init; }

    public int AmpGain { get; init; } = Unknown;
    public int LeftGain { get; init; } = Unknown;
    public int RightGain { get; init; } = Unknown;
    public int EarcupBalance { get; init; } = Unknown;

    public int ChatMix { get; init; } = Unknown;

    public NoiseReductionMode? NoiseReduction { get; init; }
    public bool? MicMuted { get; init; }
    public int MicLevel { get; init; } = Unknown;

    public bool? SidetoneEnabled { get; init; }
    public int SidetoneLevel { get; init; } = Unknown;
    public int MaxSidetoneLevel { get; init; } = Unknown;

    public int VoicePromptLevel { get; init; } = Unknown;
    public int EqPresetIndex { get; init; } = Unknown;
    public int AutoPowerOff { get; init; } = Unknown;

    public string? FirmwareVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public bool HasBattery => BatteryPercent >= 0;

    /// <summary>True when settings can be changed, not merely observed.</summary>
    public bool CanControl => Connection == ConnectionState.Active;

    /// <summary>
    /// The boom mic reports mute as 0xFF and refuses a level read when it is
    /// unplugged, so those two readings together mean "detached", not "broken".
    /// </summary>
    public bool BoomMicAttached => MicLevel >= 0;
}
