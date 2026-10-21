using Kvazr.Core.Abstractions;
using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Linux;
using Microsoft.Extensions.DependencyInjection;

#if WINDOWS
using Kvazr.App.Platform.Windows;
using Kvazr.Race.Windows;
#else
using Kvazr.App.Platform.Linux;
#endif

namespace Kvazr.App.Platform;

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

        // An ear, as distinct from the transport above: some devices answer when asked
        // and some only announce. A module that needs one checks for it and registers
        // nothing without it, which is why leaving this out cost a row that worked
        // everywhere except in the application.
        services.AddSingleton<IHidListenerFactory, Kvazr.Race.Windows.WindowsHidListenerFactory>();
        services.AddSingleton<IAutostart, WindowsAutostart>();
        services.AddSingleton<INotifier, WindowsNotifier>();
        services.AddSingleton<IBluetoothMonitor, Kvazr.Race.Windows.WindowsBluetoothMonitor>();
        services.AddSingleton<IHotkeyService, WindowsHotkeys>();

        // What makes the device list contain the user's actual devices rather than
        // only the ones this application has a driver for. Registered here because
        // it is platform code; the registry picks it up if it is present and does
        // without it on platforms where it is not.
        services.AddSingleton<ISystemDeviceEnumerator, Kvazr.Race.Windows.WindowsSystemDeviceEnumerator>();

        // One radio listener for the machine, shared by every device that broadcasts its
        // state instead of answering questions. Registered here rather than in the core
        // because it is WinRT and the core is platform-free; a host without it registers
        // no Apple contributor and those devices simply have no charge row.
        services.AddSingleton<IProximityListener, Kvazr.Race.Windows.WindowsProximityListener>();
        services.AddSingleton<IHidReportPipeFactory, Kvazr.Race.Windows.WindowsHidReportPipeFactory>();

        // Core Audio: volume, mute and level for every audio device on the machine.
        // A platform service rather than something inside a driver, because three
        // categories at once — headsets, microphones, speakers — draw their baseline
        // page from it, and none of them owns it.
        services.AddSingleton<IAudioEndpoints, Kvazr.Race.Windows.WindowsAudioEndpoints>();

        // Being told the moment a device arrives beats asking every few seconds whether
        // one has. The sweep stays as the safety net; this is what makes it feel instant.
        services.AddSingleton<IDeviceChangeWatcher, Kvazr.Race.Windows.WindowsDeviceChangeWatcher>();

        // Unpairing, which is two implementations on one platform: the classic call for
        // a headset and the modern pairing object for anything low-energy. Registered
        // here rather than called directly, because only this target can see the second
        // one at all.
        services.AddSingleton<IPairingRemoval, WindowsPairingRemoval>();
#else
        services.AddSingleton<IPairingRemoval>(UnsupportedPairingRemoval.Instance);

        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IHidEnumerator, LinuxHidEnumerator>();
            services.AddSingleton<IHidTransportFactory, LinuxHidTransportFactory>();
            services.AddSingleton<IAutostart, LinuxAutostart>();
            services.AddSingleton<INotifier, LinuxNotifier>();
            services.AddSingleton<IBluetoothMonitor>(NullBluetoothMonitor.Instance);
            services.AddSingleton<IDeviceChangeWatcher>(NullDeviceChangeWatcher.Instance);
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
            services.AddSingleton<IDeviceChangeWatcher>(NullDeviceChangeWatcher.Instance);
        }

        // Global shortcuts are Windows-only for now; elsewhere they are simply absent.
        services.AddSingleton<IHotkeyService, NullHotkeyService>();
#endif

        return services;
    }
}
