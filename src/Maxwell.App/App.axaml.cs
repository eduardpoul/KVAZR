using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Maxwell.App.Branding;
using Maxwell.App.Infrastructure;
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

    /// <summary>Development affordance: open a device page without a synthetic click.</summary>
    internal static int? OpenDeviceOnStart { get; set; }

    /// <summary>
    /// Development affordance: hold the splash up instead of letting the shell dismiss
    /// it. A screen that is on for six hundred milliseconds cannot be photographed.
    /// </summary>
    internal static SplashForm? PinSplash { get; set; }

    /// <summary>Forces a theme for the session, for putting a render beside a light artboard.</summary>
    internal static AppTheme? ThemeOnStart { get; set; }

    internal static int? OpenAppSectionOnStart { get; set; }

    internal static int? OpenSectionOnStart { get; set; }

    /// <summary>Development affordance: a window size as "1040x2000", so a long page fits one picture.</summary>
    internal static string? WindowSizeOnStart { get; set; }

    /// <summary>Development affordance: scroll the content pane down before the screenshot.</summary>
    internal static double? ScrollOnStart { get; set; }

    private readonly IServiceProvider? _services;

    private TrayIcon? _tray;
    private MainWindow? _window;
    private FlyoutWindow? _flyout;
    private IDeviceRegistry? _registry;
    private PrimaryDevice? _primary;
    private HiddenDevices? _hidden;
    private ISettingsStore? _settings;
    // private HotkeyCoordinator? _hotkeys;   // shelved, see the startup block below
    private SplashWindow? _splash;
    private bool _discovered;
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

            // The two settings every window is built out of — the theme and the language —
            // are read before the first window rather than after it.
            //
            // This used to come after the splash, so that the splash covered the read, and
            // that was the wrong trade for a file of two kilobytes. A window built before
            // these are known is built wrong: the splash came up in the default theme and
            // then changed under the user, and the tray panel — whose fixed labels are
            // bound once, when it is constructed — came up in Russian under an English
            // interface, which is exactly the bug this ordering hides best. Reading the
            // file first costs a millisecond and nobody has ever seen it.
            _settings = _services.GetRequiredService<ISettingsStore>();
            _settings.LoadAsync().GetAwaiter().GetResult();
            _settings.Changed += OnSettingsChanged;

            // The development flag wins the first paint so captures are deterministic,
            // and then expires: left standing it outranked the settings toggle for the
            // whole session, which read as "the dark theme does not work".
            ApplyTheme(ThemeOnStart ?? _settings.Current.Theme);
            ThemeOnStart = null;

            // Resolved for its side effect: the constructor makes this instance the
            // ambient catalogue. Anything built before it says its words in the fallback
            // language, which is Russian whatever the user has chosen.
            _services.GetRequiredService<Localization.Localizer>();

            // Still before discovery. The splash exists to cover the interval between
            // here and the shell being on screen, and one put up after that work has
            // already happened covers nothing.
            ShowSplash(desktop);

            _registry = _services.GetRequiredService<IDeviceRegistry>();
            _primary = _services.GetRequiredService<PrimaryDevice>();
            _hidden = _services.GetRequiredService<HiddenDevices>();
            _registry.DevicesChanged += OnDevicesChanged;

            // Seeded, not announced. Discovery runs before the tray subscribes, and a
            // device already attached at launch has not just connected — popping the
            // panel every time the app starts is not what "on connect" means.

            // Shortcuts are shelved along with their page. Left registered they would keep
            // claiming key combinations system-wide with no screen to change or clear them
            // — a shortcut nobody can see is worse than none.
            // _hotkeys = _services.GetRequiredService<HotkeyCoordinator>();
            // _hotkeys.WindowRequested += (_, _) => Dispatcher.UIThread.Post(ShowWindow);
            // _hotkeys.Apply();

            _flyout = _services.GetRequiredService<FlyoutWindow>();
            FlyoutViewModel flyout = _services.GetRequiredService<FlyoutViewModel>();
            flyout.SettingsRequested += (_, _) => Dispatcher.UIThread.Post(ShowWindow);

            // "Открыть страницу устройства" has to do both halves. Raising the window
            // alone lands the user on whatever page was open last, which is the one
            // thing they did not ask for.
            flyout.DeviceRequested += (_, key) => Dispatcher.UIThread.Post(() =>
            {
                ShowWindow();
                _services.GetRequiredService<ShellViewModel>().SelectDevice(key);
            });

            BuildTray();

            // One sweep, one answer, three consumers. Started here rather than in any of
            // them so that whichever is built last still learns the result.
            FirstSweep sweep = _services.GetRequiredService<FirstSweep>();
            sweep.WhenComplete(() =>
            {
                _discovered = true;
                RefreshTray();
            });
            sweep.Begin();

            _window = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = _window;

            // Launched by the Run key with --minimised, or configured to start hidden:
            // showing the window then would defeat the point of starting with Windows.
            ApplyWindowSize(_window);

            bool startHidden = desktop.Args?.Contains("--minimised", StringComparer.OrdinalIgnoreCase) == true;
            if (!startHidden) _window.Show();

            // The shell is up and the first discovery pass has been folded into the
            // list. Anything still arriving is an update, not a first answer, and the
            // place to show it is the window rather than a splash on top of it.
            Dispatcher.UIThread.Post(() => _splash?.Dismiss(), DispatcherPriority.Background);

            if (OpenDeviceOnStart is { } index)
            {
                ShellViewModel shell = _services.GetRequiredService<ShellViewModel>();
                shell.PendingSectionIndex = OpenSectionOnStart;
                Dispatcher.UIThread.Post(() => shell.SelectDeviceAt(index), DispatcherPriority.Background);
            }

            if (OpenAppSectionOnStart is { } section)
            {
                ShellViewModel shell = _services.GetRequiredService<ShellViewModel>();
                Dispatcher.UIThread.Post(() => shell.SelectAppSectionAt(section), DispatcherPriority.Background);
            }

            if (ScrollOnStart is { } offset)
            {
                // After the page has been laid out, or the scroll viewer has no extent
                // to scroll within and the request is silently dropped.
                Dispatcher.UIThread.Post(
                    () => _window?.ScrollTo(offset),
                    DispatcherPriority.ApplicationIdle);
            }

            if (PresentFlyoutOnStart)
            {
                Dispatcher.UIThread.Post(ShowFlyout, DispatcherPriority.Background);
            }

            desktop.Exit += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts the splash up, in the form that fits how often this user has been here.
    ///
    /// A cold start gets the name; a repeat launch gets the mark alone, because after
    /// the first time the word is not information. "Has been here before" is read off
    /// the settings file: it is written the first time anything is saved, and its
    /// absence is the only durable evidence of a genuinely first run that this
    /// application already keeps.
    /// </summary>
    private void ShowSplash(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Started by the Run key to sit in the tray. Throwing a 640×400 window across
        // the middle of the screen is the exact opposite of what was asked for.
        bool startHidden = desktop.Args?.Contains("--minimised", StringComparer.OrdinalIgnoreCase) == true;
        if (startHidden && PinSplash is null) return;

        try
        {
            bool seenBefore = File.Exists(JsonSettingsStore.DefaultPath());

            _splash = new SplashWindow();
            _splash.Present(
                PinSplash ?? (seenBefore ? SplashForm.MarkOnly : SplashForm.Named),
                pinned: PinSplash is not null);
        }
#pragma warning disable CA1031 // A splash that fails to appear must not stop the app.
        catch (Exception ex)
        {
            _services?.GetService<ILoggerFactory>()?.CreateLogger("Splash").SplashFailed(ex);

            // Closed, not merely forgotten. Present shows the window before it finishes,
            // so dropping the reference here once left a splash on screen with nothing
            // able to dismiss it.
            _splash?.Close();
            _splash = null;
        }
#pragma warning restore CA1031
    }

    private static void ApplyWindowSize(Window window)
    {
        if (WindowSizeOnStart is not { } size) return;

        string[] parts = size.Split('x', StringSplitOptions.TrimEntries);

        if (parts.Length == 2
            && double.TryParse(parts[0], CultureInfo.InvariantCulture, out double width)
            && double.TryParse(parts[1], CultureInfo.InvariantCulture, out double height))
        {
            // Past the declared minimums on purpose: this exists to make a tall page fit
            // one screenshot, and MinHeight would silently clip the request.
            window.MinWidth = Math.Min(window.MinWidth, width);
            window.MinHeight = Math.Min(window.MinHeight, height);
            window.Width = width;
            window.Height = height;
        }
    }

    private void BuildTray()
    {
        // A tray icon that fails to appear must not take the application with it: the
        // device layer keeps working and the window is still reachable.
        try
        {
            var open = new NativeMenuItem(Maxwell.App.Localization.Localizer.Current.Say("Открыть KVAZR"));
            open.Click += (_, _) => ShowWindow();

            var quit = new NativeMenuItem(Maxwell.App.Localization.Localizer.Current.Say("Выход"));
            quit.Click += (_, _) => Shutdown();

            _tray = new TrayIcon
            {
                Icon = TrayIconRenderer.Render(KvazrMarkState.Searching),
                ToolTipText = Maxwell.App.Localization.Localizer.Current.Say("KVAZR — поиск устройств…"),
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

            // The connect popup used to fire here, on the became-usable transition.
            // Removed at the user's word — the tray menu is the announcement surface,
            // and a window sliding out near the clock was one surface too many.
        });

    private void RefreshTray()
    {
        if (_tray is null) return;

        // The mark, always. A percentage in the notification area answers "how much is
        // left" about exactly one device, and the application stopped being about one
        // device — with eight attached, whichever number the icon showed would be the
        // wrong one most of the time, and the setting that chose between them only moved
        // the wrongness around. The mark answers the question that survives the change:
        // whether anything wants attention.
        _tray.Icon = TrayIconRenderer.Render(MarkState());
        _tray.ToolTipText = Describe();
    }

    /// <summary>
    /// What the mark is currently saying. Firmware is passed as false because nothing
    /// installs firmware yet — the download was parked once it turned out the vendor's
    /// own application covers it — and the state is wired through rather than dropped
    /// so that the day something does write an image, one call site changes.
    /// </summary>
    private KvazrMarkState MarkState() => TrayState.For(
        [.. (_registry?.Devices ?? []).Select(TrayDeviceReading.From)],
        discovering: !_discovered,
        firmwareInstalling: false,
        lowBatteryThreshold: _settings?.Current.LowBatteryThreshold ?? 15);

    /// <summary>
    /// What the icon can no longer say. The mark carries one state for the whole
    /// machine, so the numbers move here: the devices that report a battery, most
    /// nearly flat first, because that is the one worth reading before the rest.
    ///
    /// Four at most. A Windows tooltip is a small box that appears under a pointer, and
    /// a list of eight lines in it is a list nobody reads.
    /// </summary>
    private string Describe()
    {
        List<DeviceSnapshot> devices =
            [.. (_registry?.Devices ?? []).Where(d => d.IsPresent && _hidden?.IsVisible(d) != false)];

        if (devices.Count == 0) return Maxwell.App.Localization.Localizer.Current.Say("KVAZR — устройств не найдено");

        List<string> lines =
        [
            .. devices
                .Where(d => d.BatteryPercent is not null)
                .OrderBy(d => d.BatteryPercent)
                .Take(4)
                .Select(d => $"{Maxwell.App.Localization.Localizer.Current.Say(d.Info.DisplayName)} — {d.BatteryPercent}%"),
        ];

        if (lines.Count == 0) return Maxwell.App.Localization.Localizer.Current.Say("KVAZR — устройств: {0}", devices.Count);

        return string.Join(Environment.NewLine, lines);
    }

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

        _splash?.Close();
        _splash = null;

        GC.SuppressFinalize(this);
    }
}
