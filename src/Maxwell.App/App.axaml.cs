using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Maxwell.App.Platform;
using Maxwell.App.Tray;
using Maxwell.App.Views;
using Maxwell.Core.Model;
using Maxwell.Core.Services;
using Maxwell.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.App;

public partial class App : Application, IDisposable
{
    private readonly IServiceProvider? _services;

    private TrayIcon? _tray;
    private MainWindow? _window;
    private IDeviceService? _device;
    private ISettingsStore? _settings;
    private HotkeyCoordinator? _hotkeys;
    private ConnectionState _lastConnection = ConnectionState.NotFound;
    private bool _disposed;

    /// <summary>Used by the XAML previewer, which cannot supply a container.</summary>
    public App()
    {
    }

    public App(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            // Tray-first: closing the window hides it, and only the tray menu quits.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _settings = _services.GetRequiredService<ISettingsStore>();
            _settings.LoadAsync().GetAwaiter().GetResult();
            _settings.Changed += OnSettingsChanged;
            ApplyTheme(_settings.Current.Theme);

            _device = _services.GetRequiredService<IDeviceService>();
            _device.StateChanged += OnDeviceStateChanged;

            _hotkeys = _services.GetRequiredService<HotkeyCoordinator>();
            _hotkeys.WindowRequested += (_, _) => Dispatcher.UIThread.Post(ShowWindow);
            _hotkeys.Apply();

            BuildTray();

            _window = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = _window;

            // Launched by the Run key with --minimised, or configured to start hidden:
            // showing the window then would defeat the point of starting with Windows.
            bool startHidden = desktop.Args?.Contains("--minimised", StringComparer.OrdinalIgnoreCase) == true;
            if (!startHidden) _window.Show();

            desktop.Exit += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray()
    {
        var open = new NativeMenuItem("Открыть Maxwell");
        open.Click += (_, _) => ShowWindow();

        var quit = new NativeMenuItem("Выход");
        quit.Click += (_, _) => Shutdown();

        _tray = new TrayIcon
        {
            Icon = TrayIconRenderer.Render(DeviceState.Unknown, charging: false),
            ToolTipText = "Maxwell — поиск устройства…",
            IsVisible = true,
            Menu = [open, new NativeMenuItemSeparator(), quit],
        };

        _tray.Clicked += (_, _) => ShowWindow();

        TrayIcon.SetIcons(this, [_tray]);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTheme(settings.Theme);
            RefreshTray();
        });

    private void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private void OnDeviceStateChanged(object? sender, DeviceState state)
    {
        // The device service raises this from its polling thread.
        Dispatcher.UIThread.Post(() =>
        {
            RefreshTray();

            // Announce a connection once, on the transition — not on every poll.
            bool becameUsable = _lastConnection != state.Connection
                && state.Connection == ConnectionState.Active;

            _lastConnection = state.Connection;

            if (becameUsable && _settings?.Current.ShowConnectPopup == true && state.HasBattery)
                ShowWindow();
        });
    }

    private void RefreshTray()
    {
        if (_tray is null || _device is null) return;

        DeviceState state = _device.State;
        bool withPercentage = _settings?.Current.ShowPercentageInTray ?? true;

        _tray.Icon = TrayIconRenderer.Render(
            withPercentage ? state.BatteryPercent : DeviceState.Unknown,
            state.IsCharging);

        _tray.ToolTipText = Describe(state);
    }

    private static string Describe(DeviceState state) => state.Connection switch
    {
        ConnectionState.NotFound => "Maxwell — не подключено",
        ConnectionState.MonitorOnly => "Maxwell — только донгл, для настройки нужен кабель",
        _ when state.HasBattery => $"Maxwell — {state.BatteryPercent}%{(state.IsCharging ? ", заряжается" : "")}",
        _ => "Maxwell — подключено",
    };

    private void ShowWindow()
    {
        if (_window is null) return;

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Shutdown()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_device is not null) _device.StateChanged -= OnDeviceStateChanged;
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;

        // The tray icon is an OS resource; leaving it behind puts a dead icon in the
        // notification area until the user hovers over it.
        _tray?.Dispose();
        _tray = null;

        GC.SuppressFinalize(this);
    }
}
