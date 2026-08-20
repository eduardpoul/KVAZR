using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels;

/// <summary>One entry in the navigation pane, bound directly to the page it shows.</summary>
public sealed record NavSection(PageViewModel Page)
{
    public string Title => Page.Title;
}

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
    public partial PageViewModel CurrentPage { get; set; }

    /// <summary>Sections belonging to the headset.</summary>
    public ObservableCollection<NavSection> DeviceSections { get; }

    /// <summary>Sections belonging to the application; always reachable.</summary>
    public ObservableCollection<NavSection> AppSections { get; }

    public MainWindowViewModel(
        IDeviceService device,
        OverviewViewModel overview,
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
            new NavSection(overview),
            new NavSection(sound),
            new NavSection(microphone),
            new NavSection(system),
            new NavSection(advanced),
        ];

        AppSections =
        [
            new NavSection(profiles),
            new NavSection(hotkeys),
            new NavSection(settings),
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
    }

    partial void OnSelectedAppSectionChanged(NavSection? value)
    {
        if (value is null) return;
        SelectedDeviceSection = null;
        CurrentPage = value.Page;
    }

    partial void OnStateChanged(DeviceState value)
    {
        OnPropertyChanged(nameof(DeviceTitle));
        OnPropertyChanged(nameof(TrayStatus));
    }

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

        foreach (NavSection section in DeviceSections) section.Page.Dispose();
        foreach (NavSection section in AppSections) section.Page.Dispose();
    }
}
