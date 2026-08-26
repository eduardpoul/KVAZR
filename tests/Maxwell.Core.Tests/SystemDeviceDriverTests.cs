using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.System;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The system driver is the only one that runs on every machine, against hardware nobody
/// here owns, so its rules are the ones most likely to be wrong somewhere. Every rule
/// below was derived from one real device tree and is pinned here so that a captured node
/// list can re-run it without the hardware being present.
///
/// The node shapes in these tests are not invented. They are what a Windows 11 machine
/// actually reported for an Audeze Maxwell, a pair of AirPods, a HyperX QuadCast S, a
/// Logitech receiver and a NuPhy dongle.
/// </summary>
public sealed class SystemDeviceDriverTests
{
    private static readonly Guid Hid = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private static readonly Guid Keyboard = new("4d36e96b-e325-11ce-bfc1-08002be10318");
    private static readonly Guid Mouse = new("4d36e96f-e325-11ce-bfc1-08002be10318");
    private static readonly Guid Media = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    private static readonly Guid UsbHub = new("36fc9e60-c465-11cf-8056-444553540000");

    private static SystemDeviceNode Node(
        Guid container,
        Guid deviceClass,
        string instanceId = @"USB\VID_0000&PID_0000\1",
        SystemDeviceLink link = SystemDeviceLink.Usb,
        string? name = null,
        string? busName = null,
        int? battery = null,
        uint? cod = null,
        string? address = null,
        bool root = false,
        bool? connected = null) =>
        new()
        {
            InstanceId = instanceId,
            ContainerId = container,
            IsConnected = connected,
            ClassGuid = deviceClass,
            Link = link,
            Name = name,
            BusReportedName = busName,
            BatteryPercent = battery,
            ClassOfDevice = cod,
            Address = address,
            IsDeviceRoot = root,
        };

    /// <summary>
    /// A low-energy device asleep is a pairing, not a device.
    ///
    /// Measured on the mouse, asleep and then woken: awake it has HID nodes beside its
    /// GATT services, asleep it has only the services — and Windows goes on answering its
    /// charge either way. A charge that answers is read as a device that answers, so the
    /// row sat there at a hundred per cent claiming to be live while the mouse slept.
    /// </summary>
    [Fact]
    public void ASleepingLowEnergyDevice_IsNotHere()
    {
        Guid container = Guid.NewGuid();

        Assert.False(SystemDeviceProfile.IsPeripheral(
        [
            Node(container, Hid, instanceId: @"BTHLEDEVICE\{00001812-0000}_DEV_X\1",
                link: SystemDeviceLink.BluetoothLe),
            Node(container, Mouse, instanceId: @"BTHLE\DEV_X\1", link: SystemDeviceLink.BluetoothLe),
        ]));
    }

    [Fact]
    public void TheSameDeviceAwake_IsHere()
    {
        Guid container = Guid.NewGuid();

        // The HID collection is what appears when it connects, and it is what the mouse
        // is driven through.
        Assert.True(SystemDeviceProfile.IsPeripheral(
        [
            Node(container, Hid, instanceId: @"BTHLEDEVICE\{00001812-0000}_DEV_X\1",
                link: SystemDeviceLink.BluetoothLe),
            Node(container, Mouse, instanceId: @"HID\{00001812-0000}_DEV_X&COL01\1",
                link: SystemDeviceLink.BluetoothLe),
        ]));
    }

    [Fact]
    public void AWiredDevice_IsNotJudgedByThatRule()
    {
        // The rule is about what a pairing leaves behind. Nothing on a cable leaves
        // anything behind, so a USB device is here whenever it is enumerated at all.
        Assert.True(SystemDeviceProfile.IsPeripheral([Node(Guid.NewGuid(), Mouse)]));
    }

