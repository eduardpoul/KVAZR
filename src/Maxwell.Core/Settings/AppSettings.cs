namespace Maxwell.Core.Settings;

/// <summary>
/// Whose battery the tray icon shows when several devices are attached.
///
/// Artboard 4c drew both answers and refused to pick; artboard 7a settles it by making it
/// a choice, with the primary device as the default. The two are genuinely different
/// promises: one number that stays put and can be learned, or one that is always the most
/// urgent and therefore always moving.
/// </summary>
public enum TrayPercentSource
{
    /// <summary>The chosen device. Predictable — the user knows whose number that is.</summary>
    Primary,

    /// <summary>Whichever is closest to flat. The warning cannot be missed; the number jumps.</summary>
    Lowest,
}

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

    /// <summary>Draw the battery percentage into the tray icon rather than a plain glyph.</summary>
    public bool ShowPercentageInTray { get; set; } = true;

    /// <summary>Whose battery the tray icon draws when several devices are attached.</summary>
    public TrayPercentSource TrayPercentFrom { get; set; } = TrayPercentSource.Primary;

    /// <summary>
    /// Interface language as a culture name. Empty means follow the system. Applied
    /// without a restart, which is worth saying on the screen because most applications
    /// that offer this ask for one.
    /// </summary>
    public string Language { get; set; } = string.Empty;

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
    /// Devices the user has put away, and when. Hidden is not forgotten: the pairing in
    /// Windows is untouched and the device returns intact when it is unhidden. The date
    /// is kept because a list of hidden things with no dates is impossible to reason
    /// about six months later — "the keyboard in the drawer" needs a when.
    /// </summary>
    public Dictionary<string, DateTimeOffset> HiddenDevices { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Put anonymous devices away without being asked. A machine with a car stereo and a
    /// neighbour's speaker paired shows them on first launch, and a list where several
    /// rows are bare addresses teaches the user to distrust the whole list.
    /// </summary>
    public bool HideSilentDevices { get; set; } = true;

    public AppSettings Clone() => new()
    {
        ConfirmedCapabilities = ConfirmedCapabilities.ToDictionary(
            p => p.Key, p => new List<string>(p.Value), StringComparer.Ordinal),
        HiddenDevices = new Dictionary<string, DateTimeOffset>(HiddenDevices, StringComparer.Ordinal),
        HideSilentDevices = HideSilentDevices,
        Theme = Theme,
        RunAtLogin = RunAtLogin,
        StartMinimised = StartMinimised,
        ShowPercentageInTray = ShowPercentageInTray,
        TrayPercentFrom = TrayPercentFrom,
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
