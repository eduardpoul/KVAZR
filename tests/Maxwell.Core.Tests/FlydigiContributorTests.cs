using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Flydigi;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The pad's charge is offered to the gamepad its dongle belongs to and to nothing else,
/// and only when the dongle carries the collection the question goes to.
/// </summary>
public sealed class FlydigiContributorTests
{
    private static readonly HidInterface Commands =
        new("dongle-ffa0", 0x37D7, 0x2501, 0xFFA0, 0x01, 32, 32, 0, "Flydigi APEX5 Wireless");

    private static DeviceInfo Device(params string[] ids) => new()
    {
        Key = "system:flydigi-apex-5",
        DriverId = "system",
        Vendor = "Flydigi",
        Model = "Flydigi APEX 5",
        Class = DeviceClass.Gamepad,
        HardwareIds = new HashSet<string>(ids, StringComparer.Ordinal),
    };

    private static async Task<IDeviceConnection?> Attach(DeviceInfo device, params HidInterface[] interfaces) =>
        await new FlydigiContributor(new Enumerator(interfaces))
            .AttachAsync(device, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ThePadOnItsDongle_GetsACharge()
    {
        using IDeviceConnection? connection = await Attach(Device("usb:37d7:2501", "usb:37d7:6001"), Commands);

        Assert.NotNull(connection);
        Assert.Contains(connection.Capabilities, c => c.Id == CapabilityIds.PowerBattery);
        Assert.Contains(connection.Capabilities, c => c.Id == CapabilityIds.PowerCharging);
        Assert.Empty(connection.Transports);
    }

    /// <summary>The dock alone is the pad switched off: nothing to ask, so nothing attached.</summary>
    [Fact]
    public async Task TheDockAlone_GetsNothing()
    {
        Assert.Null(await Attach(Device("usb:37d7:6001"), Commands));
    }

    [Fact]
    public async Task ADongleWithoutTheCommandCollection_GetsNothing()
    {
        Assert.Null(await Attach(Device("usb:37d7:2501")));
    }

    [Fact]
    public async Task SomebodyElsesDevice_GetsNothing()
    {
        Assert.Null(await Attach(Device("usb:046d:c54d"), Commands));
    }

    private sealed class Enumerator(params HidInterface[] interfaces) : IHidEnumerator
    {
        public IReadOnlyList<MaxwellInterface> Enumerate() => [];

        public IReadOnlyList<HidInterface> EnumerateAll() => interfaces;

        public IReadOnlyList<MaxwellInterface> EnumerateRaceInterfaces() => [];
    }
}
