using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.System;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The catalogue is the layer that grows fastest and is checked least, because most of
/// what goes into it describes hardware nobody here owns. These tests pin the rules it
/// has to keep whatever is in the file.
/// </summary>
public sealed class DeviceCatalogueTests
{
    private static readonly Guid Hid = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private static readonly Guid Media = new("4d36e96c-e325-11ce-bfc1-08002be10318");

    private static SystemDeviceNode Node(
        Guid container,
        string busName,
        ushort? vid = null,
        ushort? pid = null,
        Guid? deviceClass = null,
        string? btName = null) =>
        new()
        {
            InstanceId = $@"USB\{container:N}",
            ContainerId = container,
            ClassGuid = deviceClass ?? Hid,
            Link = btName is null ? SystemDeviceLink.Usb : SystemDeviceLink.Bluetooth,
            BusReportedName = btName is null ? busName : null,
            Name = btName,
            IsDeviceRoot = btName is not null,
            VendorId = vid,
            ProductId = pid,
        };

    private static async Task<IReadOnlyList<DeviceInfo>> Discover(
        IDeviceCatalogue catalogue,
        params SystemDeviceNode[] nodes) =>
        await new SystemDeviceDriver(new FakeEnumerator(nodes), catalogue)
            .DiscoverAsync(TestContext.Current.CancellationToken);

    [Fact]
    public void TheShippedCatalogue_Loads()
    {
        var catalogue = new DeviceCatalogue();

        Assert.NotEmpty(catalogue.Entries);
        Assert.All(catalogue.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Slug)));

        // A slug is also the picture's name, so two entries sharing one would silently
        // give two different devices the same photograph.
        Assert.Equal(
            catalogue.Entries.Count,
            catalogue.Entries.Select(e => e.Slug).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TwoProductIdsOfOneModel_BecomeOneDevice()
    {
        // The QuadCast S, which is the case the whole slug scheme exists for: Windows
        // sees two USB devices and is right to, because nothing it can read says
        // otherwise. Only the catalogue can say they are one microphone.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "HyperX QuadCast S", 0x0951, 0x171D, Media),
            Node(Guid.NewGuid(), "HyperX QuadCast S", 0x0951, 0x171F));

        DeviceInfo device = Assert.Single(devices);

        Assert.Equal("HyperX QuadCast S", device.DisplayName);
        Assert.Equal(DeviceClass.Microphone, device.Class);
        Assert.Contains("usb:0951:171d", device.HardwareIds);
        Assert.Contains("usb:0951:171f", device.HardwareIds);
    }

    [Fact]
    public async Task AnEmptyCatalogue_LeavesEveryDeviceInTheList()
    {
        // The claim the whole design rests on: everything the catalogue adds is an
        // improvement, never a precondition. Delete the file and the list still stands.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            EmptyCatalogue.Instance,
            Node(Guid.NewGuid(), "HyperX QuadCast S", 0x0951, 0x171D, Media),
            Node(Guid.NewGuid(), "HyperX QuadCast S", 0x0951, 0x171F));

        // Two, because without knowledge two product ids are two devices — and saying so
        // is more honest than guessing they are one.
        Assert.Equal(2, devices.Count);
        Assert.All(devices, d => Assert.Equal("HyperX QuadCast S", d.DisplayName));
    }

    [Fact]
    public async Task TheDeviceOutranksTheEntry_WhenTheyDisagree()
    {
        // An entry is a guess; the hardware is a fact. Believing the entry over a device
        // that has said what it is would put the wrong sections on its page.
        var catalogue = new StubCatalogue(new CatalogueEntry
        {
            Slug = "wrong",
            Vendor = "Test",
            Model = "Test Headset",
            Class = DeviceClass.Headset,
        });

        Guid container = Guid.NewGuid();

        IReadOnlyList<DeviceInfo> devices = await Discover(
            catalogue,
            Node(container, "thing", deviceClass: new Guid("4d36e96b-e325-11ce-bfc1-08002be10318")));

        Assert.Equal(DeviceClass.Keyboard, Assert.Single(devices).Class);
    }

    [Fact]
    public async Task AnEntryFillsAClassTheSystemCouldNotWorkOut()
    {
        // The other half of the same rule: where the device said nothing, the entry is
        // the only thing that can speak, and audio devices say nothing at this tier.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "HyperX QuadCast S", 0x0951, 0x171D, Media));

        Assert.Equal(DeviceClass.Microphone, Assert.Single(devices).Class);
    }

    [Fact]
    public async Task ABluetoothDeviceIsMatchedByName_BecauseItHasNoProductId()
    {
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "", btName: "Edik’s AirPods Pro - Find My"));

        DeviceInfo device = Assert.Single(devices);

        Assert.Equal(DeviceClass.Earbuds, device.Class);
        Assert.Equal("apple-airpods-pro-2", device.ImageKey);
    }

    [Fact]
    public async Task AVendorIdNamesTheMaker_WhereNoEntryCoversTheDevice()
    {
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "Some Gaming Thing", 0x046D, 0xFFFF));

        Assert.Equal("Logitech Some Gaming Thing", Assert.Single(devices).DisplayName);
    }

    [Fact]
    public async Task TheMakerIsNotRepeated_WhenTheNameAlreadyCarriesIt()
    {
        // "HyperX HyperX QuadCast S" is what happens without this, and it happened
        // (originally with the Flydigi dock, which has since folded into its gamepad).
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "HyperX QuadCast S", 0x0951, 0x171D));

        Assert.Equal("HyperX QuadCast S", Assert.Single(devices).DisplayName);
    }

    [Fact]
    public async Task TheDockFoldsIntoTheDeviceItCharges()
    {
        // The user's model, stated twice: docks and receivers belong inside the device
        // they serve. The dock's container matches the gamepad's entry, the row is the
        // gamepad, and the dock survives as a chip rather than as hardware of its own.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "Flydigi CD2", 0x37D7, 0x6001));

        DeviceInfo device = Assert.Single(devices);
        Assert.Equal("Flydigi APEX 5", device.DisplayName);
        Assert.Contains(device.Transports, t => t.Label == "Dock");
    }

    [Fact]
    public async Task AReceiverStaysAReceiver()
    {
        // One receiver serves whatever is paired to it. Naming it after the mouse behind
        // it would name a device the application cannot actually reach.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            new DeviceCatalogue(),
            Node(Guid.NewGuid(), "USB Receiver", 0x046D, 0xC54D));

        Assert.Equal(DeviceClass.Receiver, Assert.Single(devices).Class);
    }

    private sealed class FakeEnumerator(IReadOnlyList<SystemDeviceNode> nodes) : ISystemDeviceEnumerator
    {
        public IReadOnlyList<SystemDeviceNode> Enumerate() => nodes;
    }

    private sealed class StubCatalogue(CatalogueEntry entry) : IDeviceCatalogue
    {
        public CatalogueEntry? Match(IReadOnlySet<string> hardwareIds, string reportedName) => entry;

        public string? Vendor(IReadOnlySet<string> hardwareIds) => null;
    }
}
