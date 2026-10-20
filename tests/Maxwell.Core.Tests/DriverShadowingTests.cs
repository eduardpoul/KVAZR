using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// One driver finds every peripheral on the machine and another understands one family
/// of them properly. That overlap is the design working, not a fault — but left alone it
/// puts the same headset in the list twice, once with sixteen capabilities and once with
/// a battery reading, and the second row reads as a second headset.
/// </summary>
public sealed class DriverShadowingTests
{
    private static DeviceInfo Device(string key, string driver, params string[] hardwareIds) => new()
    {
        Key = key,
        DriverId = driver,
        Vendor = string.Empty,
        Model = key,
        HardwareIds = new HashSet<string>(hardwareIds, StringComparer.Ordinal),
    };

    private static async Task<IReadOnlyList<string>> Discover(params IDeviceDriver[] drivers)
    {
        using var registry = new DeviceRegistry(
            drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        await registry.DiscoverAsync(TestContext.Current.CancellationToken);

        return [.. registry.Devices.Select(d => d.Info.Key).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task TheVendorDriverKeepsTheDevice_AndTheSystemDriversCopyGoes()
    {
        IReadOnlyList<string> keys = await Discover(
            new FakeDriver("audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e")),
            new FakeDriver("system", DriverTier.System, Device("system:abc", "system", "usb:3329:4b1e", "container:1")));

        Assert.Equal(["audeze:maxwell"], keys);
    }

    [Fact]
    public async Task ADeviceNobodyElseClaims_Survives()
    {
        IReadOnlyList<string> keys = await Discover(
            new FakeDriver("audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e")),
            new FakeDriver("system", DriverTier.System, Device("system:mouse", "system", "usb:046d:c54d")));

        Assert.Equal(["audeze:maxwell", "system:mouse"], keys);
    }

    [Fact]
    public async Task MatchingIsOnIdentifiers_NotOnNames()
    {
        // The two drivers agree least about the name: one reads it from the USB product
        // string, the other has it written into a catalogue.
        IReadOnlyList<string> keys = await Discover(
            new FakeDriver("audeze.race", DriverTier.Vendor,
                Device("audeze:maxwell", "audeze.race", "bt:034275a08236") with { Model = "Maxwell" }),
            new FakeDriver("system", DriverTier.System,
                Device("system:bt", "system", "bt:034275a08236") with { Model = "Audeze Maxwell BT" }));

        Assert.Equal(["audeze:maxwell"], keys);
    }

    [Fact]
    public async Task TwoDriversAtTheSameTier_BothKeepTheirDevice()
    {
        // Not the expected overlap but a real conflict, and dropping one silently would
        // hide it. Two rows are wrong in a way somebody will report; one row is wrong in
        // a way nobody can see.
        IReadOnlyList<string> keys = await Discover(
            new FakeDriver("a", DriverTier.Vendor, Device("a:one", "a", "usb:1234:5678")),
            new FakeDriver("b", DriverTier.Vendor, Device("b:one", "b", "usb:1234:5678")));

        Assert.Equal(["a:one", "b:one"], keys);
    }

    [Fact]
    public async Task ADeviceWithNoIdentifiers_IsNeverShadowed()
    {
        // Nothing is known about it, so nothing can be concluded about it either. The
        // empty set must not be treated as matching everything.
        IReadOnlyList<string> keys = await Discover(
            new FakeDriver("audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e")),
            new FakeDriver("system", DriverTier.System, Device("system:mystery", "system")));

        Assert.Equal(["audeze:maxwell", "system:mystery"], keys);
    }

    /// <summary>
    /// A receiver belongs to the device it is paired with even while that device is off
    /// the air. Charging on its cable or switched off, the mouse is still the one the
    /// receiver is for — and it was exactly then that a bare "USB Receiver" came back into
    /// the list beside it.
    /// </summary>
    [Fact]
    public async Task AReceiverStaysUnderItsMouse_WhileTheMouseIsOffTheAir()
    {
        DeviceInfo mouse = Device("hidpp:unit:2121B490", "logitech.hidpp", "usb:046d:c54d") with
        {
            Transports = [new TransportInfo { Kind = TransportKind.Dongle, Label = "USB dongle", IsPresent = false }],
        };

        using var registry = new DeviceRegistry(
            [
                new FakeDriver("logitech.hidpp", DriverTier.Class, mouse),
                new FakeDriver("system", DriverTier.System,
                    Device("system:receiver", "system", "usb:046d:c54d", "container:7")),
            ],
            new InMemoryCapabilityLedger(),
            NullLoggerFactory.Instance);

        await registry.DiscoverAsync(TestContext.Current.CancellationToken);

        DeviceSnapshot only = Assert.Single(registry.Devices);
        Assert.Equal("hidpp:unit:2121B490", only.Info.Key);
        Assert.Equal(DeviceAvailability.Offline, only.Availability);
    }

    private sealed class FakeDriver(string id, DriverTier tier, params DeviceInfo[] devices) : IDeviceDriver
    {
        public string DriverId => id;

        public DriverTier Tier => tier;

        public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);

        public Task<IDeviceConnection> ConnectAsync(
            DeviceInfo device,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Shadowing is decided before anything is opened.");
    }
}
