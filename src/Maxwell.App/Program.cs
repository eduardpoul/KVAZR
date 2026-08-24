using Avalonia;
using Maxwell.App.ViewModels;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.ViewModels.Pages;
using Maxwell.App.Views;
using Maxwell.Core;
using Maxwell.App.Platform;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Polling;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maxwell.App;


internal static class Program
{
    /// <summary>
    /// The generic host owns configuration, logging and the polling background service;
    /// Avalonia owns the UI thread. The host is started first so the device is already
    /// being discovered by the time the window appears.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // A 16-pixel glyph cannot be judged from source, so there is a way to render
        // every variant and look at it. No host, no device, no window.
        if (Array.IndexOf(args, "--dump-icons") is var at and >= 0 && at + 1 < args.Length)
        {
            return DumpIcons(args[at + 1]);
        }

        // The same question one level up: whether a silhouette reads at the size a device
        // row actually gives it, and whether it survives both themes. Also the only
        // honest way to find out whether the bundled WebP files decode at all.
        if (Array.IndexOf(args, "--dump-thumbs") is var thumbs and >= 0 && thumbs + 1 < args.Length)
        {
            return DumpThumbs(args[thumbs + 1]);
        }

        // Regenerates src/Maxwell.App/Assets/kvazr.ico from the same geometry the rest
        // of the application draws. Committed rather than generated during the build so
        // that a build does not need a render surface, but generated rather than drawn
        // by hand so the tile and the tray can never disagree.
        if (Array.IndexOf(args, "--write-icon") is var ico and >= 0 && ico + 1 < args.Length)
        {
            return WriteIcon(args[ico + 1]);
        }

        // Same argument as the icons, one phase later: the design-system matrix is wider
        // than any window on this machine, and checking a control against the artboard
        // means looking at it in both themes.
        if (Array.IndexOf(args, "--dump-pages") is var pages and >= 0 && pages + 1 < args.Length)
        {
            return DumpPages(args[pages + 1]);
        }

        // One instance, and this is not a preference.
        //
        // WebView2 keeps its user data in a folder owned by exactly one process, and the
        // content pane is a WebView2. A second copy of the application therefore does not
        // start alongside the first — it throws "the requested resource is in use"
        // (0x800700AA) out of a component nobody here wrote, and the user is shown a
        // .NET crash box with a Continue button that continues into a window with no
        // content in it.
        //
        // That was reachable by double-clicking the shortcut of an application whose
        // whole point is to sit in the tray, which is to say: reachable by anybody.
        var only = new Mutex(true, @"Local\KVAZR.SingleInstance", out bool first);

        if (!first)
        {
            RaiseTheRunningOne();
            return 0;
        }

