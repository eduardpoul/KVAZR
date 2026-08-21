using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.App.Infrastructure;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.Platform;
using Maxwell.App.ViewModels.Devices;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Devices;
using Maxwell.Core.Polling;

namespace Maxwell.App.ViewModels;

/// <summary>One entry in the application group of the navigation pane.</summary>
public sealed record AppSection(string Title, object Page);


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
    private readonly Dictionary<string, DevicePageViewModel> _pages = new(StringComparer.Ordinal);

    private bool _disposed;

    public ShellViewModel(
        IDeviceRegistry registry,
        IPollingCoordinator polling,
        IConfirmationPrompt confirm,
        HiddenDevices hidden,
        CapabilityShowcaseViewModel showcase,
        SettingsViewModel settings,
        AboutViewModel about,
        FirstSweep sweep)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(polling);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(showcase);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(sweep);

        _registry = registry;
        _polling = polling;
        _confirm = confirm;
        _hidden = hidden;

        AppSections =
        [
            new AppSection(settings.Title, settings),
            new AppSection(showcase.Title, showcase),
            new AppSection(Identity.Title, Identity),
            new AppSection(about.Title, about),
        ];
        Dashboard = new DashboardViewModel(this, Devices, Loading);
        CurrentPage = Dashboard;

        // Started before the registry is subscribed to, because the wait began when the
        // process did — not when this object got round to asking.
        Loading.Begin();
        sweep.WhenComplete(Loading.Resolve);

        _registry.DevicesChanged += OnDevicesChanged;
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

    /// <summary>The identity sheet: every mark state, motion, loader and skeleton on one page.</summary>
    internal IdentityViewModel Identity { get; } = new();

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

    public string HeaderTitle => CurrentPage switch
    {
        DevicePageViewModel device => device.Title,
        DashboardViewModel => "Все устройства",
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
        if (index >= 0 && index < Devices.Count) SelectedDevice = Devices[index];
    }

    /// <summary>
    /// Opens a named device, for the tray panel's "открыть страницу устройства". By key
    /// rather than by position: the two lists are ordered independently, and a device's
    /// place in either of them moves whenever something else connects.
    /// </summary>
    public void SelectDevice(string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        if (Devices.FirstOrDefault(d => d.Key == deviceKey) is { } device) SelectedDevice = device;
    }

    /// <summary>
    /// Opens the design-system matrix. Same reason as <see cref="SelectDeviceAt"/>: it
    /// is the page every control has to be checked against, and reaching it by driving
    /// the pointer is both unreliable and rude to whoever is using the machine.
    /// </summary>
    public void ShowShowcase()
    {
        if (AppSections.Count > 0) SelectedAppSection = AppSections[0];
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
    }

    private DevicePageViewModel? PageFor(string deviceKey)
    {
        if (_pages.TryGetValue(deviceKey, out DevicePageViewModel? page)) return page;

        if (_registry.Session(deviceKey) is not { } session) return null;

        page = new DevicePageViewModel(session, _polling, _confirm);
        _pages[deviceKey] = page;
        return page;
    }

    // ---------- registry ----------

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        // A device turning up is also an answer, and it may arrive before the sweep
        // this shell is waiting on has returned. Resolve is idempotent.
        Loading.Resolve();
        Sync();
    });

    /// <summary>
    /// Selects the identity sheet. Exists for the same reason as the other development
    /// flags: it is a page of drawings, and the only way to review a drawing is to open
    /// it.
    /// </summary>
    public void SelectIdentity() =>
        SelectedAppSection = AppSections.FirstOrDefault(s => ReferenceEquals(s.Page, Identity));

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

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HeaderTitle));
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DevicesChanged -= OnDevicesChanged;
        _hidden.Changed -= OnDevicesChanged;
        Loading.Dispose();

        foreach (DevicePageViewModel page in _pages.Values) page.Dispose();
        _pages.Clear();
    }
}