    /// <summary>
    /// A Bluetooth Low Energy mouse, which is a mouse that says nothing about itself in
    /// the field classic Bluetooth uses — low energy has an Appearance instead and leaves
    /// this one at zero.
    ///
    /// It read as a mouse until its own device node was found, and then turned into
    /// Unknown: the field arrived, held nothing, and ended the search before the setup
    /// class it is actually identified by could be consulted. Silence is not an answer.
    /// </summary>
    [Fact]
    public void ALowEnergyMouse_IsStillAMouse()
    {
        Guid container = Guid.NewGuid();

        Assert.Equal(
            DeviceClass.Mouse,
            SystemDeviceProfile.Classify(
            [
                Node(container, Hid, link: SystemDeviceLink.BluetoothLe, cod: 0, address: "dca525f19711"),
                Node(container, Mouse, link: SystemDeviceLink.BluetoothLe),
            ]));
    }

    [Fact]
    public void AClassicHeadset_StillAnswersFromItsOwnField()
    {
        // The field is still the better source where it is filled in: it is a standard
        // every classic device answers, so a headset says headset without anybody having
        // heard of the brand.
        Assert.Equal(
            DeviceClass.Headset,
            SystemDeviceProfile.Classify([Node(Guid.NewGuid(), Hid, cod: 0x240404)]));
    }

    private static async Task<IReadOnlyList<DeviceInfo>> Discover(params SystemDeviceNode[] nodes) =>
        await new SystemDeviceDriver(new FakeSystemEnumerator(nodes))
            .DiscoverAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Paired is not connected, and the transport says which.
    ///
    /// Measured on a pair of AirPods, both ways. In the case: every Bluetooth node on
    /// the machine — eighteen of them — reports not connected. Taken out and playing:
    /// exactly one turns true, the node named after the device itself, while its Avrcp
    /// Transport, its Hands-Free AG and its vendor service all go on saying false. So
    /// the question is whether any node says yes.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ABluetoothLink_IsPresentOnlyWhileTheDeviceIsConnected(bool connected, bool present)
    {
        Guid container = Guid.NewGuid();

        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(container, Hid, name: "Buds", root: true, link: SystemDeviceLink.Bluetooth,
                instanceId: @"BTHENUM\DEV_6093160A369F\A&1", cod: 0x240404, address: "6093160a369f",
                connected: connected),

            // The service nodes, which report false whatever the device is doing.
            Node(container, Media, name: "Buds Avrcp Transport", link: SystemDeviceLink.Bluetooth,
                connected: false));

        TransportInfo bluetooth = devices[0].Transports.Single(t => t.Kind == TransportKind.Bluetooth);

