using Kvazr.Core.Firmware;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.Versioning;
using Kvazr.Core.Abstractions;
using Kvazr.Core.Catalogue;
using Kvazr.Core.Devices;
using Kvazr.Core.Drivers.Mock;
using Kvazr.Core.Drivers.System;
using Kvazr.Core.Polling;
using Kvazr.Core.Services;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Kvazr.Core;

/// <summary>Composition root helpers. The host wires platform pieces; the core stays neutral.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the device layer. <see cref="IDeviceService"/> is a singleton by
    /// design: it is the sole owner of the headset, and a second instance would mean
    /// two callers racing for a device that cannot handle overlapping requests.
    /// </summary>
    public static IServiceCollection AddKvazrCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddHostedService<DevicePollingService>();
        services.TryAddNotifier();

        services.TryAddSingleton(TimeProvider.System);

        // One long-lived client against one fixed S3 host. IHttpClientFactory exists to
        // recycle handlers so DNS changes are picked up by long-running services; a
        // desktop app that talks to a single bucket a few times a year does not have
        // that problem, and the factory would be a package dependency for nothing.
        services.TryAddSingleton(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        });

        services.AddSingleton<IFirmwareCatalog, FirmwareCatalog>();
        services.AddSingleton<FirmwareUpdateService>();
        services.AddHostedService<FirmwareCheckService>();

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
        bool includeMockDevices = false,
        int mockDeviceCount = 2,
        params IDriverModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        // Two owners of the same hardware is the one failure this layer exists to
        // prevent, and it does not announce itself: the device answers the wrong
        // question instead of refusing. Fail at startup rather than in the field.
        if (services.Any(d => d.ServiceType == typeof(IDeviceService)))
        {
            throw new InvalidOperationException(
                "AddKvazrCore and AddDeviceRegistry both claim the device. Register one.");
        }

        services.AddSingleton<ICapabilityLedger, SettingsCapabilityLedger>();

        // One per application, and registered before the modules so that anything wanting
        // to read it during registration finds the same instance the registry will use.
        services.TryAddSingleton<DriverSwitchboard>();

        // The device families come in as modules — one per class protocol or per deep
        // device — so the core never names a driver and adding a family is adding an
        // assembly folder and one argument here, nothing else. Order does not matter:
        // the registry shadows on tier and merges on hardware id regardless.
        foreach (IDriverModule module in modules) module.Register(services);

        // Registered last of the real drivers, though order does not decide anything —
        // the registry shadows on tier. It is here so that a machine with nothing this
        // application understands still shows the user their devices rather than an
        // empty list, which is indistinguishable from the application being broken.
        services.TryAddSingleton<IDeviceCatalogue, DeviceCatalogue>();

        if (services.Any(d => d.ServiceType == typeof(ISystemDeviceEnumerator)))
        {
            services.AddSingleton<IDeviceDriver, SystemDeviceDriver>();
        }

        if (includeMockDevices)
        {
            services.AddSingleton<IDeviceDriver>(_ => new MockDeviceDriver(mockDeviceCount));
        }

        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();

        services.AddSingleton<DevicePollingScheduler>();
        services.AddSingleton<IPollingCoordinator>(sp => sp.GetRequiredService<DevicePollingScheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<DevicePollingScheduler>());

        services.TryAddNotifier();

        return services;
    }

    /// <summary>Registers the Windows HID transport.</summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsHid(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHidEnumerator, WindowsHidEnumerator>();
        services.AddSingleton<IHidListenerFactory, WindowsHidListenerFactory>();
        services.AddSingleton<IHidTransportFactory, WindowsHidTransportFactory>();
        services.AddSingleton<IBluetoothMonitor, WindowsBluetoothMonitor>();
        services.AddSingleton<ISystemDeviceEnumerator, WindowsSystemDeviceEnumerator>();

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
