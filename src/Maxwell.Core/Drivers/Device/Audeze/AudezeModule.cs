using Maxwell.Core.Devices;

using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// The Audeze module: the deepest kind of support, one vendor's own protocol reversed.
///
/// The template for every "как одизи" device family. It owns exactly one driver and
/// registers it; the RACE protocol, the capability descriptions and the two connection
/// kinds it needs are its own business, invisible to the composition root. A second
/// deep device — Flydigi, Sony — is a folder beside this one with a module just like it,
/// and one line at the root.
/// </summary>
public sealed class AudezeModule : IDriverModule
{
    public string Name => "audeze";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDeviceDriver, AudezeRaceDriver>();
    }
}
