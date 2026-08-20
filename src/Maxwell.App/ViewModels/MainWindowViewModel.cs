using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels;

/// <summary>One entry in the navigation pane.</summary>
public sealed record NavSection(string Key, string Title, string Glyph, bool RequiresControl);

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceService _device;
    private bool _disposed;

    [ObservableProperty]
    public partial DeviceState State { get; set; } = DeviceState.Empty;

    [ObservableProperty]
    public partial NavSection Selected { get; set; }

    /// <summary>Sections belonging to the headset. Disabled when only the dongle is present.</summary>
    public ObservableCollection<NavSection> DeviceSections { get; } =
    [
        new("overview", "Обзор", "", false),
        new("sound", "Звук", "", true),
        new("microphone", "Микрофон", "", true),
        new("system", "Система", "", true),
        new("advanced", "Дополнительно", "", true),
    ];

    /// <summary>Sections belonging to the application itself; always available.</summary>
    public ObservableCollection<NavSection> AppSections { get; } =
    [
        new("profiles", "Профили", "", false),
        new("hotkeys", "Горячие клавиши", "", false),
        new("settings", "Параметры", "", false),
    ];

    public MainWindowViewModel(IDeviceService device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        Selected = DeviceSections[0];
        State = device.State;

        _device.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, DeviceState state) =>
        Dispatcher.UIThread.Post(() => State = state);

    // ---------- projections for the view ----------

    public string DeviceTitle => State.DeviceName ?? "Audeze Maxwell";

    public string DeviceSubtitle => State.ControlTransport switch
    {
        null when State.DonglePresent => "Xbox · только донгл",
        null => "не подключены",
        _ => $"Xbox · прошивка {State.FirmwareVersion ?? "—"}",
    };

    public string BatteryText => State.HasBattery ? $"{State.BatteryPercent}%" : "—";

    public string ConnectionText => State.Connection switch
    {
        ConnectionState.NotFound => "Наушники не найдены",
        ConnectionState.MonitorOnly => "Подключён только донгл — доступен мониторинг",
        _ => State.IsCharging ? "Заряжается" : "Подключено по кабелю",
    };

    public bool CanControl => State.CanControl;

    public bool ShowNotFound => State.Connection == ConnectionState.NotFound;

    public bool ShowMonitorOnly => State.Connection == ConnectionState.MonitorOnly;

    /// <summary>
    /// The projections above are computed from <see cref="State"/>, so every one of
    /// them has to be re-read whenever the snapshot is replaced.
    /// </summary>
    partial void OnStateChanged(DeviceState value)
    {
        OnPropertyChanged(nameof(DeviceTitle));
        OnPropertyChanged(nameof(DeviceSubtitle));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(CanControl));
        OnPropertyChanged(nameof(ShowNotFound));
        OnPropertyChanged(nameof(ShowMonitorOnly));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _device.StateChanged -= OnStateChanged;
    }
}
