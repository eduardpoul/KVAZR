using Avalonia;
using Maxwell.App.ViewModels;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.ViewModels.Pages;
using Maxwell.App.Views;
using Maxwell.Core;
using Maxwell.App.Platform;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Polling;
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

        IHost host = CreateHost(args);
        host.Start();

        // A panel that only appears on a tray click cannot be looked at while working
        // on it; --flyout presents it at startup.
        App.PresentFlyoutOnStart = Array.IndexOf(args, "--flyout") >= 0;
        App.PresentPopupOnStart = Array.IndexOf(args, "--popup") >= 0;

        if (Array.IndexOf(args, "--open") is var open and >= 0
            && open + 1 < args.Length
            && int.TryParse(args[open + 1], out int index))
        {
            App.OpenDeviceOnStart = index;
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

    private static IHost CreateHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<DevicePollingOptions>(
            builder.Configuration.GetSection(DevicePollingOptions.SectionName));

        builder.Services.AddPlatform();
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        builder.Services.AddSingleton<PrimaryDevice>();
        builder.Services.AddSingleton<HotkeyCoordinator>();

        // The stand-in mouse and keyboard are development scaffolding: ten of the
        // design's artboards are about devices that are not on this desk, and without
        // something to render the shell would be built headset-shaped.
        // --mock-crowd fills the list out so the crowded cases can be looked at; the
        // panel's height ceiling is specified at six devices and two will not reach it.
        int mocks = Array.IndexOf(args, "--mock-crowd") >= 0 ? 5 : 2;
        builder.Services.AddDeviceRegistry(includeMockDevices: true, mockDeviceCount: mocks);

        builder.Services.AddSingleton<IConfirmationPrompt>(sp =>
            new ModalConfirmation(sp.GetRequiredService<MainWindow>));

        builder.Services.AddSingleton<CapabilityShowcaseViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<FlyoutViewModel>();
        builder.Services.AddSingleton<FlyoutWindow>();
        builder.Services.AddSingleton<ConnectPopupWindow>();
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
