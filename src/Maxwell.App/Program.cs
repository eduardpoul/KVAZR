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

        IHost host = CreateHost(args);
        host.Start();

        // A panel that only appears on a tray click cannot be looked at while working
        // on it; --flyout presents it at startup.
        App.PresentFlyoutOnStart = Array.IndexOf(args, "--flyout") >= 0;

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

        if (Array.IndexOf(args, "--open") is var open and >= 0
            && open + 1 < args.Length
            && int.TryParse(args[open + 1], out int index))
        {
            App.OpenDeviceOnStart = index;
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
        // --no-vendor drops the deep drivers and leaves the class protocols, so a device
        // that has a vendor driver falls through to the shared page and that page can be
        // looked at on real hardware. The only headset on this desk is an Audeze: without
        // this switch the baseline headset page could be guessed at and never seen.
        List<IDriverModule> modules =
        [
            new Maxwell.Core.Drivers.Logitech.LogitechModule(),
            new Maxwell.Core.Drivers.Audio.AudioModule(),
        ];

        if (Array.IndexOf(args, "--no-vendor") < 0)
        {
            modules.Insert(0, new Maxwell.Core.Drivers.Audeze.AudezeModule());
        }

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
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<FlyoutViewModel>();
        builder.Services.AddSingleton<FlyoutWindow>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
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
