using Kvazr.Core.Model;

namespace Kvazr.Core.Settings;

/// <summary>
/// A named set of headset settings.
///
/// Every field is nullable on purpose: a profile stores only what it should impose, so
/// one that just switches the EQ preset leaves the user's volume and balance alone.
/// A profile of non-nullable values would silently overwrite everything it touched.
/// </summary>
public sealed class DeviceProfile
{
    public string Name { get; set; } = "New profile";

    /// <summary>Executable name that activates this profile, without a path. Empty means manual only.</summary>
    public string TriggerProcess { get; set; } = "";

    public int? ChatMix { get; set; }
    public int? AmpGain { get; set; }
    public int? LeftGain { get; set; }
    public int? RightGain { get; set; }
    public int? EarcupBalance { get; set; }
    public int? SidetoneLevel { get; set; }
    public int? VoicePromptLevel { get; set; }
    public int? EqPresetIndex { get; set; }
    public NoiseReductionMode? NoiseReduction { get; set; }
    public bool? SidetoneEnabled { get; set; }

    /// <summary>How many settings this profile actually imposes.</summary>
    public int SettingCount =>
        (ChatMix.HasValue ? 1 : 0) +
        (AmpGain.HasValue ? 1 : 0) +
        (LeftGain.HasValue ? 1 : 0) +
        (RightGain.HasValue ? 1 : 0) +
        (EarcupBalance.HasValue ? 1 : 0) +
        (SidetoneLevel.HasValue ? 1 : 0) +
        (VoicePromptLevel.HasValue ? 1 : 0) +
        (EqPresetIndex.HasValue ? 1 : 0) +
        (NoiseReduction.HasValue ? 1 : 0) +
        (SidetoneEnabled.HasValue ? 1 : 0);

    /// <summary>Capture everything currently readable from the headset.</summary>
    public static DeviceProfile FromState(string name, DeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new DeviceProfile
        {
            Name = name,
            ChatMix = Value(state.ChatMix),
            AmpGain = Value(state.AmpGain),
            LeftGain = Value(state.LeftGain),
            RightGain = Value(state.RightGain),
            EarcupBalance = Value(state.EarcupBalance),
            SidetoneLevel = Value(state.SidetoneLevel),
            VoicePromptLevel = Value(state.VoicePromptLevel),
            EqPresetIndex = Value(state.EqPresetIndex),
            NoiseReduction = state.NoiseReduction,
            SidetoneEnabled = state.SidetoneEnabled,
        };
    }

    /// <summary>An unread value must not be captured, or applying the profile would write a guess.</summary>
    private static int? Value(int raw) => raw >= 0 ? raw : null;

    public DeviceProfile Clone() => (DeviceProfile)MemberwiseClone();
}
