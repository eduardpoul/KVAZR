namespace Maxwell.Core.Settings;

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

    /// <summary>
    /// Capability ids the hardware has accepted a write for, keyed by device. These are
    /// promotions from "command known, never tested" to verified: once a device has
    /// taken a value, the amber mark must not come back on the next launch.
    /// </summary>
    public Dictionary<string, List<string>> ConfirmedCapabilities { get; set; } =
        new(StringComparer.Ordinal);

    public AppSettings Clone() => new()
    {
        ConfirmedCapabilities = ConfirmedCapabilities.ToDictionary(
            p => p.Key, p => new List<string>(p.Value), StringComparer.Ordinal),
        Theme = Theme,
        RunAtLogin = RunAtLogin,
        StartMinimised = StartMinimised,
        ShowPercentageInTray = ShowPercentageInTray,
        ShowConnectPopup = ShowConnectPopup,
        NotifyLowBattery = NotifyLowBattery,
        LowBatteryThreshold = LowBatteryThreshold,
        NotifyChargeComplete = NotifyChargeComplete,
        Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal),
        Profiles = Profiles.Select(p => p.Clone()).ToList(),
        AutoSwitchProfiles = AutoSwitchProfiles,
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
