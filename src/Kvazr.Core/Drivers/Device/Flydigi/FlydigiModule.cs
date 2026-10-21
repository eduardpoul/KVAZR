using Kvazr.Core.Devices;

using Microsoft.Extensions.DependencyInjection;

namespace Kvazr.Core.Drivers.Flydigi;

/// <summary>
/// A Flydigi pad's own channel, contributed to the gamepad the system already found.
///
/// A contributor and no driver, as with HyperX: it never owns the device, it only says
/// what nobody else can. Windows-only for now, because its channel is; elsewhere the pad
/// is still a gamepad with its dongle and dock, just without a charge.
/// </summary>
public sealed class FlydigiModule : IDriverModule
{
    public string Name => "flydigi";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!OperatingSystem.IsWindows()) return;

        services.AddSingleton<ICapabilityContributor, FlydigiContributor>();
    }
}
