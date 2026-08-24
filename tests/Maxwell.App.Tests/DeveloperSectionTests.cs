using Maxwell.App.Platform;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// The developer section: a switch per device library, and what it has to remember.
///
/// It exists because the registry always keeps the deepest driver that recognises a
/// device, so the tier underneath cannot be looked at on hardware that has a vendor
/// driver — and every piece of hardware worth building a vendor driver for is exactly the
/// hardware whose baseline page matters most.
/// </summary>
public sealed class DeveloperSectionTests
{
    [Fact]
    public void TheSectionIsShut_UntilSomebodyAsksForIt()
    {
        using Fixture fixture = Build();

        // What it opens is a way to switch off the driver that understands your headset.
        // Nobody should find it by scrolling.
        Assert.False(fixture.Vm.DeveloperMode);
    }

    [Fact]
    public void AskingForIt_IsRemembered()
    {
        using Fixture fixture = Build();

        fixture.Vm.DeveloperMode = true;

        Assert.True(fixture.Store.Current.DeveloperMode);
        Assert.True(fixture.Store.Saves > 0);
    }

    [Fact]
    public void EveryDriverIsListed_DeepestFirst()
    {
        using Fixture fixture = Build();

        // Deepest first because that is the order they shadow each other in: switching
        // off the top entry is what hands its devices to the one below.
        Assert.Equal(
            ["audeze.race", "logitech.hidpp", "system"],
            fixture.Vm.Libraries.Select(l => l.Id).Take(3));
    }

    [Fact]
    public void ContributorsAreListedToo_AndSayWhatTheyAre()
    {
        using Fixture fixture = Build();

        LibrarySwitchViewModel audio = fixture.Vm.Libraries.Single(l => l.Id == "audio");

        // A contributor owns no device: it adds rows to whatever another driver found,
        // and a row claiming it owned devices would be a different kind of thing.
        Assert.Null(audio.Tier);
        Assert.Equal(0, audio.Devices);
    }

    [Fact]
    public void SwitchingOneOff_ReachesTheRegistryAndTheFile()
    {
        using Fixture fixture = Build();

        LibrarySwitchViewModel audeze = fixture.Vm.Libraries.Single(l => l.Id == "audeze.race");
        audeze.ToggleCommand.Execute(null);

        // Both, and for different spans: the switchboard is what this run reads, the file
        // is what the next launch reads. One without the other is a switch that forgets
        // itself, or one that appears to have moved and has not.
        Assert.False(fixture.Switches.IsOn("audeze.race"));
        Assert.Contains("audeze.race", fixture.Store.Current.DisabledLibraries);
    }

    [Fact]
    public void SwitchingItBackOn_ClearsBothAgain()
    {
        using Fixture fixture = Build();

        LibrarySwitchViewModel audeze = fixture.Vm.Libraries.Single(l => l.Id == "audeze.race");
        audeze.ToggleCommand.Execute(null);
        audeze.ToggleCommand.Execute(null);

        Assert.True(fixture.Switches.IsOn("audeze.race"));
        Assert.DoesNotContain("audeze.race", fixture.Store.Current.DisabledLibraries);
    }

    [Fact]
    public void ASwitchAlreadyOffAtStartup_ComesUpOff()
    {
        var switches = new DriverSwitchboard();
        switches.Set("audeze.race", false);

        using Fixture fixture = Build(switches);

        Assert.False(fixture.Vm.Libraries.Single(l => l.Id == "audeze.race").IsOn);
        Assert.True(fixture.Vm.Libraries.Single(l => l.Id == "system").IsOn);
    }

    // ---------- fixture ----------

    private static Fixture Build(DriverSwitchboard? switches = null)
    {
        var store = new Harness.Store();
        var board = switches ?? new DriverSwitchboard();

        IDeviceDriver[] drivers =
        [
            new StubDriver("system", DriverTier.System),
            new StubDriver("audeze.race", DriverTier.Vendor),
            new StubDriver("logitech.hidpp", DriverTier.Class),
        ];

        var registry = new DeviceRegistry(
            drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance, null, board);

        var vm = new SettingsViewModel(
            store, new StubAutostart(), registry, new HiddenDevices(store),
            new Maxwell.App.Localization.Localizer(store),
            board, drivers, [new StubContributor("audio")]);

        return new Fixture(vm, store, board, registry);
    }

    private sealed record Fixture(
        SettingsViewModel Vm, Harness.Store Store, DriverSwitchboard Switches, DeviceRegistry Registry)
        : IDisposable
    {
        public void Dispose()
        {
            Vm.Dispose();
            Registry.Dispose();
        }
    }

    private sealed class StubDriver(string id, DriverTier tier) : IDeviceDriver
    {
        public string DriverId => id;

        public DriverTier Tier => tier;

        public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([]);

        public Task<IDeviceConnection> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default) =>
            throw new IOException("Nothing here opens anything.");
    }

    private sealed class StubContributor(string id) : ICapabilityContributor
    {
        public string ContributorId => id;

        public Task<IDeviceConnection?> AttachAsync(DeviceInfo device, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDeviceConnection?>(null);
    }

    private sealed class StubAutostart : IAutostart
    {
        public bool IsEnabled { get; private set; }

        public bool StartsMinimised { get; private set; }

        public void Enable(bool startMinimised)
        {
            IsEnabled = true;
            StartsMinimised = startMinimised;
        }

        public void Disable() => IsEnabled = false;
    }
}