        using (only)
        {
            return Run(args);
        }
    }

    /// <summary>
    /// Brings the copy that is already running to the front, so a second launch does
    /// something rather than appearing to do nothing.
    ///
    /// It may find nothing to raise, and that is a normal outcome: the application starts
    /// to the tray by default and has no window until somebody asks for one. Its icon is
    /// already there, which is the answer to "where did it go".
    /// </summary>
    private static void RaiseTheRunningOne()
    {
        using Process me = Process.GetCurrentProcess();

        foreach (Process other in Process.GetProcessesByName(me.ProcessName))
        {
            using (other)
            {
                if (other.Id == me.Id || other.MainWindowHandle == IntPtr.Zero) continue;

                _ = Native.ShowWindow(other.MainWindowHandle, Native.Restore);
                _ = Native.SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    private static int Run(string[] args)
    {
        IHost host = CreateHost(args);

        // Before the host starts, because starting it starts discovery.
        //
        // This was done from the Avalonia side at first, where the settings file is read
        // for the theme, and the log said what that was worth: the first sweep ran with
        // every driver enabled and only the second one respected the switches. A library
        // switched off had already found and opened its hardware by the time it was told
        // not to, which for --no-vendor means opening the headset over HID — the exact
        // thing the flag exists to avoid.
        RestoreLibrarySwitches(host.Services, args);

        host.Start();

        // A panel that only appears on a tray click cannot be looked at while working
        // on it; --flyout presents it at startup.
        // A panel that only appears on a tray click cannot be looked at while working on
        // it. The name after it opens one row, for the same reason --open takes one: the
        // rows are ordered by name and a click is not available to a capture.
        if (Array.IndexOf(args, "--flyout") is var panel and >= 0)
        {
            App.PresentFlyoutOnStart = true;

            if (panel + 1 < args.Length && !args[panel + 1].StartsWith('-'))
            {
                App.OpenFlyoutRowNamed = args[panel + 1];
            }
        }

        // The splash is gone within a second of the shell appearing, which is right for
        // a user and useless for looking at it. These hold it up instead.
        if (Array.IndexOf(args, "--splash") >= 0) App.PinSplash = SplashForm.Named;
        if (Array.IndexOf(args, "--splash-mark") >= 0) App.PinSplash = SplashForm.MarkOnly;

        // The artboards are drawn light and the machine this is developed on runs dark,
        // so without this every comparison was made across two different themes — which
        // is how a page can look "close enough" while being wrong about its geometry.
        if (Array.IndexOf(args, "--theme") is var theme and >= 0 && theme + 1 < args.Length)
        {
            App.ThemeOnStart = args[theme + 1] switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                _ => AppTheme.System,
            };
        }

        // Same argument for the language, and one more: checking a translation used to
        // mean editing the user's settings file, starting the application, and putting the
        // file back — three chances to leave somebody else's configuration changed.
        if (Array.IndexOf(args, "--lang") is var lang and >= 0 && lang + 1 < args.Length)
        {
            App.LanguageOnStart = args[lang + 1];
        }

        // A page taller than the window can only be reviewed a third at a time, and
        // scrolling it means driving the mouse, which takes the cursor away from
        // whoever is sitting at the machine. Resizing the window costs nobody anything.
        if (Array.IndexOf(args, "--size") is var size and >= 0 && size + 1 < args.Length)
        {
            App.WindowSizeOnStart = args[size + 1];
        }

        if (Array.IndexOf(args, "--scroll") is var scroll and >= 0
            && scroll + 1 < args.Length
            && double.TryParse(args[scroll + 1], System.Globalization.CultureInfo.InvariantCulture, out double offset))
        {
            App.ScrollOnStart = offset;
        }

        // A number or a piece of the name. The number was there first and kept being
        // wrong: the list is ordered by name and fills over a minute, so "the fourth
        // device" means different things depending on how much of the sweep has landed.
        if (Array.IndexOf(args, "--open") is var open and >= 0 && open + 1 < args.Length)
        {
            if (int.TryParse(args[open + 1], out int index)) App.OpenDeviceOnStart = index;
            else App.OpenDeviceNamed = args[open + 1];
        }

        // The content pane renders in a WebView by default on Windows; --native falls
        // back to the Avalonia views, which is also what every capture pipeline that
        // cannot see into a child HWND wants.
        if (Array.IndexOf(args, "--native") >= 0) Web.WebUi.Enabled = false;

        if (Array.IndexOf(args, "--section") is var sec and >= 0
            && sec + 1 < args.Length
            && int.TryParse(args[sec + 1], out int sectionIndex))
        {
            App.OpenSectionOnStart = sectionIndex;
        }

        // The application's own pages are now reached through the pane's pinned row and
        // then the group it reveals — two clicks, both of them pointer work. Same reason
        // as --open: a screenshot of the wrong page proves nothing.
        if (Array.IndexOf(args, "--app") is var app and >= 0
            && app + 1 < args.Length
            && int.TryParse(args[app + 1], out int section))
        {
            App.OpenAppSectionOnStart = section;
        }

        try
        {
            return BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            host.Dispose();
        }
    }

    private static int DumpIcons(string path)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        Tray.TrayIconPreview.Dump(path);

        Console.WriteLine($"wrote {path}");
        return 0;
    }

    private static int DumpThumbs(string path)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        Imaging.DeviceImagePreview.Dump(path);

        Console.WriteLine($"wrote {path}");
        return 0;
    }

    private static int WriteIcon(string path)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        Branding.KvazrAppIcon.WriteIco(path, Branding.KvazrSurface.Dark);

        Console.WriteLine($"wrote {path}");
        return 0;
    }

    private static int DumpPages(string path)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
        Views.Pages.PagePreview.Dump(path);

        Console.WriteLine($"wrote {path}");
        return 0;
    }

    private static IHost CreateHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<DevicePollingOptions>(
            builder.Configuration.GetSection(DevicePollingOptions.SectionName));

        builder.Services.AddPlatform();
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        builder.Services.AddSingleton(sp => new PrimaryDevice(
            sp.GetRequiredService<IDeviceRegistry>(),
            sp.GetRequiredService<HiddenDevices>(),
            sp.GetRequiredService<ISettingsStore>()));
        builder.Services.AddSingleton<HotkeyCoordinator>();

        // The stand-in mouse and keyboard are development scaffolding: ten of the
        // design's artboards are about devices that are not on this desk, and without
        // something to render the shell would be built headset-shaped.
        // --mock-crowd fills the list out so the crowded cases can be looked at; the
        // panel's height ceiling is specified at six devices and two will not reach it.
        int mocks = Array.IndexOf(args, "--mock-crowd") >= 0 ? 5 : 2;
        // The device families this build understands, listed once. A new class protocol
        // or a new deep device is one more module here and its folder under Drivers — the
        // core and the other modules do not move. See docs/ARCHITECTURE.md.
        // Every module is registered, always. --no-vendor used to work by leaving the
        // deep ones out of this list, which made it a startup-only, all-or-nothing
        // answer to a question the developer section now answers one library at a time
        // and while the application is running. The flag survives as a shortcut into
        // that switchboard — see App.NoVendorOnStart — rather than as a second
        // mechanism that can disagree with it.
        List<IDriverModule> modules =
        [
            new Maxwell.Core.Drivers.Audeze.AudezeModule(),
            new Maxwell.Core.Drivers.HyperX.HyperXModule(),
            new Maxwell.Core.Drivers.Logitech.LogitechModule(),
            new Maxwell.Core.Drivers.Audio.AudioModule(),
        ];

        builder.Services.AddDeviceRegistry(includeMockDevices: true, mockDeviceCount: mocks, [.. modules]);

        builder.Services.AddSingleton<IConfirmationPrompt>(sp =>
            new ModalConfirmation(sp.GetRequiredService<MainWindow>));

        builder.Services.AddSingleton<Localization.Localizer>();
        builder.Services.AddSingleton<Infrastructure.CurvePresetLibrary>();
        builder.Services.AddSingleton<Infrastructure.FirstSweep>();
        builder.Services.AddSingleton<HotkeysViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<AboutViewModel>();
        builder.Services.AddSingleton<Platform.HiddenDevices>();
        builder.Services.AddSingleton<Platform.TrayExtras>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<FlyoutViewModel>();
        builder.Services.AddSingleton<FlyoutWindow>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    /// <summary>
    /// Puts the developer section's switches back, and applies --no-vendor through the
    /// same set rather than as a mechanism of its own.
    ///
    /// The settings file is read here and again by the shell a moment later, which is two
    /// reads of two kilobytes and buys one invariant worth more than that: nothing is
    /// discovered before the switches are known.
    /// </summary>
    private static void RestoreLibrarySwitches(IServiceProvider services, string[] args)
    {
        var store = services.GetRequiredService<ISettingsStore>();
        store.LoadAsync().GetAwaiter().GetResult();

        var off = new HashSet<string>(store.Current.DisabledLibraries, StringComparer.Ordinal);

        // Every driver that speaks one vendor's protocol, off for this run. Not written
        // back to the file, so a launch without the flag is a build with them again.
        if (Array.IndexOf(args, "--no-vendor") >= 0)
        {
            foreach (IDeviceDriver driver in services.GetServices<IDeviceDriver>())
            {
                if (driver.Tier == DriverTier.Vendor) off.Add(driver.DriverId);
            }
        }

        services.GetRequiredService<DriverSwitchboard>().Replace(off);
    }

    private static class Native
    {
        public const int Restore = 9;

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr window);
    }

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new App(services))
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>Parameterless entry point required by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