        Assert.Equal(present, bluetooth.IsPresent);
    }

    /// <summary>
    /// And silence is not a denial. A bus we have not measured carries no such key at
    /// all, and reading that as "not connected" would take every device on it off the
    /// list.
    /// </summary>
    [Fact]
    public async Task ALinkWhoseNodesSayNothingAboutIt_IsPresent()
    {
        Guid container = Guid.NewGuid();

        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(container, Hid, name: "Buds", root: true, link: SystemDeviceLink.Bluetooth,
                instanceId: @"BTHENUM\DEV_6093160A369F\A&1", cod: 0x240404, address: "6093160a369f"));

        Assert.True(devices[0].Transports.Single(t => t.Kind == TransportKind.Bluetooth).IsPresent);
    }

    [Fact]
    public async Task NodesOfOneDevice_BecomeOneDevice()
    {
        Guid container = Guid.NewGuid();

        // A Bluetooth headset really does arrive as a dozen nodes. Before container ids
        // were used as the key, this listed as a dozen headsets.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(container, Hid, name: "Audeze Maxwell BT", root: true, link: SystemDeviceLink.Bluetooth,
                instanceId: @"BTHENUM\DEV_034275A08236\A&1", cod: 0x240404, address: "034275a08236"),
            Node(container, Media, name: "Audeze Maxwell BT Avrcp Transport", link: SystemDeviceLink.Bluetooth),
            Node(container, Media, name: "Audeze Maxwell BT Hands-Free AG", link: SystemDeviceLink.Bluetooth,
                battery: 98),
            Node(container, Hid, name: "BTNOTIFYR", link: SystemDeviceLink.Bluetooth));

        DeviceInfo device = Assert.Single(devices);
        Assert.Equal("Audeze Maxwell BT", device.DisplayName);
        Assert.Equal(DeviceClass.Headset, device.Class);
    }

    [Fact]
    public async Task BluetoothName_ComesFromTheDeviceRoot_NotTheProfileNodes()
    {
        Guid container = Guid.NewGuid();

        // Every node here carries a name and all but one of them is the name of a
        // Bluetooth profile. Picking the shortest, or the first, gets this wrong.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(container, Media, name: "AAP Server", link: SystemDeviceLink.Bluetooth),
            Node(container, Media, name: "Hands-Free AG", link: SystemDeviceLink.Bluetooth),
            Node(container, Hid, name: "Edik’s AirPods", root: true, link: SystemDeviceLink.Bluetooth,
                instanceId: @"BTHENUM\DEV_6093160A369F\A&1", cod: 0x240418));

        Assert.Equal("Edik’s AirPods", Assert.Single(devices).DisplayName);
    }

    [Fact]
    public async Task UsbName_ComesFromTheBus_BecauseTheFriendlyNameIsTheDrivers()
    {
        // Windows calls a microphone, a mouse and a keyboard all "USB Input Device".
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(Guid.NewGuid(), Hid, name: "USB Input Device", busName: "HyperX QuadCast S"));

        Assert.Equal("HyperX QuadCast S", Assert.Single(devices).DisplayName);
    }

    [Fact]
    public async Task NoMakerIsShown_BecauseTheManufacturerPropertyIsTheDriversAuthor()
    {
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(Guid.NewGuid(), Hid, busName: "HyperX QuadCast S") with
            {
                Manufacturer = "(Standard USB Host Controller)",
            });

        DeviceInfo device = Assert.Single(devices);
        Assert.Equal(string.Empty, device.Vendor);
        Assert.Equal("HyperX QuadCast S", device.DisplayName);
    }

    [Theory]
    [InlineData(0x240404u, DeviceClass.Headset)]
    [InlineData(0x240418u, DeviceClass.Earbuds)]
    [InlineData(0x240428u, DeviceClass.Speaker)]
    [InlineData(0x000540u, DeviceClass.Keyboard)]
    [InlineData(0x000580u, DeviceClass.Mouse)]
    [InlineData(0x000508u, DeviceClass.Gamepad)]
    public async Task BluetoothSaysWhatItIs_WithoutAnyoneKnowingTheModel(uint cod, DeviceClass expected)
    {
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(Guid.NewGuid(), Hid, link: SystemDeviceLink.Bluetooth, name: "thing", root: true, cod: cod));

        Assert.Equal(expected, Assert.Single(devices).Class);
    }

    [Fact]
    public async Task AReceiverServingBoth_IsAReceiver_NotAMouseAndNotAKeyboard()
    {
        Guid container = Guid.NewGuid();

        // A Logitech Lightspeed receiver is one container with a mouse node and a
        // keyboard node. Calling it either one names a device that is not there.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(container, Mouse, busName: "USB Receiver"),
            Node(container, Keyboard, busName: "USB Receiver"),
            Node(container, Hid, busName: "USB Receiver"));

        DeviceInfo device = Assert.Single(devices);
        Assert.Equal(DeviceClass.Receiver, device.Class);
        Assert.Equal(TransportKind.Dongle, Assert.Single(device.Transports).Kind);
    }

    [Fact]
    public async Task UsbAudio_StaysUnknown_RatherThanBeingGuessedFromTheName()
    {
        // Nothing at this tier separates a microphone from a headset, and "QuadCast" is
        // not evidence. Unknown is a class the interface draws properly; a wrong class
        // is one it draws confidently.
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(Guid.NewGuid(), Media, busName: "HyperX QuadCast S"));

        Assert.Equal(DeviceClass.Unknown, Assert.Single(devices).Class);
    }

    [Fact]
    public async Task Plumbing_IsNotAPeripheral()
    {
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(Guid.NewGuid(), UsbHub, busName: "Generic USB Hub"));

        Assert.Empty(devices);
    }

    [Fact]
    public async Task NothingIsWritable_AndNoTransportClaimsOtherwise()
    {
        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(Guid.NewGuid(), Hid, busName: "Some device"));

        Assert.All(Assert.Single(devices).Transports, t => Assert.False(t.CarriesControl));
    }

    [Fact]
    public async Task HardwareIds_CarryEveryIdentifierTheSystemKnows()
    {
        Guid container = Guid.NewGuid();

        IReadOnlyList<DeviceInfo> devices = await Discover(
            Node(container, Hid, busName: "Audeze Maxwell") with { VendorId = 0x3329, ProductId = 0x4B1E },
            Node(container, Media, link: SystemDeviceLink.Bluetooth, address: "034275A08236"));

        DeviceInfo device = Assert.Single(devices);

        Assert.Contains($"container:{container:D}", device.HardwareIds);
        Assert.Contains("usb:3329:4b1e", device.HardwareIds);
        Assert.Contains("bt:034275a08236", device.HardwareIds);
    }

    [Fact]
    public async Task ADeviceThatReportsNoBattery_OffersNoCapabilityAtAll()
    {
        // Turn 11c of the design: connected, nothing to configure, and a page all the
        // same. An empty capability list is the honest answer, not a broken connection.
        SystemDeviceNode node = Node(Guid.NewGuid(), Hid, busName: "Unidentified");
        var driver = new SystemDeviceDriver(new FakeSystemEnumerator([node]));

        IReadOnlyList<DeviceInfo> devices = await driver.DiscoverAsync(TestContext.Current.CancellationToken);
        using IDeviceConnection connection =
            await driver.ConnectAsync(devices[0], TestContext.Current.CancellationToken);

        Assert.Empty(connection.Capabilities);

        CapabilityValue value = await connection.ReadAsync(
            CapabilityIds.PowerBattery, TestContext.Current.CancellationToken);

        Assert.True(value.IsAbsent);
    }

    [Fact]
    public async Task ABatteryOnAnyNode_IsTheDevicesBattery()
    {
        Guid container = Guid.NewGuid();

        // On a headset the level hangs off the Hands-Free node, not the one named after
        // the device, so the whole container has to be searched rather than one node
        // remembered — the node that had it last time may not be present this time.
        SystemDeviceNode[] nodes =
        [
            Node(container, Hid, name: "Headset", root: true, link: SystemDeviceLink.Bluetooth,
                instanceId: @"BTHENUM\DEV_1\A&1", cod: 0x240404),
            Node(container, Media, name: "Headset Hands-Free AG", link: SystemDeviceLink.Bluetooth, battery: 98),
        ];

        var driver = new SystemDeviceDriver(new FakeSystemEnumerator(nodes));
        IReadOnlyList<DeviceInfo> devices = await driver.DiscoverAsync(TestContext.Current.CancellationToken);
        using IDeviceConnection connection =
            await driver.ConnectAsync(devices[0], TestContext.Current.CancellationToken);

        CapabilityValue value = await connection.ReadAsync(
            CapabilityIds.PowerBattery, TestContext.Current.CancellationToken);

        Assert.Equal(98, Assert.IsType<NumberValue>(value).Value);
    }

    private sealed class FakeSystemEnumerator(IReadOnlyList<SystemDeviceNode> nodes) : ISystemDeviceEnumerator
    {
        public IReadOnlyList<SystemDeviceNode> Enumerate() => nodes;
    }
}
