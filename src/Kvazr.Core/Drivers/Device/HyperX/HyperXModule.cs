using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kvazr.Core.Drivers.HyperX;

/// <summary>
/// One microphone's own status channel, contributed to the device the system already
/// found.
///
/// Registers a contributor and no driver, for the same reason the audio module does: it
/// never owns a device, it only ever says more about one. Requires a listener factory,
/// and registers nothing without it — on a platform where nothing can open a HID
/// collection, the QuadCast is still a working microphone with a full baseline page.
/// </summary>
public sealed class HyperXModule : IDriverModule
{
    public string Name => "hyperx";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(IHidListenerFactory))) return;

        services.AddSingleton<ICapabilityContributor, HyperXContributor>();
    }
}
