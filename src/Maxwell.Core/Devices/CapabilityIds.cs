namespace Maxwell.Core.Devices;

/// <summary>
/// The shared capability vocabulary, from docs/CAPABILITIES.md.
///
/// These ids are not invented here. Audeze's RACE map, Logitech's HID++ feature set
/// and HeadsetControl's capability enum independently converged on the same set of
/// concepts, which is the whole reason a single shell can serve every vendor: a
/// headset from one vendor and a headset from another fill in the same slots.
///
/// A driver uses the id that matches the meaning, never one that merely looks close.
/// Two drivers reporting <see cref="MixChat"/> must mean the same thing to the user,
/// because the same control, the same hotkey and the same flyout row will drive both.
/// </summary>
public static class CapabilityIds
{
    // Power and identity
    public const string PowerBattery = "power.battery";
    public const string PowerCharging = "power.charging";
    public const string PowerAutoOff = "power.auto_off";
    public const string DeviceFirmware = "device.firmware";

    // Link
    public const string LinkTransport = "link.transport";
    public const string LinkSignal = "link.signal";
    public const string LinkHosts = "link.hosts";
    public const string LinkPair = "link.pair";

    // Audio
    public const string AudioVolume = "audio.volume";

    // Left and right stay two Range capabilities rather than becoming one RangeGroup.
    // The grouped primitive now exists and the stand-in mouse uses it, but the RACE
    // driver writes these through two separate registers with separate ceilings, and
    // presenting them as one control would promise an atomic write the protocol cannot
    // make. They are grouped on screen by their section, which is enough.
    public const string AudioGainLeft = "audio.gain_left";
    public const string AudioGainRight = "audio.gain_right";

    public const string AudioBalance = "audio.balance";
    public const string AudioEqPreset = "audio.eq_preset";
    public const string AudioEqGraphic = "audio.eq_graphic";
    public const string AudioVoicePrompts = "audio.voice_prompts";

    // Microphone
    public const string MicMute = "mic.mute";
    public const string MicLevel = "mic.level";
    public const string MicSidetone = "mic.sidetone";
    public const string MicSidetoneLevel = "mic.sidetone_level";
    public const string MicSidetoneMax = "mic.sidetone_level_max";
    public const string MicNoiseReduction = "mic.noise_reduction";

    // Game and chat
    public const string MixChat = "mix.chat";

    // Pointer
    public const string PointerDpi = "pointer.dpi";
    public const string PointerDpiXy = "pointer.dpi_xy";
    public const string PointerReportRate = "pointer.report_rate";
    public const string ButtonsMap = "buttons.map";

    // Like head tracking below, this one sits outside the shared vocabulary and is
    // marked as such deliberately. A sensor response curve is drawn in the design's
    // gamepad artboard and offered by several vendors' own applications, but neither
    // HID++ nor anything else we have read exposes it, so no driver can honestly claim
    // it. It exists so the curve primitive has a device page to live on before the
    // hardware that owns it arrives.
    public const string PointerResponse = "pointer.response";

    // Keyboard
    public const string KeysMap = "keys.map";
    public const string KeysLayers = "keys.layers";

    // Lighting
    public const string LightOn = "light.on";
    public const string LightBrightness = "light.brightness";
    public const string LightEffect = "light.effect";
    public const string LightColor = "light.color";

    // Profiles and maintenance
    public const string ProfilesOnboard = "profiles.onboard";
    public const string MaintFactoryReset = "maint.factory_reset";

    // Head tracking sits outside the shared vocabulary on purpose: it is not a
    // concept other vendors expose, and on this model it is not even confirmed to work.
    public const string SensorsHeadTracking = "sensors.head_tracking";
}
