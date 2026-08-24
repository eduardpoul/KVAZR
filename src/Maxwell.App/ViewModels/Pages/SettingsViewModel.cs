using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Maxwell.App.Localization;
using Maxwell.App.Platform;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// Everything that belongs to the application rather than to any device: artboard 7a,
/// plus the hidden-devices section from 13b.
///
/// Every property here writes straight through to the store and saves. There is no Apply
/// button and no Cancel, which is the convention the rest of Windows settles on and also
/// the honest one: a switch that has visibly moved but has not taken effect is a lie the
/// screen is telling, and the moment there is an Apply button somebody will close the
/// window without pressing it.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly IAutostart _autostart;
    private readonly IDeviceRegistry _registry;
    private readonly HiddenDevices _hidden;
    private readonly Localizer _text;

    /// <summary>
    /// Set while a change is being written back, so the store's own change notification
    /// does not come back round and re-enter every setter. Without it, saving one value
    /// re-applies all of them, which turns a single toggle into a full rewrite of the file.
    /// </summary>
    private bool _applying;

    private bool _disposed;

    public SettingsViewModel(
        ISettingsStore store,
        IAutostart autostart,
        IDeviceRegistry registry,
        HiddenDevices hidden,
        Localizer text)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autostart);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(text);

        _store = store;
        _autostart = autostart;
        _registry = registry;
        _hidden = hidden;
        _text = text;

        _hidden.Changed += OnHiddenChanged;
        _text.Changed += OnLanguageChanged;
        RefreshHidden();
    }

    public string Title => _text["page.settings", Maxwell.App.Localization.Localizer.Current.Say("Параметры")];

    /// <summary>The catalogue, for the screen that draws this.</summary>
    public Localizer Text => _text;

    // ---------- appearance ----------

    public int ThemeIndex
    {
        get => (int)_store.Current.Theme;
        set => Write(s => s.Theme = (AppTheme)value);
    }

    /// <summary>
    /// Each language named in itself. "Russian" under an English interface is the kind of
    /// thing that leaves somebody who does not read English unable to find their way back.
    /// </summary>
    public IReadOnlyList<string> Languages { get; } = ["Русский", "English"];

    public int LanguageIndex
    {
        get => Math.Max(0, Localizer.Cultures.ToList().IndexOf(_text.Culture));
        set
        {
            string culture = Localizer.Cultures[Math.Clamp(value, 0, Localizer.Cultures.Count - 1)];

            Write(s => s.Language = culture);

            // Applied here and now rather than at the next start. The catalogue is a
            // dictionary and every surface redraws on notice, so there is nothing a
            // restart would do that this does not.
            _text.Use(culture);
        }
    }

    /// <summary>The intervals, in the language in use.</summary>
    public IReadOnlyList<string> PollIntervalLabels =>
    [
        _text["poll.10s", Maxwell.App.Localization.Localizer.Current.Say("Каждые 10 с")],
        _text["poll.30s", Maxwell.App.Localization.Localizer.Current.Say("Каждые 30 с")],
        _text["poll.1m", Maxwell.App.Localization.Localizer.Current.Say("Каждую минуту")],
        _text["poll.5m", Maxwell.App.Localization.Localizer.Current.Say("Каждые 5 минут")],
    ];

    // ---------- start and tray ----------

    public bool RunAtLogin
    {
        // Read from Windows rather than from the file. The registry entry is the setting;
        // the stored flag is only what we last asked for, and the two part company the
        // moment somebody switches the app off in Task Manager's Startup tab — leaving a
        // switch that says "on" over a machine that will not start it.
        get => _autostart.IsEnabled;
        set
        {
            Write(s => s.RunAtLogin = value);

            if (value) _autostart.Enable(_store.Current.StartMinimised);
            else _autostart.Disable();

            OnPropertyChanged(nameof(StartMinimised));
            OnPropertyChanged(nameof(CanStartMinimised));
        }
    }

    /// <summary>
    /// Whether the entry Windows runs carries --minimised. Only half of it is the stored
    /// flag: the argument on the Run entry is what actually decides, so it is read back
    /// the same way the switch above is.
    /// </summary>
    public bool StartMinimised
    {
        get => _autostart.IsEnabled ? _autostart.StartsMinimised : _store.Current.StartMinimised;
        set
        {
            Write(s => s.StartMinimised = value);

            // Rewritten rather than left for the next time autostart is toggled: a
            // setting that takes effect only after being switched off and on again is a
            // setting people report as broken.
            if (_autostart.IsEnabled) _autostart.Enable(value);
        }
    }

    /// <summary>
    /// There is nothing to start minimised if nothing starts. The row stays visible and
    /// goes flat instead of vanishing, so the pair reads as one setting with a condition
    /// rather than as a row that comes and goes.
    /// </summary>
    public bool CanStartMinimised => _autostart.IsEnabled;

    public bool MinimiseOnClose
    {
        get => _store.Current.MinimiseOnClose;
        set => Write(s => s.MinimiseOnClose = value);
    }

    // ---------- notifications and polling ----------

    public int LowBatteryThreshold
    {
        get => _store.Current.LowBatteryThreshold;
        set => Write(s => s.LowBatteryThreshold = value);
    }

    public string LowBatteryLabel => $"{LowBatteryThreshold}%";

    /// <summary>
    /// The alerts themselves. Both were honoured by the scheduler from the day it was
    /// written and neither had a switch, so the only way to stop being told about a flat
    /// battery was to edit the settings file.
    /// </summary>
    public bool NotifyLowBattery
    {
        get => _store.Current.NotifyLowBattery;
        set
        {
            Write(s => s.NotifyLowBattery = value);
            OnPropertyChanged(nameof(CanSetThreshold));
        }
    }

    public bool NotifyChargeComplete
    {
        get => _store.Current.NotifyChargeComplete;
        set => Write(s => s.NotifyChargeComplete = value);
    }

    /// <summary>A threshold for an alert nobody will see is not a setting.</summary>
    public bool CanSetThreshold => NotifyLowBattery;

    /// <summary>
    /// The weekly ask to Audeze's public manifest. Honoured by the check service since it
    /// was written; this is the switch it never had.
    /// </summary>
    public bool CheckFirmwareUpdates
    {
        get => _store.Current.CheckFirmwareUpdates;
        set => Write(s => s.CheckFirmwareUpdates = value);
    }

    /// <summary>
    /// The intervals offered. Not a free number: the difference between 15 and 17 seconds
    /// is meaningless, and a text box invites somebody to type 1 and wonder later why the
    /// battery is going flat faster than it used to.
    /// </summary>
    /// <summary>
    /// The Russian wording, kept as the fallback the catalogue falls back to. The screen
    /// draws <see cref="PollIntervalLabels"/>, which is this list run through it.
    /// </summary>
    public IReadOnlyList<string> PollIntervals { get; } =
        [Maxwell.App.Localization.Localizer.Current.Say("Каждые 10 с"), Maxwell.App.Localization.Localizer.Current.Say("Каждые 30 с"), Maxwell.App.Localization.Localizer.Current.Say("Каждую минуту"), Maxwell.App.Localization.Localizer.Current.Say("Каждые 5 минут")];

    private static readonly int[] PollSeconds = [10, 30, 60, 300];

    /// <summary>
    /// The cadence as the dashboard's header says it — "Опрос каждые 30 с". Built from
    /// the same list the setting itself offers, so the two can never drift apart and say
    /// different things about the same number.
    /// </summary>
    public string PollCadence =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _text["poll.cadence", Maxwell.App.Localization.Localizer.Current.Say("Опрос {0}")],
            PollIntervalLabels[Math.Clamp(PollIntervalIndex, 0, PollIntervalLabels.Count - 1)]
                .ToLowerInvariant());

    public int PollIntervalIndex
    {
        get
        {
            int seconds = _store.Current.PollingSeconds;
            int at = Array.IndexOf(PollSeconds, seconds);
            return at >= 0 ? at : 1;
        }

        set => Write(s => s.PollingSeconds = PollSeconds[Math.Clamp(value, 0, PollSeconds.Length - 1)]);
    }

    // ---------- hidden devices, artboard 13b ----------

    public ObservableCollection<HiddenDeviceViewModel> Hidden { get; } = [];

    public bool HasHidden => Hidden.Count > 0;

    public string HiddenHeading
    {
        get
        {
            string word = _text["settings.hidden", Maxwell.App.Localization.Localizer.Current.Say("СКРЫТЫЕ")];
            return Hidden.Count > 0 ? $"{word} · {Hidden.Count}" : word;
        }
    }

    public bool HideSilentDevices
    {
        get => _store.Current.HideSilentDevices;
        set => Write(s => s.HideSilentDevices = value);
    }

    [RelayCommand]
    private async Task ShowAllHiddenAsync() => await _hidden.ShowAllAsync().ConfigureAwait(true);

    private void OnHiddenChanged(object? sender, EventArgs e) => RefreshHidden();

    /// <summary>
    /// Every string on this screen comes from the catalogue, so a language change moves
    /// all of them at once. Cheaper and less error-prone than naming twenty properties.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void RefreshHidden()
    {
        Hidden.Clear();

        foreach ((DeviceSnapshot device, DateTimeOffset at) in _hidden.List(_registry))
        {
            Hidden.Add(new HiddenDeviceViewModel(device, at, _hidden));
        }

        OnPropertyChanged(nameof(HasHidden));
        OnPropertyChanged(nameof(HiddenHeading));
        OnPropertyChanged(nameof(HideSilentDevices));
    }

    /// <summary>
    /// Applies a change and persists it. Fire-and-forget on purpose: a settings toggle
    /// that awaited a file write would block the interface on a disk, and there is
    /// nothing useful to do about a failure that the next change will not also try.
    /// </summary>
    private void Write(Action<AppSettings> change)
    {
        if (_applying) return;

        _applying = true;

        try
        {
            change(_store.Current);
            _ = _store.SaveAsync();
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hidden.Changed -= OnHiddenChanged;
        _text.Changed -= OnLanguageChanged;
    }
}

/// <summary>One row in the hidden list: what it was, when it went away, and the way back.</summary>
public sealed partial class HiddenDeviceViewModel : ObservableObject
{
    private readonly HiddenDevices _hidden;

    public HiddenDeviceViewModel(DeviceSnapshot device, DateTimeOffset hiddenAt, HiddenDevices hidden)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(hidden);

        Device = device;
        HiddenAt = hiddenAt;
        _hidden = hidden;
    }

    public DeviceSnapshot Device { get; }

    public DateTimeOffset HiddenAt { get; }

    public string DisplayName => Device.Info.DisplayName;

    public string Kind => Device.Info.Class == DeviceClass.Unknown ? Maxwell.App.Localization.Localizer.Current.Say("Класс не определён") : Device.Info.Class.ToString();

    /// <summary>
    /// "скрыто 18 августа". A date rather than "3 days ago", because the list is read
    /// months later and a relative time turns into arithmetic the reader has to do.
    /// </summary>
    public string When => $"скрыто {HiddenAt:d MMMM}";

    [RelayCommand]
    private async Task RestoreAsync() => await _hidden.ShowAsync(Device.Info.Key).ConfigureAwait(true);
}
