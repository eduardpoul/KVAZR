using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels;

/// <summary>
/// One entry in the navigation pane. The page is untyped because not every page is
/// bound to the headset — the design-system showcase has no device at all.
/// </summary>
public sealed record NavSection(string Title, object Page);

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceService _device;
    private bool _disposed;

    [ObservableProperty]
    public partial DeviceState State { get; set; } = DeviceState.Empty;

    [ObservableProperty]
    public partial NavSection? SelectedDeviceSection { get; set; }

    [ObservableProperty]
    public partial NavSection? SelectedAppSection { get; set; }

    [ObservableProperty]
    public partial object CurrentPage { get; set; }

    /// <summary>Sections belonging to the headset.</summary>
    public ObservableCollection<NavSection> DeviceSections { get; }

    /// <summary>Sections belonging to the application; always reachable.</summary>
    public ObservableCollection<NavSection> AppSections { get; }

    public MainWindowViewModel(
        IDeviceService device,
        OverviewViewModel overview,
        CapabilityShowcaseViewModel showcase,
        SoundViewModel sound,
        MicrophoneViewModel microphone,
        SystemViewModel system,
        AdvancedViewModel advanced,
        ProfilesViewModel profiles,
        HotkeysViewModel hotkeys,
        SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(overview);

        _device = device;

        DeviceSections =
        [
            new NavSection(overview.Title, overview),
            new NavSection(sound.Title, sound),
            new NavSection(microphone.Title, microphone),
            new NavSection(system.Title, system),
            new NavSection(advanced.Title, advanced),
        ];

        AppSections =
        [
            new NavSection(profiles.Title, profiles),
            new NavSection(hotkeys.Title, hotkeys),
            new NavSection(settings.Title, settings),
            new NavSection(showcase.Title, showcase),
        ];

        CurrentPage = overview;
        SelectedDeviceSection = DeviceSections[0];
        State = device.State;

        _device.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, DeviceState state) =>
        Dispatcher.UIThread.Post(() => State = state);

    // The two lists are separate controls, so selecting in one has to clear the other
    // or both would show a highlighted row at once.
    partial void OnSelectedDeviceSectionChanged(NavSection? value)
    {
        if (value is null) return;
        SelectedAppSection = null;
        CurrentPage = value.Page;
        OnPropertyChanged(nameof(SelectedTitle));
    }

    partial void OnSelectedAppSectionChanged(NavSection? value)
    {
        if (value is null) return;
        SelectedDeviceSection = null;
        CurrentPage = value.Page;
        OnPropertyChanged(nameof(SelectedTitle));
    }

    partial void OnStateChanged(DeviceState value)
    {
        OnPropertyChanged(nameof(DeviceTitle));
        OnPropertyChanged(nameof(TrayStatus));
    }

    /// <summary>Header over the page area, taken from the selected nav entry.</summary>
    public string SelectedTitle => (SelectedDeviceSection ?? SelectedAppSection)?.Title ?? string.Empty;

    public string DeviceTitle => State.DeviceName ?? "Audeze Maxwell";

    public string TrayStatus => State.Connection switch
    {
        ConnectionState.NotFound => "Не подключено",
        ConnectionState.MonitorOnly => "Только донгл",
        _ => State.HasBattery ? $"{State.BatteryPercent}%" : "Подключено",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _device.StateChanged -= OnStateChanged;

        foreach (NavSection section in DeviceSections.Concat(AppSections))
        {
            if (section.Page is IDisposable disposable) disposable.Dispose();
        }
    }
}
