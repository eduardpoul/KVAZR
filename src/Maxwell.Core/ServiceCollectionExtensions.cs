using System.Runtime.Versioning;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.Mock;
using Maxwell.Core.Services;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.Core;

/// <summary>Composition root helpers. The host wires platform pieces; the core stays neutral.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the device layer. <see cref="IDeviceService"/> is a singleton by
    /// design: it is the sole owner of the headset, and a second instance would mean
    /// two callers racing for a device that cannot handle overlapping requests.
    /// </summary>
    public static IServiceCollection AddMaxwellCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddHostedService<DevicePollingService>();
        services.TryAddNotifier();

        return services;
    }

    /// <summary>
    /// Registers the multi-device layer: one registry owning one session per device,
    /// each session the sole owner of its hardware. Runs alongside the older
    /// single-headset <see cref="IDeviceService"/> until the shell moves across.
    /// </summary>
    /// <param name="includeMockDevices">
    /// Adds a stand-in mouse and keyboard so the shell can be developed against three
    /// device classes. Development only — a device list containing hardware the user
    /// does not own is worse than an empty one.
    /// </param>
    public static IServiceCollection AddDeviceRegistry(
        this IServiceCollection services,
        bool includeMockDevices = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICapabilityLedger, SettingsCapabilityLedger>();
        services.AddSingleton<IDeviceDriver, AudezeRaceDriver>();

        if (includeMockDevices) services.AddSingleton<IDeviceDriver, MockDeviceDriver>();

        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();

        return services;
    }

    /// <summary>Registers the Windows HID transport.</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsHid(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHidEnumerator, WindowsHidEnumerator>();
        services.AddSingleton<IHidTransportFactory, WindowsHidTransportFactory>();

        return services;
    }

    private static void TryAddNotifier(this IServiceCollection services)
    {
        // A host that has a real notifier registers it before calling this; otherwise
        // the core stays silent rather than failing to resolve.
        if (services.Any(d => d.ServiceType == typeof(INotifier))) return;
        services.AddSingleton<INotifier>(NullNotifier.Instance);
    }
}
