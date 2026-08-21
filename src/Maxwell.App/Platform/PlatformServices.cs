using Maxwell.Core.Abstractions;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Linux;
using Microsoft.Extensions.DependencyInjection;

#if WINDOWS
using Maxwell.App.Platform.Windows;
using Maxwell.Race.Windows;
#else
using Maxwell.App.Platform.Linux;
#endif

namespace Maxwell.App.Platform;

/// <summary>
/// Picks the implementations for the running operating system.
///
/// Two layers of selection are at work and both are needed. Conditional compilation
/// decides what is even present in the assembly — the Windows toast package only ships
/// for the Windows target — while the runtime check decides what gets registered, so a
/// single portable build can still serve more than one OS.
/// </summary>
internal static class PlatformServices
{
    public static IServiceCollection AddPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

#if WINDOWS
        services.AddSingleton<IHidEnumerator, WindowsHidEnumerator>();
        services.AddSingleton<IHidTransportFactory, WindowsHidTransportFactory>();
        services.AddSingleton<IAutostart, WindowsAutostart>();
        services.AddSingleton<INotifier, WindowsNotifier>();
        services.AddSingleton<IBluetoothMonitor, Maxwell.Race.Windows.WindowsBluetoothMonitor>();
        services.AddSingleton<IHotkeyService, WindowsHotkeys>();

        // What makes the device list contain the user's actual devices rather than
        // only the ones this application has a driver for. Registered here because
        // it is platform code; the registry picks it up if it is present and does
        // without it on platforms where it is not.
        services.AddSingleton<ISystemDeviceEnumerator, Maxwell.Race.Windows.WindowsSystemDeviceEnumerator>();
#else
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IHidEnumerator, LinuxHidEnumerator>();
            services.AddSingleton<IHidTransportFactory, LinuxHidTransportFactory>();
            services.AddSingleton<IAutostart, LinuxAutostart>();
            services.AddSingleton<INotifier, LinuxNotifier>();
            services.AddSingleton<IBluetoothMonitor>(NullBluetoothMonitor.Instance);
        }
        else
        {
            // macOS needs an IOKit HID transport that does not exist yet. Registering
            // nothing would fail at resolve time, so the app runs with a device layer
            // that reports "not found" instead of crashing on startup.
            services.AddSingleton<IHidEnumerator, UnsupportedHidEnumerator>();
            services.AddSingleton<IHidTransportFactory, UnsupportedHidTransportFactory>();
            services.AddSingleton<IAutostart, UnsupportedAutostart>();
            services.AddSingleton<INotifier>(NullNotifier.Instance);
            services.AddSingleton<IBluetoothMonitor>(NullBluetoothMonitor.Instance);
        }

        // Global shortcuts are Windows-only for now; elsewhere they are simply absent.
        services.AddSingleton<IHotkeyService, NullHotkeyService>();
#endif

        return services;
    }
}
