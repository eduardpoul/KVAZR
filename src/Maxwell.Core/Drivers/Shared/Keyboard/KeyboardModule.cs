using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.Core.Drivers.Keyboard;

/// <summary>
/// Registers the keyboard class tier, where the host offers a way to ask a collection.
///
/// Conditional, like the other class modules: a host with no pipe factory registers no
/// contributor, and the keyboards still appear — plainer, with what the system knows and
/// nothing more.
/// </summary>
public sealed class KeyboardModule : IDriverModule
{
    public string Name => "keyboard";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICapabilityContributor>(sp =>
            new ViaContributor(
                sp.GetRequiredService<IHidReportPipeFactory>(),
                sp.GetService<IDeviceCatalogue>()));
    }
}
