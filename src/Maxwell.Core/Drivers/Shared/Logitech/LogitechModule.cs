using Maxwell.Core.Devices;

using Microsoft.Extensions.DependencyInjection;

namespace Maxwell.Core.Drivers.Logitech;

/// <summary>
/// The Logitech module: a class driver, one protocol covering a whole ecosystem.
///
/// The template for shared-protocol support. HID++ is Logitech-wide — mice, keyboards,
/// trackballs, whatever is paired to a receiver — so this one module speaks for the
/// entire family by protocol rather than by model. Windows-only for now, because its
/// channel is; on another platform it registers nothing and the composition root does
/// not change.
/// </summary>
public sealed class LogitechModule : IDriverModule
{
    public string Name => "logitech";

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!OperatingSystem.IsWindows()) return;

        services.AddSingleton<IDeviceDriver, HidPlusPlusDriver>();
    }
}
