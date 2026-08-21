using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Maxwell.App.Platform;
using Maxwell.App.Tray;
using Maxwell.App.ViewModels;
using Maxwell.App.Views;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maxwell.App;

public partial class App : Application, IDisposable
{
    /// <summary>Development affordance: show the tray panel as soon as the app is up.</summary>
    internal static bool PresentFlyoutOnStart { get; set; }

    /// <summary>Development affordance: show the connect announcement without a connect.</summary>
    internal static bool PresentPopupOnStart { get; set; }

    /// <summary>Development affordance: open a device page without a synthetic click.</summary>
    internal static int? OpenDeviceOnStart { get; set; }

    /// <summary>
    /// Development affordance: open the design-system matrix, maximised.
    ///
    /// Maximised is part of the switch rather than a second flag because the matrix is
    /// twelve primitives wide by eight states across and does not fit a default window —
    /// opening it at 1040 pixels shows three columns, which is not a thing anyone can
    /// check the design against.
    /// </summary>
    internal static bool OpenShowcaseOnStart { get; set; }

    private readonly IServiceProvider? _services;

    private TrayIcon? _tray;
    private MainWindow? _window;
    private FlyoutWindow? _flyout;
    private ConnectPopupWindow? _popup;
    private IDeviceRegistry? _registry;
    private PrimaryDevice? _primary;
    private ISettingsStore? _settings;
    private HotkeyCoordinator? _hotkeys;
    private DeviceAvailability? _lastAvailability;
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

            _registry = _services.GetRequiredService<IDeviceRegistry>();
            _primary = _services.GetRequiredService<PrimaryDevice>();
            _registry.DevicesChanged += OnDevicesChanged;

            // Seeded, not announced. Discovery runs before the tray subscribes, and a
            // device already attached at launch has not just connected — popping the
            // panel every time the app starts is not what "on connect" means.
            _lastAvailability = _services.GetRequiredService<PrimaryDevice>().Snapshot?.Availability;

            _hotkeys = _services.GetRequiredService<HotkeyCoordinator>();
            _hotkeys.WindowRequested += (_, _) => Dispatcher.UIThread.Post(ShowWindow);
            _hotkeys.Apply();

            _flyout = _services.GetRequiredService<FlyoutWindow>();
            _popup = _services.GetRequiredService<ConnectPopupWindow>();
            _services.GetRequiredService<FlyoutViewModel>().SettingsRequested +=
                (_, _) => Dispatcher.UIThread.Post(ShowWindow);

            BuildTray();

            _window = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = _window;

            // Launched by the Run key with --minimised, or configured to start hidden:
            // showing the window then would defeat the point of starting with Windows.
            bool startHidden = desktop.Args?.Contains("--minimised", StringComparer.OrdinalIgnoreCase) == true;
            if (!startHidden) _window.Show();

            if (OpenDeviceOnStart is { } index)
            {
                ShellViewModel shell = _services.GetRequiredService<ShellViewModel>();
                Dispatcher.UIThread.Post(() => shell.SelectDeviceAt(index), DispatcherPriority.Background);
            }

            if (OpenShowcaseOnStart)
            {
                ShellViewModel shell = _services.GetRequiredService<ShellViewModel>();
                _window.WindowState = WindowState.Maximized;
                Dispatcher.UIThread.Post(shell.ShowShowcase, DispatcherPriority.Background);
            }

            if (PresentFlyoutOnStart)
            {
                Dispatcher.UIThread.Post(ShowFlyout, DispatcherPriority.Background);
            }

            if (PresentPopupOnStart)
            {
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (_primary?.Snapshot is { } device) _popup?.Present(device);
                    },
                    DispatcherPriority.Background);
            }

            desktop.Exit += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTray()
    {
        // A tray icon that fails to appear must not take the application with it: the
        // device layer keeps working and the window is still reachable.
        try
        {
            var open = new NativeMenuItem("Открыть Maxwell");
            open.Click += (_, _) => ShowWindow();

            var quit = new NativeMenuItem("Выход");
            quit.Click += (_, _) => Shutdown();

            _tray = new TrayIcon
            {
                Icon = TrayIconRenderer.Render(-1, charging: false),
                ToolTipText = "Поиск устройств…",
                IsVisible = true,
                Menu = [open, new NativeMenuItemSeparator(), quit],
            };

            // Clicking the tray opens the panel, not the window. The window is a place
            // you go to; the panel is a place you glance at.
            _tray.Clicked += (_, _) => ShowFlyout();

            TrayIcon.SetIcons(this, [_tray]);

            ILogger? log = _services?.GetService<ILoggerFactory>()?.CreateLogger("Tray");
            log?.TrayCreated(_tray.IsVisible, _tray.Icon is not null, TrayIcon.GetIcons(this)?.Count ?? -1);
        }
#pragma warning disable CA1031 // A missing tray icon must not stop the app from running.
        catch (Exception ex)
        {
            _services?.GetService<ILoggerFactory>()?.CreateLogger("Tray").TrayFailed(ex);
        }
#pragma warning restore CA1031
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

    private void OnDevicesChanged(object? sender, EventArgs e) =>
        // The registry raises this from the polling thread.
        Dispatcher.UIThread.Post(() =>
        {
            RefreshTray();

            DeviceAvailability? now = _primary?.Snapshot?.Availability;

            // Announce a connection once, on the transition — not on every poll.
            bool becameUsable = _lastAvailability != now && now == DeviceAvailability.Active;
            _lastAvailability = now;

            if (becameUsable
                && _settings?.Current.ShowConnectPopup == true
                && _primary?.Snapshot is { } connected)
            {
                _popup?.Present(connected);
            }
        });

    private void RefreshTray()
    {
        if (_tray is null) return;

        DeviceSnapshot? device = _primary?.Snapshot;
        bool withPercentage = _settings?.Current.ShowPercentageInTray ?? true;

        _tray.Icon = TrayIconRenderer.Render(
            withPercentage ? device?.BatteryPercent ?? -1 : -1,
            device?.IsCharging ?? false);

        _tray.ToolTipText = Describe(device);
    }

    /// <summary>
    /// One icon can carry one device. The tooltip names it, because with several
    /// attached a bare percentage answers the wrong question.
    /// </summary>
    private static string Describe(DeviceSnapshot? device) => device switch
    {
        null => "Устройств не найдено",
        { Availability: DeviceAvailability.Unreachable } =>
            $"{device.Info.DisplayName} — не отвечает",
        { BatteryPercent: { } percent } =>
            $"{device.Info.DisplayName} — {percent}%{(device.IsCharging ? ", заряжается" : string.Empty)}",
        _ => $"{device.Info.DisplayName} — подключено",
    };

    private void ShowFlyout()
    {
        _flyout?.Present();
    }

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

        if (_registry is not null) _registry.DevicesChanged -= OnDevicesChanged;
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;

        // The tray icon is an OS resource; leaving it behind puts a dead icon in the
        // notification area until the user hovers over it.
        _tray?.Dispose();
        _tray = null;

        GC.SuppressFinalize(this);
    }
}
