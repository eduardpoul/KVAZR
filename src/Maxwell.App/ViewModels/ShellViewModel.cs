using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.ViewModels.Devices;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Devices;
using Maxwell.Core.Polling;

namespace Maxwell.App.ViewModels;

/// <summary>One entry in the application group of the navigation pane.</summary>
public sealed record AppSection(string Title, object Page);

/// <summary>
/// The "Все устройства" page. Shares the shell's list rather than copying it, so the
/// dashboard and the navigation pane can never disagree about what is attached.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    public DashboardViewModel(ObservableCollection<DeviceListItemViewModel> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        Devices = devices;
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<DeviceListItemViewModel> Devices { get; }

    /// <summary>Drives the first-launch state, which is an introduction and not an error.</summary>
    public bool IsEmpty => Devices.Count == 0;
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
    private readonly Dictionary<string, DevicePageViewModel> _pages = new(StringComparer.Ordinal);

    private bool _disposed;

    public ShellViewModel(
        IDeviceRegistry registry,
        IPollingCoordinator polling,
        IConfirmationPrompt confirm,
        CapabilityShowcaseViewModel showcase)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(polling);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(showcase);

        _registry = registry;
        _polling = polling;
        _confirm = confirm;

        AppSections = [new AppSection(showcase.Title, showcase)];
        Dashboard = new DashboardViewModel(Devices);
        CurrentPage = Dashboard;

        _registry.DevicesChanged += OnDevicesChanged;
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
    /// Opens the design-system matrix. Same reason as <see cref="SelectDeviceAt"/>: it
    /// is the page every control has to be checked against, and reaching it by driving
    /// the pointer is both unreliable and rude to whoever is using the machine.
    /// </summary>
    public void ShowShowcase()
    {
        if (AppSections.Count > 0) SelectedAppSection = AppSections[0];
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

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Sync);

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

    private bool Matches(DeviceSnapshot snapshot) =>
        string.IsNullOrWhiteSpace(SearchText)
        || snapshot.Info.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DevicesChanged -= OnDevicesChanged;

        foreach (DevicePageViewModel page in _pages.Values) page.Dispose();
        _pages.Clear();
    }
}
