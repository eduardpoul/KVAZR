using Kvazr.Core.Catalogue;
using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Kvazr.Core.Drivers.Keyboard;

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
