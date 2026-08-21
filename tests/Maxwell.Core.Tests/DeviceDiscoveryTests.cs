using Maxwell.Core.Abstractions;
using Maxwell.Core.Model;
using Maxwell.Core.Services;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// Discovery used to decide what the app could do from the shape of the connector:
/// a cable meant full control, a dongle meant a read-only shell. That was wrong. The
/// dongle drives the headset perfectly well — battery, settings, writes — and the
/// state that actually matters is whether anything answers.
///
/// The distinction is not academic. A dongle whose headset is switched off enumerates
/// exactly like one whose headset is awake, so nothing short of asking can tell them
/// apart, and the old code never asked.
/// </summary>
public sealed class DeviceDiscoveryTests
{
    private static MaxwellInterface Dongle(string path = @"\\?\dongle") =>
        new(path, 0x4B18, MaxwellInterface.RaceUsagePage, 0x01, 62, 62, "Audeze Maxwell");

    private static MaxwellInterface Cable(string path = @"\\?\cable") =>
        new(path, 0x4B1E, MaxwellInterface.RaceUsagePage, 0x01, 62, 62, "Audeze Maxwell");

    private static DeviceService Build(
        IHidEnumerator enumerator, IHidTransportFactory factory, IBluetoothMonitor? bluetooth = null) =>
        new(enumerator, factory, bluetooth ?? NullBluetoothMonitor.Instance,
            NullLogger<DeviceService>.Instance);

