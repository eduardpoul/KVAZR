using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.Logitech;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The module seam: a module registers exactly its own driver, and the composition root
/// is the only place that names one. If a family's registration ever drifts back into
/// the core, this is where it shows.
/// </summary>
public sealed class DriverModuleTests
{
    [Fact]
    public void AModule_RegistersItsDriver_AndNamesItself()
    {
        var services = new ServiceCollection();

        var module = new AudezeModule();
        module.Register(services);

        Assert.Equal("audeze", module.Name);
        Assert.Contains(services, d => d.ServiceType == typeof(IDeviceDriver));
    }

    [Fact]
    public void TheLogitechModule_IsPlatformHonest()
    {
        // On Windows it registers its HID++ driver; elsewhere it registers nothing and
        // the same composition root still works. The assertion tracks the running host.
        var services = new ServiceCollection();
        new LogitechModule().Register(services);

        int expected = OperatingSystem.IsWindows() ? 1 : 0;
        Assert.Equal(expected, services.Count(d => d.ServiceType == typeof(IDeviceDriver)));
    }

    [Fact]
    public void TheFlydigiModule_AddsAContributor_AndNoDriver()
    {
        // It never owns the pad; the system tier does. It only says what nobody else can.
        var services = new ServiceCollection();
        new Maxwell.Core.Drivers.Flydigi.FlydigiModule().Register(services);

        int expected = OperatingSystem.IsWindows() ? 1 : 0;
        Assert.Equal(expected, services.Count(d => d.ServiceType == typeof(ICapabilityContributor)));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDeviceDriver));
    }
}
