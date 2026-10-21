using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Kvazr.App.Localization;
using Kvazr.App.Platform;
using Kvazr.Core.Abstractions;
using Kvazr.Core.Devices;
using Kvazr.Core.Settings;

namespace Kvazr.App.ViewModels.Pages;

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
    private readonly DriverSwitchboard _switches;
    private readonly IReadOnlyList<IDeviceDriver> _drivers;
    private readonly IReadOnlyList<ICapabilityContributor> _contributors;

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
        Localizer text,
        DriverSwitchboard switches,
        IEnumerable<IDeviceDriver> drivers,
        IEnumerable<ICapabilityContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autostart);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(switches);
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(contributors);

        _store = store;
        _autostart = autostart;
        _registry = registry;
        _hidden = hidden;
        _text = text;
        _switches = switches;
        _drivers = [.. drivers];
        _contributors = [.. contributors];

        _hidden.Changed += OnHiddenChanged;
        _text.Changed += OnLanguageChanged;
        _registry.DevicesChanged += OnDevicesChanged;
        RefreshHidden();
        RefreshLibraries();
    }

    public string Title => _text["page.settings", Kvazr.App.Localization.Localizer.Current.Say("Settings")];

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
    /// thing that leaves somebody who does not read English unable to find their way
    /// back to it.
    /// </summary>
    public IReadOnlyList<string> Languages { get; } = ["English", "Русский"];

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
        _text["poll.10s", Kvazr.App.Localization.Localizer.Current.Say("Every 10 s")],
        _text["poll.30s", Kvazr.App.Localization.Localizer.Current.Say("Every 30 s")],
        _text["poll.1m", Kvazr.App.Localization.Localizer.Current.Say("Every minute")],
        _text["poll.5m", Kvazr.App.Localization.Localizer.Current.Say("Every 5 minutes")],
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
    /// The source wording, which is what the catalogue is keyed by. The screen
    /// draws <see cref="PollIntervalLabels"/>, which is this list run through it.
    /// </summary>
    public IReadOnlyList<string> PollIntervals { get; } =
        [Kvazr.App.Localization.Localizer.Current.Say("Every 10 s"), Kvazr.App.Localization.Localizer.Current.Say("Every 30 s"), Kvazr.App.Localization.Localizer.Current.Say("Every minute"), Kvazr.App.Localization.Localizer.Current.Say("Every 5 minutes")];

    private static readonly int[] PollSeconds = [10, 30, 60, 300];

    /// <summary>
    /// The cadence as the dashboard's header says it — "Polling every 30 s". Built from
    /// the same list the setting itself offers, so the two can never drift apart and say
    /// different things about the same number.
    /// </summary>
    public string PollCadence =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            _text["poll.cadence", Kvazr.App.Localization.Localizer.Current.Say("Polling {0}")],
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
            string word = _text["settings.hidden", Kvazr.App.Localization.Localizer.Current.Say("HIDDEN")];
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
    // ---------- developer ----------

    /// <summary>
    /// Whether the developer section is shown at all.
    ///
    /// A switch rather than a build flag or an environment variable, because the people
    /// it is for are the ones running an ordinary build on their own desk. Off by
    /// default and remembered once turned on: what it reveals is a way to switch off the
    /// driver that understands your headset, which is the right tool for building the
    /// tier underneath and a very effective way for anybody else to break their own
    /// installation.
    /// </summary>
    public bool DeveloperMode
    {
        get => _store.Current.DeveloperMode;
        set
        {
            Write(s => s.DeveloperMode = value);
            OnPropertyChanged(nameof(Libraries));
        }
    }

    /// <summary>
    /// Every driver and contributor in this build, with a switch each.
    ///
    /// The list is what the container holds rather than a written-down one, so a family
    /// added under Drivers appears here with no second place to remember. Ordered deepest
    /// first, because that is the order they shadow each other in: switching off the top
    /// entry is what hands its devices to the one below.
    /// </summary>
    public ObservableCollection<LibrarySwitchViewModel> Libraries { get; } = [];

    /// <summary>
    /// Rebuilt rather than updated in place. The rows carry how many devices each library
    /// currently owns, which is the number that answers "did that do anything", and it
    /// changes underneath on every sweep.
    /// </summary>
    private void RefreshLibraries()
    {
        Dictionary<string, int> owned = _registry.Devices
            .GroupBy(d => d.Info.DriverId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // Only when something it shows has actually moved.
        //
        // DevicesChanged fires on every reading that differs, which is most sweeps of
        // every device, and rebuilding the list each time replaced every row in a
        // collection the screen is bound to. What that looks like is a settings page
        // that will not hold its scroll position while a battery percentage ticks.
        if (!Moved(owned)) return;

        _owned = owned;
        Libraries.Clear();

        foreach (IDeviceDriver driver in _drivers.OrderByDescending(d => d.Tier).ThenBy(d => d.DriverId, StringComparer.Ordinal))
        {
            Libraries.Add(new LibrarySwitchViewModel(
                driver.DriverId, driver.Tier, _switches.IsOn(driver.DriverId),
                owned.GetValueOrDefault(driver.DriverId), SetLibrary));
        }

        foreach (ICapabilityContributor contributor in _contributors.OrderBy(c => c.ContributorId, StringComparer.Ordinal))
        {
            Libraries.Add(new LibrarySwitchViewModel(
                contributor.ContributorId, null, _switches.IsOn(contributor.ContributorId), 0, SetLibrary));
        }

        OnPropertyChanged(nameof(Libraries));
    }

    /// <summary>
    /// Switches one library and makes the change real at once.
    ///
    /// Three things, in this order, and the order is the point: the switchboard is what
    /// the registry reads, the file is what the next launch reads, and the sweep is what
    /// turns both into devices on the screen. Without the sweep the switch would be
    /// honest and the list would go on showing the arrangement it replaced until
    /// something else happened to trigger discovery.
    /// </summary>
    private void SetLibrary(string id, bool on)
    {
        _switches.Set(id, on);

        Write(s =>
        {
            if (on) s.DisabledLibraries.Remove(id);
            else s.DisabledLibraries.Add(id);
        });

        // Fire and forget, but not silently: an unobserved failure in here is a switch
        // that moved on screen and changed nothing on the machine, and the screen would
        // go on claiming otherwise until something else swept.
        _ = Task.Run(async () =>
        {
            try
            {
                await _registry.DiscoverAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A device that would not open on the way past. The next sweep tries
                // again from scratch, which is what every other discovery failure does.
            }
        });
    }

    /// <summary>What each library owned when the list was last built.</summary>
    private Dictionary<string, int> _owned = new(StringComparer.Ordinal);

    private bool Moved(Dictionary<string, int> owned) =>
        Libraries.Count == 0
        || owned.Count != _owned.Count
        || owned.Any(pair => _owned.GetValueOrDefault(pair.Key) != pair.Value)
        || Libraries.Any(l => l.IsOn != _switches.IsOn(l.Id));

    private void OnDevicesChanged(object? sender, EventArgs e) => OnUi(RefreshLibraries);

    /// <summary>
    /// Device events arrive on whatever thread the sweep ran on, and this touches a
    /// collection the screen is bound to.
    /// </summary>
    private static void OnUi(Action work)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) work();
        else Avalonia.Threading.Dispatcher.UIThread.Post(work);
    }

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
        _registry.DevicesChanged -= OnDevicesChanged;
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

    public string DisplayName => Kvazr.App.Localization.Localizer.Current.Say(Device.Info.DisplayName);

    public string Kind => Device.Info.Class == DeviceClass.Unknown ? Kvazr.App.Localization.Localizer.Current.Say("Class not established") : Device.Info.Class.ToString();

    /// <summary>
    /// "hidden 18 August". A date rather than "3 days ago", because the list is read
    /// months later and a relative time turns into arithmetic the reader has to do.
    /// </summary>
    public string When => Kvazr.App.Localization.Localizer.Current.Say("hidden {0:d MMMM}", HiddenAt);

    [RelayCommand]
    private async Task RestoreAsync() => await _hidden.ShowAsync(Device.Info.Key).ConfigureAwait(true);
}