    [Fact]
    public async Task NothingPluggedIn_IsNotFound()
    {
        DeviceService service = Build(new FakeEnumerator(), new FakeFactory(answers: false));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.NotFound, service.State.Connection);
        Assert.False(service.State.DonglePresent);
        Assert.False(service.State.CanControl);
    }

    /// <summary>The regression this whole change exists for.</summary>
    [Fact]
    public async Task DongleAlone_WhenTheHeadsetAnswers_IsFullyControllable()
    {
        DeviceService service = Build(new FakeEnumerator(Dongle()), new FakeFactory(answers: true));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Active, service.State.Connection);
        Assert.True(service.State.CanControl);
        Assert.Equal(MaxwellTransport.XboxDongle, service.State.ControlTransport);
        Assert.True(service.State.DonglePresent);

        // The overview names each lead from these two, so a cable that is not there
        // must not be reported as one. It was, while control implied a cable.
        Assert.False(service.State.CablePresent);
    }

    [Fact]
    public async Task DongleAlone_WhenTheHeadsetIsSilent_IsUnreachable()
    {
        DeviceService service = Build(new FakeEnumerator(Dongle()), new FakeFactory(answers: false));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Unreachable, service.State.Connection);
        Assert.False(service.State.CanControl);
        Assert.True(service.State.DonglePresent);

        // Silence is not "no device": the dongle is still there and the user needs to
        // be told which of the two situations they are in.
        Assert.Null(service.State.ControlTransport);
    }

    [Fact]
    public async Task WithBothLeads_TheCableIsPreferred()
    {
        DeviceService service = Build(
            new FakeEnumerator(Dongle(), Cable()), new FakeFactory(answers: true));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Active, service.State.Connection);
        Assert.Equal(MaxwellTransport.XboxUsbC, service.State.ControlTransport);
        Assert.True(service.State.DonglePresent);
        Assert.True(service.State.CablePresent);
    }

    /// <summary>A device another process is holding — usually Audeze HQ.</summary>
    [Fact]
    public async Task WhenTheInterfaceCannotBeOpened_IsUnreachable()
    {
        DeviceService service = Build(new FakeEnumerator(Dongle()), new FakeFactory(throws: true));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Unreachable, service.State.Connection);
        Assert.False(service.State.CanControl);
    }

    /// <summary>
    /// An unreachable device has to be re-probed on the next sweep. Caching the open
    /// session by path alone would leave a headset that was switched off once marked
    /// dead until the app restarted.
    /// </summary>
    [Fact]
    public async Task AnUnreachableDevice_IsProbedAgain_AndRecovers()
    {
        var factory = new FakeFactory(answers: false);
        DeviceService service = Build(new FakeEnumerator(Dongle()), factory);

        await service.DiscoverAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Unreachable, service.State.Connection);

        factory.Answers = true;
        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Active, service.State.Connection);
    }

    /// <summary>
    /// Bluetooth carries audio and a battery level but advertises no control profile, so
    /// the headset is plainly connected and completely unconfigurable. Reporting that as
    /// "nothing found" to someone wearing them and hearing sound is the state this test
    /// exists to prevent.
    /// </summary>
    [Fact]
    public async Task BluetoothAlone_IsItsOwnState_WithABatteryButNoControl()
    {
        DeviceService service = Build(
            new FakeEnumerator(), new FakeFactory(answers: false), new FakeBluetooth(present: true, battery: 99));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.BluetoothOnly, service.State.Connection);
        Assert.True(service.State.BluetoothPresent);
        Assert.Equal(99, service.State.BatteryPercent);

        // Audible, readable, and still not controllable.
        Assert.False(service.State.CanControl);
        Assert.Null(service.State.ControlTransport);
    }

    [Fact]
    public async Task BluetoothWithNoBatteryReported_StillCountsAsConnected()
    {
        DeviceService service = Build(
            new FakeEnumerator(), new FakeFactory(answers: false), new FakeBluetooth(present: true, battery: -1));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.BluetoothOnly, service.State.Connection);
        Assert.False(service.State.HasBattery);
    }

    /// <summary>A dongle that answers must win: it is the one that can actually do anything.</summary>
    [Fact]
    public async Task WithBluetoothAndAWorkingDongle_TheDongleTakesControl()
    {
        DeviceService service = Build(
            new FakeEnumerator(Dongle()), new FakeFactory(answers: true), new FakeBluetooth(present: true, battery: 99));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.Active, service.State.Connection);
        Assert.True(service.State.CanControl);
        Assert.True(service.State.BluetoothPresent);
        Assert.Equal(MaxwellTransport.XboxDongle, service.State.ControlTransport);
    }

    [Fact]
    public async Task WithNoBluetoothAndNoInterfaces_IsStillNotFound()
    {
        DeviceService service = Build(
            new FakeEnumerator(), new FakeFactory(answers: false), new FakeBluetooth(present: false, battery: -1));

        await service.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionState.NotFound, service.State.Connection);
        Assert.False(service.State.BluetoothPresent);
    }

    // ---------- fakes ----------

    private sealed class FakeBluetooth(bool present, int battery) : IBluetoothMonitor
    {
        public BluetoothStatus Query() => new(present, battery);
    }

    private sealed class FakeEnumerator(params MaxwellInterface[] interfaces) : IHidEnumerator
    {
        public IReadOnlyList<MaxwellInterface> Enumerate() => interfaces;

        public IReadOnlyList<MaxwellInterface> EnumerateRaceInterfaces() => interfaces;
    }

    private sealed class FakeFactory(bool answers = true, bool throws = false) : IHidTransportFactory
    {
        public bool Answers { get; set; } = answers;

        public IHidTransport Open(MaxwellInterface iface) =>
            throws ? throw new IOException("held by another process") : new FakeTransport(this, iface);
    }

    /// <summary>
    /// Answers a battery read the way the hardware does, or stays silent the way a
    /// dongle with no headset behind it does — which is not silence on the wire but a
    /// buffer marked stale, and the difference is the whole point.
    /// </summary>
    private sealed class FakeTransport(FakeFactory owner, MaxwellInterface descriptor) : IHidTransport
    {
        private bool _asked;

        public MaxwellInterface Descriptor => descriptor;

        public bool Write(byte[] report)
        {
            _asked = true;
            return true;
        }

        public byte[]? ReadInputReport(byte reportId)
        {
            byte[] buffer = new byte[Descriptor.InputReportLength];
            buffer[0] = RaceCodec.InputReportId;

            if (!_asked || !owner.Answers)
            {
                // Length zero, stale flag set: exactly what the dongle returns when the
                // headset is not on the link.
                buffer[2] = RaceCodec.StaleFlag;
                return buffer;
            }

            // 05 5D <len:u16> <cmd:u16> <payload>, the level riding the last byte.
            byte[] packet = [0x05, RacePacket.Event, 0x05, 0x00, 0xD6, 0x0C, 0x00, 0x00, 100];
            packet.CopyTo(buffer, 3);
            buffer[1] = (byte)packet.Length;
            return buffer;
        }

        public void Dispose()
        {
        }
    }
}
