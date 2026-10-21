using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Kvazr.Core.Drivers.Audio;

/// <summary>
/// The audio class module: one implementation, three categories of device.
///
/// Headsets, microphones and speakers all present the same four things to Core Audio, so
/// the baseline page for all three comes from here rather than from three drivers. It
/// registers a contributor and no driver at all — audio never owns a device, it only ever
/// says more about one.
///
/// Requires the system enumerator, which is how an endpoint is tied back to the device it
/// belongs to. Without it the module registers nothing rather than attaching endpoints to
/// guesses.
/// </summary>
public sealed class AudioModule : IDriverModule
{
    public string Name => "audio";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(IAudioEndpoints))) return;
        if (!services.Any(d => d.ServiceType == typeof(ISystemDeviceEnumerator))) return;

        services.AddSingleton<ICapabilityContributor, AudioContributor>();
    }
}
