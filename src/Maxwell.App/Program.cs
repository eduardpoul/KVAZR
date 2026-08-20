using Avalonia;
using Maxwell.App.ViewModels;
using Maxwell.App.Views;
using Maxwell.Core;
using Maxwell.Core.Services;
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

        builder.Services.Configure<PollingOptions>(
            builder.Configuration.GetSection(PollingOptions.SectionName));

        builder.Services.AddWindowsHid();
        builder.Services.AddMaxwellCore();

        builder.Services.AddSingleton<MainWindowViewModel>();
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
