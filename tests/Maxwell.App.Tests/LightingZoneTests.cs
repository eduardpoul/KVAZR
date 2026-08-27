using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// The colour row, against the three faults a keyboard showed it up with.
///
/// A device holds a colour on its own scale — a VIA keyboard keeps hue and saturation as
/// single bytes — so what comes back is a step or two from what was sent. Everything
/// here follows from that: an exact match finds nothing, and a control that answers a
/// reading by writing turns the drift into a loop.
/// </summary>
public sealed class LightingZoneTests
{
    private sealed class Recorder : IDeviceSession
    {
        public List<CapabilityValue> Writes { get; } = [];

        public string DeviceKey => "test:keyboard";

        public DeviceSnapshot Snapshot { get; } = new()
        {
            Info = new DeviceInfo
            {
                Key = "test:keyboard",
                DriverId = "test",
                Vendor = "Test",
                Model = "Keyboard",
                Class = DeviceClass.Keyboard,
            },
            Availability = DeviceAvailability.Active,
        };

        public event EventHandler<DeviceSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(IReadOnlyList<string> ids, CancellationToken token = default) => Task.CompletedTask;

        public Task RefreshAllAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string id, CapabilityValue value, CancellationToken token = default)
        {
            Writes.Add(value);
            return Task.FromResult(true);
        }
    }

    /// <summary>Red, as the palette holds it.</summary>
    private const int Red = 0xE0483C;

    private static CapabilityState Colour(int packed) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.LightColor,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Lighting,
            Access = CapabilityAccess.ReadWrite,
            Title = "Colour",
            Members = [new CapabilityMember(0, "Whole board")],
        },
        Value = CapabilityValue.Vector([CapabilityValue.Of(packed)]),
    };

    [Fact]
    public void AColourThatCameBackExact_LightsItsSwatch()
    {
        Harness.Bind();
        var vm = new LightingCapabilityViewModel(Colour(Red), new Recorder());
        Harness.Pump();

        Assert.Equal(Red, vm.Zones[0].Selected?.Packed);
    }

    /// <summary>
    /// And one that came back a step off lights it too. This is the fault the reader
    /// reported: a swatch that lit when pressed and went dark when the answer arrived.
    /// </summary>
    [Theory]
    [InlineData(0xE0483C)]
    [InlineData(0xE1493D)]
    [InlineData(0xDE4639)]
    public void AColourRoundedByTheDevice_StillLightsItsSwatch(int packed)
    {
        Harness.Bind();
        var vm = new LightingCapabilityViewModel(Colour(packed), new Recorder());
        Harness.Pump();

        Assert.Equal(Red, vm.Zones[0].Selected?.Packed);
    }

    /// <summary>But a colour nobody picked lights nothing, which is the honest answer.</summary>
    [Fact]
    public void AColourFarFromEverySwatch_LightsNone()
    {
        Harness.Bind();
        var vm = new LightingCapabilityViewModel(Colour(0x123456), new Recorder());
        Harness.Pump();

        Assert.Null(vm.Zones[0].Selected);
        Assert.Equal(0x123456, vm.Zones[0].Packed);
    }

    /// <summary>
    /// A reading is not an instruction.
    ///
    /// Two guards deliver this and the contract is worth pinning whichever one does:
    /// the base class suppresses writes while a reading is being applied, and the zone
    /// now refuses at the site as well. The second was added on the way to the fault
    /// above rather than because this was seen failing — said plainly, because a test
    /// whose comment claims a fault it never saw teaches the next reader something
    /// untrue.
    /// </summary>
    [Fact]
    public void AReadingThatLightsASwatch_WritesNothing()
    {
        Harness.Bind();
        var session = new Recorder();
        var vm = new LightingCapabilityViewModel(Colour(0x123456), session);
        Harness.Pump();

        vm.Apply(Colour(Red));
        Harness.Pump();

        Assert.Equal(Red, vm.Zones[0].Selected?.Packed);
        Assert.Empty(session.Writes);
    }

    /// <summary>
    /// And a reading that arrives while our own write is still travelling is older than
    /// the hand that made it. Every other primitive had this rule; the composites did
    /// not, and on the lighting it showed as the previous colour painting itself back.
    /// </summary>
    [Fact]
    public void AReadingArrivingDuringAWrite_IsIgnored()
    {
        Harness.Bind();
        var session = new Recorder();
        var vm = new LightingCapabilityViewModel(Colour(0x123456), session);
        Harness.Pump();

        vm.Zones[0].Choose(Red);
        Assert.Single(session.Writes);

        // The poll behind the write, still carrying the colour from before it.
        vm.Apply(Colour(0x123456));
        Harness.Pump();

        Assert.Equal(Red, vm.Zones[0].Packed);
    }
}
