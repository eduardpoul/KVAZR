using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Maxwell.App.Tray;
using Maxwell.App.Views;
using Maxwell.Core.Model;
using Maxwell.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.App;

public partial class App : Application, IDisposable
{
    private readonly IServiceProvider? _services;

    private TrayIcon? _tray;
    private MainWindow? _window;
    private IDeviceService? _device;
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

            _device = _services.GetRequiredService<IDeviceService>();
            _device.StateChanged += OnDeviceStateChanged;

            BuildTray();

            _window = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = _window;
            _window.Show();

            desktop.Exit += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_device is not null) _device.StateChanged -= OnDeviceStateChanged;

        // The tray icon is an OS resource; leaving it behind puts a dead icon in the
        // notification area until the user hovers over it.
        _tray?.Dispose();
        _tray = null;

        GC.SuppressFinalize(this);
    }

    private void BuildTray()
    {
        var open = new NativeMenuItem("Open Maxwell");
        open.Click += (_, _) => ShowWindow();

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Shutdown();

        _tray = new TrayIcon
        {
            Icon = TrayIconRenderer.Render(DeviceState.Unknown, charging: false),
            ToolTipText = "Maxwell — searching…",
            IsVisible = true,
            Menu = [open, new NativeMenuItemSeparator(), quit],
        };

        _tray.Clicked += (_, _) => ShowWindow();

        TrayIcon.SetIcons(this, [_tray]);
    }

    private void OnDeviceStateChanged(object? sender, DeviceState state)
    {
        // The device service raises this from its polling thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (_tray is null) return;

            _tray.Icon = TrayIconRenderer.Render(state.BatteryPercent, state.IsCharging);
            _tray.ToolTipText = Describe(state);
        });
    }

    private static string Describe(DeviceState state) => state.Connection switch
    {
        ConnectionState.NotFound => "Maxwell — not connected",
        ConnectionState.MonitorOnly => "Maxwell — dongle only, connect a cable to configure",
        _ when state.HasBattery => $"Maxwell — {state.BatteryPercent}%{(state.IsCharging ? ", charging" : "")}",
        _ => "Maxwell — connected",
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
}
