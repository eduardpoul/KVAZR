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

    /// <summary>
    /// The halves of a device that has two batteries — a pair of earphones, and its case.
    ///
    /// Apart from <see cref="PowerBattery"/> rather than instead of it: the hero and the
    /// tray want one number for the device, and the one that matters is the emptier half,
    /// because the pair only lasts as long as its worse side.
    /// </summary>
    public const string PowerBatteryLeft = "power.battery.left";

    public const string PowerBatteryRight = "power.battery.right";

    public const string PowerBatteryCase = "power.battery.case";

    /// <summary>
    /// Whether each half is charging, which is not the same question for each of them: a
    /// case on a cable charges the earphones sitting in it while itself being charged, and
    /// one earphone out of the case is not charging while the other is.
    /// </summary>
    public const string PowerChargingLeft = "power.charging.left";

    public const string PowerChargingRight = "power.charging.right";

    public const string PowerChargingCase = "power.charging.case";
    public const string PowerAutoOff = "power.auto_off";
    public const string DeviceFirmware = "device.firmware";

    /// <summary>
    /// Which edition of a model this is, where the hardware tells them apart. An Audeze
    /// Maxwell is sold as an Xbox one and a PlayStation one and says which by its USB
    /// product id; a model with only one edition simply never declares this.
    /// </summary>
    public const string DeviceEdition = "device.edition";

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

    // Audio the operating system owns, as distinct from the device's own amplifier.
    // Both can sit on one page and they are not the same number: the Windows slider
    // scales what is sent to the device, the device's own gain decides how loudly it
    // plays what arrives. Calling either of them plain "volume" made the page
    // unreadable, so both say whose they are.
    public const string AudioSystemVolume = "audio.system_volume";
    public const string AudioSystemOn = "audio.system_on";

    // Microphone
    public const string MicMute = "mic.mute";
    public const string MicLevel = "mic.level";
    public const string MicSidetone = "mic.sidetone";
    public const string MicSidetoneLevel = "mic.sidetone_level";
    public const string MicSidetoneMax = "mic.sidetone_level_max";
    public const string MicNoiseReduction = "mic.noise_reduction";

    // The Windows side of a microphone: the recording level and the mute that every
    // audio device has, whatever protocol it does or does not speak.
    public const string MicSystemLevel = "mic.system_level";
    public const string MicSystemOn = "mic.system_on";

    /// <summary>
    /// What the microphone is hearing, right now. Only read while its page is open —
    /// reading it means listening, and listening is visible in the taskbar.
    /// </summary>
    public const string MicSystemPeak = "mic.system_peak";

    /// <summary>
    /// Which way a microphone listens: the selector on the back of a QuadCast.
    ///
    /// Not a system capability and it never will be — Windows has no notion of a polar
    /// pattern, and the dial is mechanical, so this is read from the device and never
    /// written to it.
    /// </summary>
    public const string MicPolarPattern = "mic.polar_pattern";

    // Game and chat
    public const string MixChat = "mix.chat";

    // Pointer
    public const string PointerDpi = "pointer.dpi";
    public const string PointerDpiXy = "pointer.dpi_xy";

    /// <summary>
    /// The ladder of resolutions a device stores and cycles through with a button of its
    /// own — five of them on the mice measured. One capability rather than five, because
    /// they live in one record and are written in one go.
    /// </summary>
    public const string PointerDpiSteps = "pointer.dpi_steps";
    public const string PointerReportRate = "pointer.report_rate";

    /// <summary>The sensor's own resolution, as against the pointer speed Windows keeps.</summary>
    public const string ButtonsMap = "buttons.map";

    /// <summary>How many buttons a pointer actually has, where the device will say.</summary>
    public const string ButtonsCount = "buttons.count";

    /// <summary>How far the sensor keeps reading as the mouse is lifted.</summary>
    public const string PointerLiftOff = "pointer.lift_off";

    /// <summary>How many settings profiles the device stores on board.</summary>
    public const string DeviceProfiles = "device.profiles";

    /// <summary>The device's own serial, where it has one — the only way to tell two apart.</summary>
    public const string DeviceSerial = "device.serial";

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

    /// <summary>How fast an effect runs. Only ever meaningful beside one that moves.</summary>
    public const string LightSpeed = "light.speed";

    // Profiles and maintenance
    public const string ProfilesOnboard = "profiles.onboard";
    public const string MaintFactoryReset = "maint.factory_reset";

    // Head tracking sits outside the shared vocabulary on purpose: it is not a
    // concept other vendors expose, and on this model it is not even confirmed to work.
    public const string SensorsHeadTracking = "sensors.head_tracking";
}
