using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The developer section's switches, and what the registry does when one moves.
///
/// The point of them is a comparison that cannot otherwise be made on this desk: the
/// registry always keeps the deepest driver that recognises a device, so the only headset
/// here being an Audeze meant the shared headset page could be guessed at and never seen.
/// Switching the vendor driver off hands the same hardware to the layer underneath.
///
/// Which is why the cases below are mostly about the handover rather than about the flag:
/// a switch that left the old device behind, or that needed a restart to take effect,
/// would not answer the question it exists for.
/// </summary>
public sealed class DriverSwitchboardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void EverythingIsOn_UntilSomethingIsSwitchedOff()
    {
        var switches = new DriverSwitchboard();

        // Including ids it has never heard of. A driver added to a build that predates
        // the file it reads must not come up disabled.
        Assert.True(switches.IsOn("audeze.race"));
        Assert.Empty(switches.SwitchedOff);
    }

    [Fact]
    public void SwitchingSomethingOff_IsRememberedAndAnnounced()
    {
        var switches = new DriverSwitchboard();
        int announced = 0;
        switches.Changed += (_, _) => announced++;

        switches.Set("audeze.race", false);

        Assert.False(switches.IsOn("audeze.race"));
        Assert.Equal(["audeze.race"], switches.SwitchedOff);
        Assert.Equal(1, announced);
    }

    [Fact]
    public void SettingWhatIsAlreadySet_AnnouncesNothing()
    {
        var switches = new DriverSwitchboard();
        switches.Set("audeze.race", false);

        int announced = 0;
        switches.Changed += (_, _) => announced++;

        switches.Set("audeze.race", false);
        switches.Set("logitech.hidpp", true);

        // Every announcement costs a full rebuild of every session, so a repeat that
        // changes nothing must not trigger one.
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task ADriverSwitchedOff_HandsItsDeviceToTheLayerBelow()
    {
        var switches = new DriverSwitchboard();

        IDeviceDriver vendor = new FakeDriver(
            "audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e"));
        IDeviceDriver system = new FakeDriver(
            "system", DriverTier.System, Device("system:maxwell", "system", "usb:3329:4b1e"));

        using var registry = new DeviceRegistry(
            [vendor, system], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance, null, switches);

        await registry.DiscoverAsync(Ct);
        Assert.Equal(["audeze:maxwell"], Keys(registry));

        switches.Set("audeze.race", false);
        await registry.DiscoverAsync(Ct);

        // One device, not two. The vendor's session is dropped rather than left standing
        // as an offline row beside the system driver's copy of the same hardware — two
        // rows for one headset, one of them permanently dead, is the confusion this
        // screen exists to remove.
        Assert.Equal(["system:maxwell"], Keys(registry));
    }

    [Fact]
    public async Task SwitchingItBackOn_ReturnsTheDeeperDevice()
    {
        var switches = new DriverSwitchboard();
        switches.Set("audeze.race", false);

        IDeviceDriver vendor = new FakeDriver(
            "audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e"));
        IDeviceDriver system = new FakeDriver(
            "system", DriverTier.System, Device("system:maxwell", "system", "usb:3329:4b1e"));

        using var registry = new DeviceRegistry(
            [vendor, system], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance, null, switches);

        await registry.DiscoverAsync(Ct);
        Assert.Equal(["system:maxwell"], Keys(registry));

        switches.Set("audeze.race", true);
        await registry.DiscoverAsync(Ct);

        Assert.Equal(["audeze:maxwell"], Keys(registry));
    }

    [Fact]
    public async Task TheListIsAnnounced_SoTheScreenDoesNotHaveToBeAsked()
    {
        var switches = new DriverSwitchboard();

        IDeviceDriver vendor = new FakeDriver(
            "audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e"));

        using var registry = new DeviceRegistry(
            [vendor], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance, null, switches);

        await registry.DiscoverAsync(Ct);

        int changed = 0;
        registry.DevicesChanged += (_, _) => changed++;

        switches.Set("audeze.race", false);
        await registry.DiscoverAsync(Ct);

        Assert.Empty(Keys(registry));
        Assert.True(changed > 0);
    }

    /// <summary>
    /// A build with nothing switched off is the shipping one, and it must behave exactly
    /// as it did before there was a switchboard at all — including where nobody passes one.
    /// </summary>
    [Fact]
    public async Task WithNoSwitchboardAtAll_NothingChanges()
    {
        IDeviceDriver vendor = new FakeDriver(
            "audeze.race", DriverTier.Vendor, Device("audeze:maxwell", "audeze.race", "usb:3329:4b1e"));

        using var registry = new DeviceRegistry(
            [vendor], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        await registry.DiscoverAsync(Ct);

        Assert.Equal(["audeze:maxwell"], Keys(registry));
    }

    private static IReadOnlyList<string> Keys(DeviceRegistry registry) =>
        [.. registry.Devices.Select(d => d.Info.Key).Order(StringComparer.Ordinal)];

    private static DeviceInfo Device(string key, string driverId, params string[] hardwareIds) => new()
    {
        Key = key,
        DriverId = driverId,
        Vendor = "Test",
        Model = key,
        HardwareIds = new HashSet<string>(hardwareIds, StringComparer.Ordinal),
    };

    private sealed class FakeDriver(string id, DriverTier tier, params DeviceInfo[] devices) : IDeviceDriver
    {
        public string DriverId => id;

        public DriverTier Tier => tier;

        public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);

        public Task<IDeviceConnection> ConnectAsync(
            DeviceInfo device,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Nothing here opens anything.");
    }
}
