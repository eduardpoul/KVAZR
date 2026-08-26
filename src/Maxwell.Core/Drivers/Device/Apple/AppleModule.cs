using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.Core.Drivers.Apple;

/// <summary>
/// The battery of Apple earphones, contributed to the device the system already found.
///
/// Registers a contributor and no driver, and registers nothing at all where nothing can
/// listen to the air. On such a build these earphones are still a working headset with a
/// full baseline page — sound, microphone, volume, recording level — and simply no charge,
/// which is exactly what they were before this existed.
/// </summary>
public sealed class AppleModule : IDriverModule
{
    public string Name => "apple";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(IProximityListener))) return;

        services.AddSingleton<ICapabilityContributor, AppleProximityContributor>();
    }
}
