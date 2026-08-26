using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.App.Infrastructure;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.Platform;
using Maxwell.App.ViewModels.Devices;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Core.Polling;
using Maxwell.Race.Windows;

namespace Maxwell.App.ViewModels;

/// <summary>One entry in the application group of the navigation pane.</summary>
/// <summary>
/// One entry in the rail's application group.
///
/// The title is read from the page rather than copied at construction: copied, it froze
/// the name in whichever language was in force when the shell was built, and a language
/// change renamed every page except the five in this list.
/// </summary>
public sealed record AppSection(object Page)
{
    public string Title => Page switch
    {
        Pages.HotkeysViewModel h => h.Title,
        Pages.SettingsViewModel s => s.Title,
        Pages.AboutViewModel a => a.Title,
        _ => string.Empty,
    };
}


/// <summary>
/// The window: a list of devices, a page for whichever is selected, and the sections
/// that belong to the application rather than to any device.
///
/// It owns one <see cref="DevicePageViewModel"/> per device, created on first visit and
/// kept, so returning to a device does not rebuild its controls or lose its scroll.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceRegistry _registry;
    private readonly IPollingCoordinator _polling;
    private readonly IConfirmationPrompt _confirm;
    private readonly HiddenDevices _hidden;
    private readonly PrimaryDevice _primary;
    private readonly CurvePresetLibrary _presets;
    private readonly TrayExtras _extras;
    private readonly INotifier _notifier;
    private readonly IPairingRemoval _pairing;
    private readonly Dictionary<string, DevicePageViewModel> _pages = new(StringComparer.Ordinal);

    private bool _disposed;

    public ShellViewModel(
        IDeviceRegistry registry,
        IPollingCoordinator polling,
        IConfirmationPrompt confirm,
        HiddenDevices hidden,
        PrimaryDevice primary,
        SettingsViewModel settings,
        HotkeysViewModel hotkeys,
        AboutViewModel about,
        FirstSweep sweep,
        CurvePresetLibrary presets,
        TrayExtras extras,
        INotifier notifier,
        IPairingRemoval pairing)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(polling);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(pairing);

        _registry = registry;
        _polling = polling;
        _confirm = confirm;
        _hidden = hidden;
        _primary = primary;
        _presets = presets;
        _notifier = notifier;
        _pairing = pairing;
        _extras = extras;

        // Shortcuts are shelved, not deleted: the page, its view model and the coordinator
        // all still build and their tests still run — the entry is simply not offered. Put
        // the line back and the section returns with its bindings intact.
        AppSections =
        [
            // new AppSection(hotkeys),
            new AppSection(settings),
            new AppSection(about),
        ];
        Dashboard = new DashboardViewModel(this, Devices, Loading, settings);
        CurrentPage = Dashboard;

        // Started before the registry is subscribed to, because the wait began when the
        // process did — not when this object got round to asking.
        Loading.Begin();
        sweep.WhenComplete(Loading.Resolve);

        _registry.DevicesChanged += OnDevicesChanged;
        Text.Changed += OnLanguageChanged;
        _hidden.Changed += OnDevicesChanged;
        Sync();
    }

    /// <summary>Present devices, then the ones last seen earlier.</summary>
    public ObservableCollection<DeviceListItemViewModel> Devices { get; } = [];

    public ObservableCollection<AppSection> AppSections { get; }

    [ObservableProperty]
    public partial DeviceListItemViewModel? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial AppSection? SelectedAppSection { get; set; }

    public DashboardViewModel Dashboard { get; }

    /// <summary>
    /// The first-sweep gate. Owned here rather than by the dashboard because the flyout
    /// and the dashboard are waiting for the same answer and must not disagree about
    /// whether it has arrived.
    /// </summary>
    internal LoadingState Loading { get; } = new();

    [ObservableProperty]
    public partial object? CurrentPage { get; private set; }

    /// <summary>
    /// Filters the device list by name. Sections and settings are deliberately not
    /// searched: with a handful of devices and a fixed set of sections, a search that
    /// mixes them returns a list nobody can predict.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public bool HasDevices => Devices.Count > 0;

    // ---------- what shape the navigation pane is in ----------

    /// <summary>
    /// True when a device is open, which turns the pane from a list of devices into that
    /// device's sections. Both shapes are in the artboards and only one is ever on
    /// screen: a rail carrying devices and sections at once is a rail nobody can scan.
    /// </summary>
    public bool HasSelectedDevice => CurrentPage is DevicePageViewModel;

    /// <summary>
    /// Whether the page is showing the device entire rather than one of its sections.
    ///
    /// A rail that can only narrow is a rail with no way back. Picking "Pointer" put the
    /// mouse's other sections out of reach without leaving the device and coming in
    /// again — the list highlights one row and a ListBox does not let go of it. So the
    /// sections have a row above them standing for all of them, and this is what marks
    /// that row as the one you are on.
    /// </summary>
    public bool IsWholeDevice => CurrentPage is DevicePageViewModel { Section: null };

    /// <summary>
    /// The third shape: the application's own pages. The artboards only ever show this
    /// group while one of those pages is open — from the dashboard the way in is the
    /// pinned row at the foot of the pane, not a list of settings competing with the
    /// devices for the same eye.
    /// </summary>
    public bool ShowsAppSections => SelectedAppSection is not null;

    /// <summary>The dashboard, and therefore the search box and the device groups.</summary>
    public bool IsDashboard => CurrentPage is DashboardViewModel;

    /// <summary>
    /// The pinned "Settings" at the foot of the pane. Absent on the application's own
    /// pages, where the same entry is already in the list above it.
    /// </summary>
    public bool ShowsSettingsShortcut => !ShowsAppSections;

    public bool ShowsDeviceList => IsDashboard && Dashboard.HasConnected;

    public bool ShowsDisconnected => IsDashboard && Dashboard.HasDisconnected;

    public string SelectedDeviceHeading => (CurrentPage as DevicePageViewModel)?.Title.ToUpperInvariant() ?? string.Empty;

    public IEnumerable<CapabilitySectionViewModel> DeviceSections =>
        (CurrentPage as DevicePageViewModel)?.Sections ?? [];

    /// <summary>
    /// Which section the pane has highlighted. Setting it moves the page between the
    /// whole device and one section — the same two screens artboards 5a and 8a draw.
    /// </summary>
    public CapabilitySectionViewModel? SelectedSection
    {
        get => (CurrentPage as DevicePageViewModel)?.Section;
        set
        {
            if (CurrentPage is not DevicePageViewModel page) return;

            page.Section = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWholeDevice));
            OnPropertyChanged(nameof(HeaderTitle));
        }
    }

    /// <summary>
    /// The line at the far end of the title row. Page-specific, because what is worth
    /// saying next to a title differs: the dashboard's readings are all memories and
    /// need a timestamp, and a settings page has nothing of the kind to admit.
    /// </summary>
    public string HeaderMeta => CurrentPage switch
    {
        DashboardViewModel dashboard => dashboard.Freshness,
        _ => string.Empty,
    };

    public bool HasHeaderMeta => HeaderMeta.Length > 0;

    /// <summary>
    /// A device page names the device in its own hero, at four times this size and
    /// beside its photograph. The artboards give that page no title row at all rather
    /// than printing the same name twice.
    /// </summary>
    public bool HasHeaderTitle => CurrentPage is not DevicePageViewModel;

    // ---------- the rail's own words ----------

    private static Localization.Localizer Text => Localization.Localizer.Current;

    public string AllDevices => Text["rail.all_devices", "All devices"];

    /// <summary>The row above the sections, which is all of them at once.</summary>
    public string WholeDevice => Text["rail.whole_device", "All sections"];

    public string ConnectedHeader => Text["dash.connected", "CONNECTED"];

    public string DisconnectedHeader => Text["dash.disconnected", "DISCONNECTED"];

    public string AppSectionHeader => Text["rail.application", "APPLICATION"];

    public string SearchPlaceholder => Text["rail.search", "Search"];

    public string SettingsLabel => Text["rail.settings", "Settings"];

    public string HeaderTitle => CurrentPage switch
    {
        DevicePageViewModel device => device.Heading,
        DashboardViewModel => AllDevices,
        _ => SelectedAppSection?.Title ?? string.Empty,
    };

    // ---------- navigation ----------

    /// <summary>
    /// Opens the device at <paramref name="index"/> in the list. Exists for the same
    /// reason as the other development flags: driving the shell with synthetic clicks is
    /// unreliable, and a screenshot of the wrong page proves nothing.
    /// </summary>
    public void SelectDeviceAt(int index)
    {
        if (index >= 0 && index < Devices.Count)
        {
            _pendingIndex = null;
            SelectedDevice = Devices[index];

            // The section, if the flag named one. After the device, because sections do
            // not exist until the page does.
            TryApplyPendingSection();
            return;
        }

        // The first sweep has not landed yet, so there is nothing at that position to
        // open. Held rather than dropped: the request arrives moments after start-up and
        // the list arrives moments after that, and silently doing nothing produced
        // screenshots of the dashboard captioned as a device page.
        _pendingIndex = index;
    }

    private int? _pendingIndex;

    /// <summary>
    /// Opens a device by a piece of its name, held until it turns up.
    ///
    /// The position is the wrong handle for this and three wasted runs proved it: the
    /// list is ordered by name and filled by a sweep that takes a minute, so "the fourth
    /// device" is a different device depending on how much of the sweep has landed —
    /// which is exactly when a development flag is used. A fragment of the name is stable.
    /// </summary>
    public void SelectDeviceNamed(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        DeviceListItemViewModel? found = Devices.FirstOrDefault(
            d => d.DisplayName.Contains(fragment, StringComparison.CurrentCultureIgnoreCase));

        if (found is null)
        {
            _pendingName = fragment;
            return;
        }

        _pendingName = null;
        SelectedDevice = found;
        TryApplyPendingSection();
    }

    private string? _pendingName;

    /// <summary>Which section the development flag asked for, held until it exists.</summary>
    public int? PendingSectionIndex { get; set; }

    /// <summary>
    /// Applies the held section once there is one to apply. Sections are built from
    /// capabilities, and capabilities arrive with the device's first answer rather than
    /// with the page — so this is retried from every sync until the list fills in.
    /// </summary>
    private void TryApplyPendingSection()
    {
        if (PendingSectionIndex is { } wanted
            && CurrentPage is DevicePageViewModel page
            && wanted >= 0 && wanted < page.Sections.Count)
        {
            SelectedSection = page.Sections[wanted];
            PendingSectionIndex = null;
        }
    }

    /// <summary>
    /// Opens one of the application's own pages by position. The pane no longer lists
    /// them until one is open, so reaching them means two pointer clicks; this is the
    /// same development door as <see cref="SelectDeviceAt"/>.
    /// </summary>
    public void SelectAppSectionAt(int index)
    {
        if (index >= 0 && index < AppSections.Count) SelectedAppSection = AppSections[index];
    }

    /// <summary>
    /// Opens a named device, for the tray panel's "open the device page". By key
    /// rather than by position: the two lists are ordered independently, and a device's
    /// place in either of them moves whenever something else connects.
    /// </summary>
    public void SelectDevice(string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        if (Devices.FirstOrDefault(d => d.Key == deviceKey) is { } device) SelectedDevice = device;
    }

    /// <summary>
    /// Looks again, now, for the first-launch screen's button.
    ///
    /// Discovery already runs on a timer, so this changes nothing a few seconds of
    /// waiting would not — and that is the point of having it. Somebody who has just
    /// plugged a device in wants to do something rather than watch, and a button that
    /// merely brings the next sweep forward is a better answer than a screen that asks
    /// them to trust it.
    /// </summary>
    [RelayCommand]
    private async Task RediscoverAsync()
    {
        try
        {
            await _registry.DiscoverAsync().ConfigureAwait(true);
        }
        catch (IOException)
        {
            // A driver failing to enumerate is already logged by the registry, and the
            // list simply stays as it was. Nothing here to tell the user that the empty
            // screen is not already telling them.
        }
    }

    /// <summary>
    /// Opens a device from the pane. A command rather than a bound selection because the
    /// two groups are two lists: with <c>SelectedItem</c> on both, clicking in one clears
    /// the other's highlight and each fights the other for the same property.
    /// </summary>
    [RelayCommand]
    private void SelectDeviceItem(DeviceListItemViewModel? device)
    {
        if (device is not null) SelectedDevice = device;
    }

    /// <summary>The foot of the pane. Named by page rather than by position, so
    /// reordering the group cannot silently point it somewhere else.</summary>
    [RelayCommand]
    private void ShowSettings()
    {
        if (AppSections.FirstOrDefault(s => s.Page is SettingsViewModel) is { } section)
        {
            SelectedAppSection = section;
        }
    }

    /// <summary>
    /// Back from one section to the device entire. The way in is a list that highlights
    /// what it opened, and a list cannot un-highlight itself — so this is the row that
    /// does it.
    /// </summary>
    [RelayCommand]
    private void ShowWholeDevice() => SelectedSection = null;

    [RelayCommand]
    private void ShowDashboard()
    {
        SelectedDevice = null;
        SelectedAppSection = null;
        SetPage(Dashboard);
    }

    /// <summary>
    /// The window became visible. This is the moment a full read is worth its cost —
    /// the scheduler deliberately keeps only the battery warm the rest of the time.
    /// </summary>
    public void OnWindowOpened()
    {
        if (CurrentPage is DevicePageViewModel device) device.Activate();
        else _polling.SetAttention(PollingAttention.Glance);
    }

    /// <summary>
    /// The window is gone but the page is still selected, so nothing else will say that
    /// it stopped being looked at. Without this the scheduler goes on reading a device
    /// page nobody can see — and a page with a live row goes on measuring, which for a
    /// microphone means the machine keeps listening behind a hidden window.
    /// </summary>
    public void OnWindowHidden() => _polling.SetAttention(PollingAttention.Idle);

    partial void OnSelectedDeviceChanged(DeviceListItemViewModel? value)
    {
        if (value is null) return;

        SelectedAppSection = null;
        SetPage(PageFor(value.Key));
    }

    partial void OnSelectedAppSectionChanged(AppSection? value)
    {
        if (value is null) return;

        SelectedDevice = null;
        SetPage(value.Page);
    }

    partial void OnSearchTextChanged(string value) => Sync();

    private void SetPage(object? page)
    {
        // Leaving a device page tells the scheduler to stop reading it hard.
        if (CurrentPage is DevicePageViewModel previous && !ReferenceEquals(previous, page))
        {
            previous.Deactivate();
        }

        CurrentPage = page;

        if (page is DevicePageViewModel current) current.Activate();
        else _polling.SetAttention(PollingAttention.Idle);

        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HasHeaderTitle));
        OnPropertyChanged(nameof(HeaderMeta));
        OnPropertyChanged(nameof(HasHeaderMeta));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(ShowsAppSections));
        OnPropertyChanged(nameof(ShowsSettingsShortcut));
        OnPropertyChanged(nameof(ShowsDeviceList));
        OnPropertyChanged(nameof(ShowsDisconnected));
        OnPropertyChanged(nameof(SelectedDeviceHeading));
        OnPropertyChanged(nameof(DeviceSections));
        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(IsWholeDevice));
    }

    private DevicePageViewModel? PageFor(string deviceKey)
    {
        if (_pages.TryGetValue(deviceKey, out DevicePageViewModel? page)) return page;

        if (_registry.Session(deviceKey) is not { } session) return null;

        page = new DevicePageViewModel(session, _polling, _confirm, _presets, _extras);
        _pages[deviceKey] = page;
        return page;
    }

    // ---------- registry ----------

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        // A device turning up is also an answer, and it may arrive before the sweep
        // this shell is waiting on has returned. Resolve is idempotent.
        Loading.Resolve();

        // A device somebody removed while it was away has just been plugged in again, so
        // the note saying it was gone is stale. Housekeeping only — the list already
        // shows it, because a present device outranks anything remembered about it — but
        // a note nothing ever clears is a file that grows for ever.
        _ = _hidden.PruneAsync(_registry);

        Sync();
    });

    /// <summary>
    /// Reconciles the list in place rather than replacing it, so the selected row stays
    /// selected when a poll comes in and the list does not flicker every few seconds.
    /// </summary>
    private void Sync()
    {
        List<DeviceSnapshot> snapshots = [.. _registry.Devices.Where(Matches)];

        foreach (DeviceSnapshot snapshot in snapshots)
        {
            DeviceListItemViewModel? existing = Devices.FirstOrDefault(d => d.Key == snapshot.Info.Key);

            if (existing is null) Devices.Add(new DeviceListItemViewModel(snapshot));
            else existing.Update(snapshot);
        }

        foreach (DeviceListItemViewModel gone in Devices.Where(d => snapshots.All(s => s.Info.Key != d.Key)).ToList())
        {
            Devices.Remove(gone);
        }

        Reorder(snapshots);

        if (_pendingIndex is { } wanted) SelectDeviceAt(wanted);
        if (_pendingName is { } named) SelectDeviceNamed(named);
        TryApplyPendingSection();

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderMeta));
        OnPropertyChanged(nameof(HasHeaderMeta));

        // The pane's shape depends on the device list as well as on the page: at
        // construction there are no devices yet, so a notification only when the page
        // changes leaves the rail empty until something else happens to move it.
        OnPropertyChanged(nameof(ShowsDeviceList));
        OnPropertyChanged(nameof(ShowsDisconnected));
        OnPropertyChanged(nameof(DeviceSections));
    }

    private void Reorder(List<DeviceSnapshot> order)
    {
        for (int target = 0; target < order.Count; target++)
        {
            int current = IndexOf(order[target].Info.Key);
            if (current >= 0 && current != target) Devices.Move(current, target);
        }
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < Devices.Count; i++)
        {
            if (Devices[i].Key == key) return i;
        }

        return -1;
    }

    /// <summary>
    /// A device earns its row by being visible and by matching what was typed. Hiding is
    /// checked first because it is the stronger statement: search narrows what is on
    /// screen for a moment, hiding says keep this off my screen.
    /// </summary>
    private bool Matches(DeviceSnapshot snapshot) =>
        _hidden.IsVisible(snapshot)
        && (string.IsNullOrWhiteSpace(SearchText)
            || snapshot.Info.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>How many devices are being kept off the screen, for the line offering them back.</summary>
    public int HiddenCount => _hidden.Count;

    public bool HasHidden => HiddenCount > 0;

    /// <summary>
    /// Puts a device away: off the dashboard, out of the tray, out of notifications, and
    /// no longer polled. The pairing in Windows is untouched.
    /// </summary>
    [RelayCommand]
    private async Task HideDeviceAsync(DeviceListItemViewModel? device)
    {
        if (device is null) return;

        await _hidden.HideAsync(device.Key, DateTimeOffset.Now).ConfigureAwait(true);
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(HasHidden));
    }

    /// <summary>Opens the device's own page, the first item of the row's menu.</summary>
    [RelayCommand]
    private void OpenDevice(DeviceListItemViewModel? device)
    {
        if (device is not null) SelectedDevice = device;
    }

    /// <summary>
    /// Makes this the device the tray and the global shortcuts act on. Artboard 4c: the
    /// user picks, rather than a rule picking and changing its mind when something else
    /// connects.
    /// </summary>
    [RelayCommand]
    private async Task ShowInTrayAsync(DeviceListItemViewModel? device)
    {
        if (device is null) return;

        await _primary.ChooseAsync(device.Key).ConfigureAwait(true);
        Sync();
    }

    /// <summary>
    /// Unpairs the device in Windows, which is the one thing on this menu that cannot be
    /// undone from here — the device has to be put back into pairing mode by hand. So it
    /// asks first, and it is only offered where it means something: a cabled device has
    /// no pairing to forget.
    /// </summary>
    [RelayCommand]
    private async Task ForgetDeviceAsync(DeviceListItemViewModel? device)
    {
        if (device is null || !device.CanForget) return;

        bool go = await _confirm.ConfirmAsync(new Capability
        {
            Id = "device.forget",
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Action,
            Title = Maxwell.App.Localization.Localizer.Current.Say("Forget {0}?", device.DisplayName),
            Subtitle = Maxwell.App.Localization.Localizer.Current.Say("The Windows pairing will be removed. To use the device again, ")
                + Maxwell.App.Localization.Localizer.Current.Say("it has to be paired afresh, as the first time."),
            Risk = CapabilityRisk.Confirm,
            IsReversible = false,
        }).ConfigureAwait(true);

        if (!go) return;

        // The address the device reported, not one derived from anything: an address
        // assembled from the wrong place would unpair the wrong device, and there is no
        // undo for that.
        string? bluetooth = device.Snapshot.Info.HardwareIds
            .FirstOrDefault(id => id.StartsWith("bt:", StringComparison.Ordinal));

        // A device on a dongle has no pairing to remove, and for it this can only mean
        // taking the row away. A Bluetooth one has both halves — and the first can be
        // refused, which used to be thrown away along with the answer.
        if (await _pairing.ForgetAsync(bluetooth?[3..]).ConfigureAwait(true) == PairingRemoval.Refused)
        {
            // Hiding the row now would answer a request to unpair by pretending to have
            // done it: the device is still paired, still there, and will come back the
            // moment anybody looks past the hiding.
            await _notifier.NotifyAsync(
                Maxwell.App.Localization.Localizer.Current.Say("{0} is still paired", device.DisplayName),
                Maxwell.App.Localization.Localizer.Current.Say(
                    "Windows would not remove the pairing. It can be removed in the system's Bluetooth settings."))
                .ConfigureAwait(true);

            return;
        }

        await _hidden.HideAsync(device.Key, DateTimeOffset.Now).ConfigureAwait(true);

        // And off the page of the device that has just been forgotten.
        //
        // This is what made the whole thing read as having done nothing. The row goes
        // from the list, and the list is behind the page somebody is standing on — so a
        // device was forgotten, its row disappeared where nobody was looking, and the
        // page went on showing it as though the button had not been pressed.
        if (SelectedDevice is { } open && string.Equals(open.Key, device.Key, StringComparison.Ordinal))
        {
            ShowDashboard();
        }
    }

    /// <summary>
    /// Takes a device that is no longer here off the list for good.
    ///
    /// Not the same offer as forgetting, and the wording says so: forgetting unpairs the
    /// device in Windows, which is a change to the system and cannot be undone from here.
    /// This changes nothing outside the application — the row goes, what was remembered
    /// about the device goes with it, and the pairing is left exactly where it was.
    ///
    /// Only while the device is absent. Nothing here can know whether a gamepad that was
    /// unplugged is in a drawer or in a bin, which is precisely why the person holding it
    /// gets to say; and if it is ever plugged in again it comes back on its own, because
    /// ignoring hardware somebody is holding is the one thing a device list must not do.
    /// </summary>
    [RelayCommand]
    private async Task RemoveDeviceAsync(DeviceListItemViewModel? device)
    {
        if (device is null || !device.CanRemove) return;

        bool go = await _confirm.ConfirmAsync(new Capability
        {
            Id = "device.remove",
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Action,
            Title = Maxwell.App.Localization.Localizer.Current.Say("Remove {0} from the list?", device.DisplayName),
            Subtitle = Maxwell.App.Localization.Localizer.Current.Say(
                "Its settings here are dropped. The pairing in the system is left alone, ")
                + Maxwell.App.Localization.Localizer.Current.Say("and connecting the device again brings it back."),
            Risk = CapabilityRisk.Confirm,
            IsReversible = false,
        }).ConfigureAwait(true);

        if (!go) return;

        await _hidden.RemoveAsync(device.Key, DateTimeOffset.Now).ConfigureAwait(true);

        // Off the page of the device that has just gone, for the same reason forgetting
        // does it: the list is behind the page somebody is standing on.
        if (SelectedDevice is { } open && string.Equals(open.Key, device.Key, StringComparison.Ordinal))
        {
            ShowDashboard();
        }
    }

    /// <summary>
    /// Brings everything back. This exists on the dashboard, and not only in the
    /// settings screen it will eventually live in, because hiding without a visible way
    /// back is a trap rather than a feature.
    /// </summary>
    [RelayCommand]
    private async Task ShowHiddenAsync()
    {
        await _hidden.ShowAllAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(HasHidden));
    }

    /// <summary>
    /// The rail renames itself.
    ///
    /// The empty notification covers the shell's own words; the section list needs more
    /// than that, because a record raises nothing of its own and the list box is bound to
    /// each entry's Title. Replacing the entries is the cheap way to say "all of these
    /// changed" — there are five of them and it happens when somebody picks a language.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        AppSection[] again = [.. AppSections];
        AppSections.Clear();
        foreach (AppSection section in again) AppSections.Add(section);

        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DevicesChanged -= OnDevicesChanged;
        Text.Changed -= OnLanguageChanged;
        _hidden.Changed -= OnDevicesChanged;
        Loading.Dispose();

        foreach (DevicePageViewModel page in _pages.Values) page.Dispose();
        _pages.Clear();
    }
}
