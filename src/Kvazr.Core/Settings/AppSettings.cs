namespace Kvazr.Core.Settings;

/// <summary>Which theme the window should follow.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Everything the application remembers between runs. Device settings are NOT here —
/// those live on the headset itself and are read back from it, so duplicating them
/// would just create two sources of truth that drift.
/// </summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool RunAtLogin { get; set; }

    public bool StartMinimised { get; set; } = true;

    /// <summary>
    /// Interface language as a culture name. English by default, and English for anything
    /// unrecognised: it is the language the product is written in, so it is the one thing
    /// that is always complete. Applied without a restart, which is worth saying on the
    /// screen because most applications that offer this ask for one.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Closing the window puts it in the tray instead of quitting. On by default, because
    /// a tray utility that exits when its window is closed stops doing the one thing it
    /// was installed for, and does so silently.
    /// </summary>
    public bool MinimiseOnClose { get; set; } = true;

    /// <summary>
    /// How often devices are asked, in seconds. Lives here rather than only in
    /// configuration because the artboard puts it on the settings screen, and the trade
    /// is real and personal: more often means a fresher number and a flatter battery in
    /// the device. Zero means take the configured default.
    /// </summary>
    public int PollingSeconds { get; set; }

    /// <summary>Show a popup when the headset connects.</summary>
    /// <summary>Dead since the popup was removed; kept so existing files deserialise.</summary>
    public bool ShowConnectPopup { get; set; } = true;

    public bool NotifyLowBattery { get; set; } = true;

    /// <summary>
    /// The level the user is warned at. Lower thresholds still fire — a drop straight
    /// past several reports only the most urgent one.
    /// </summary>
    public int LowBatteryThreshold { get; set; } = 15;

    public bool NotifyChargeComplete { get; set; } = true;

    /// <summary>Global hotkeys, keyed by action id. Empty string means unassigned.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new(StringComparer.Ordinal)
    {
        ["chatmix.up"] = "Ctrl+Alt+Up",
        ["chatmix.down"] = "Ctrl+Alt+Down",
        ["noise.cycle"] = "Ctrl+Alt+N",
        ["sidetone.toggle"] = "Ctrl+Alt+S",
        ["preset.next"] = "Ctrl+Alt+E",
        ["window.show"] = "Ctrl+Alt+Space",
    };

    /// <summary>Named device presets the user has saved.</summary>
    public List<DeviceProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Equaliser curves the user has drawn and named. Here rather than on the headset
    /// because a name does not fit in one of its four user slots; see
    /// <see cref="EqCurvePreset"/> for what that costs.
    /// </summary>
    public List<EqCurvePreset> EqPresets { get; set; } = [];

    /// <summary>Switch profiles automatically when a matching process starts.</summary>
    public bool AutoSwitchProfiles { get; set; }

    /// <summary>Ask Audeze once a week whether newer firmware has been published.</summary>
    public bool CheckFirmwareUpdates { get; set; } = true;

    /// <summary>When the last successful check ran. Null means it never has.</summary>
    public DateTimeOffset? LastFirmwareCheck { get; set; }

    /// <summary>
    /// The published version the user has already been told about, so the notice fires
    /// once per release rather than once per check.
    /// </summary>
    public string? AnnouncedFirmware { get; set; }

    /// <summary>
    /// What the last check found, so the screen has something to show on the next launch.
    /// Without this the section sits empty until the weekly interval comes round again,
    /// which is up to seven days of a page that looks broken.
    /// </summary>
    public string? LatestFirmware { get; set; }

    /// <summary>Versions Audeze published at the last check, newest first.</summary>
    public List<string> AvailableFirmware { get; set; } = [];

    /// <summary>
    /// Capability ids the hardware has accepted a write for, keyed by device. These are
    /// promotions from "command known, never tested" to verified: once a device has
    /// taken a value, the amber mark must not come back on the next launch.
    /// </summary>
    public Dictionary<string, List<string>> ConfirmedCapabilities { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The device the tray icon and the global shortcuts act on, chosen by hand.
    ///
    /// Artboard 4c settles what was a rule: "the primary is the device chosen by hand".
    /// A rule that picks the first controllable device is right about as often as it is
    /// wrong, and it changes its mind when something else connects — which is the worst
    /// property for the one device a shortcut acts on. Empty means fall back to the rule.
    /// </summary>
    public string PrimaryDeviceKey { get; set; } = string.Empty;

    /// <summary>
    /// Devices the user has put away, and when. Hidden is not forgotten: the pairing in
    /// Windows is untouched and the device returns intact when it is unhidden. The date
    /// is kept because a list of hidden things with no dates is impossible to reason
    /// about six months later — "the keyboard in the drawer" needs a when.
    /// </summary>
    public Dictionary<string, DateTimeOffset> HiddenDevices { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Devices the user has said are gone, and when.
    ///
    /// Different from hidden, and the difference is what happens next. Hidden is about a
    /// device that is here: it stays put away until it is asked for back. Removed is
    /// about one that is not — a gamepad unplugged and thrown out, an old headset — and
    /// nothing on this machine can know whether it will ever be plugged in again. So the
    /// row goes now and the entry lasts exactly as long as the absence: connect the thing
    /// again and it comes back as if new, because a device manager that ignores hardware
    /// somebody is holding is worse than one that shows a row too many.
    /// </summary>
    public Dictionary<string, DateTimeOffset> RemovedDevices { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Put anonymous devices away without being asked. A machine with a car stereo and a
    /// neighbour's speaker paired shows them on first launch, and a list where several
    /// rows are bare addresses teaches the user to distrust the whole list.
    /// </summary>
    public bool HideSilentDevices { get; set; } = true;

    /// <summary>
    /// Microphones whose headphone socket the tray panel carries as well, by device key.
    ///
    /// Off by default and asked for per device, because it is a trade of space rather
    /// than a preference about correctness: the panel is 360 pixels wide and every row it
    /// gains is a row something else loses. Somebody who monitors themselves while
    /// recording wants that slider two clicks closer than the window; somebody who has
    /// never plugged headphones into their microphone would be giving up half the panel
    /// for a control that does nothing.
    /// </summary>
    public HashSet<string> MonitoringInTray { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Show the developer section, and remember that it was asked for.
    ///
    /// Off, and not discoverable by accident: what it reveals is a way to switch off the
    /// driver that understands your headset, which is the right tool for building the
    /// tier underneath and a very effective way for anybody else to break their own
    /// installation.
    /// </summary>
    /// <summary>
    /// What drivers have written down about particular devices, keyed by device and then
    /// by whatever slot the driver chose.
    ///
    /// Almost nothing needs this: a page is read from the hardware, which is the right way
    /// round. The exception is a setting the hardware cannot keep — a QuadCast's lighting
    /// has no home in the microphone at all, so a colour somebody chose exists nowhere
    /// unless it is written here.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> DeviceMemory { get; set; } =
        new(StringComparer.Ordinal);

    public bool DeveloperMode { get; set; }

    /// <summary>
    /// Drivers and contributors the developer section has switched off, by the ids they
    /// carry themselves.
    ///
    /// Persisted, because the comparison being made with them is not a five-minute one:
    /// the point is to live with the baseline page for a while and find what it is
    /// missing. Ids of things this build no longer contains are kept rather than pruned
    /// — they cost nothing, and dropping them would silently re-enable a library on the
    /// next launch after a version where it happened to be absent.
    /// </summary>
    public HashSet<string> DisabledLibraries { get; set; } = new(StringComparer.Ordinal);

    public AppSettings Clone() => new()
    {
        ConfirmedCapabilities = ConfirmedCapabilities.ToDictionary(
            p => p.Key, p => new List<string>(p.Value), StringComparer.Ordinal),
        HiddenDevices = new Dictionary<string, DateTimeOffset>(HiddenDevices, StringComparer.Ordinal),
        RemovedDevices = new Dictionary<string, DateTimeOffset>(RemovedDevices, StringComparer.Ordinal),
        HideSilentDevices = HideSilentDevices,
        MonitoringInTray = new HashSet<string>(MonitoringInTray, StringComparer.Ordinal),
        DeveloperMode = DeveloperMode,
        DeviceMemory = DeviceMemory.ToDictionary(
            p => p.Key,
            p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal),
            StringComparer.Ordinal),
        DisabledLibraries = new HashSet<string>(DisabledLibraries, StringComparer.Ordinal),
        PrimaryDeviceKey = PrimaryDeviceKey,
        Theme = Theme,
        RunAtLogin = RunAtLogin,
        StartMinimised = StartMinimised,
        Language = Language,
        MinimiseOnClose = MinimiseOnClose,
        PollingSeconds = PollingSeconds,
        ShowConnectPopup = ShowConnectPopup,
        NotifyLowBattery = NotifyLowBattery,
        LowBatteryThreshold = LowBatteryThreshold,
        NotifyChargeComplete = NotifyChargeComplete,
        Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal),
        Profiles = Profiles.Select(p => p.Clone()).ToList(),
        AutoSwitchProfiles = AutoSwitchProfiles,
        CheckFirmwareUpdates = CheckFirmwareUpdates,
        LastFirmwareCheck = LastFirmwareCheck,
        AnnouncedFirmware = AnnouncedFirmware,
        LatestFirmware = LatestFirmware,
        AvailableFirmware = [.. AvailableFirmware],
    };
}

/// <summary>Persists <see cref="AppSettings"/> and tells everyone when they change.</summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persist the current values and raise <see cref="Changed"/>.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
