using Avalonia;
using Maxwell.App.ViewModels;
using Maxwell.App.ViewModels.Pages;
using Maxwell.App.Views;
using Maxwell.Core;
using Maxwell.App.Platform;
using Maxwell.App.Platform.Windows;
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
        IHost host = CreateHost(args);
        host.Start();

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

    private static IHost CreateHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<DevicePollingOptions>(
            builder.Configuration.GetSection(DevicePollingOptions.SectionName));

        builder.Services.AddWindowsHid();

        // Platform services must be registered before the core so its notifier
        // fallback does not win the registration.
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        builder.Services.AddSingleton<IAutostart, WindowsAutostart>();
        builder.Services.AddSingleton<INotifier, WindowsNotifier>();
        builder.Services.AddSingleton<IHotkeyService, WindowsHotkeys>();
        builder.Services.AddSingleton<PrimaryDevice>();
        builder.Services.AddSingleton<HotkeyCoordinator>();

        // The stand-in mouse and keyboard are development scaffolding: ten of the
        // design's artboards are about devices that are not on this desk, and without
        // something to render the shell would be built headset-shaped.
        builder.Services.AddDeviceRegistry(includeMockDevices: true);

        builder.Services.AddSingleton<CapabilityShowcaseViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
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
