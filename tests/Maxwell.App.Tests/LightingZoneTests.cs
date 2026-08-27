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

    /// <summary>
    /// A colour nobody picked lights nothing, which is the honest answer.
    ///
    /// Grey is the case worth writing down. It has no hue to compare — every swatch is a
    /// hue away from it, and the nearest would be whichever the arithmetic happened to
    /// reach first — so it matches none rather than one at random.
    ///
    /// Note what is deliberately not here: a dark blue. 0x123456 is the palette's blue in
    /// hue and saturation and only darker, and this row does not hold brightness, so
    /// lighting the blue swatch for it is right. That case failed this test when the test
    /// was written from RGB thinking, and the test was what was wrong.
    /// </summary>
    [Theory]
    [InlineData(0x808080)]
    [InlineData(0x1A1A1A)]
    public void AColourWithNoHue_LightsNone(int packed)
    {
        Harness.Bind();
        var vm = new LightingCapabilityViewModel(Colour(packed), new Recorder());
        Harness.Pump();

        Assert.Null(vm.Zones[0].Selected);
        Assert.Equal(packed, vm.Zones[0].Packed);
    }

    /// <summary>
    /// And the same colour darker is the same choice. The keyboard keeps hue and
    /// saturation and nothing else; brightness is the row above.
    /// </summary>
    [Fact]
    public void TheSameHueDarker_IsTheSameSwatch()
    {
        Harness.Bind();
        var vm = new LightingCapabilityViewModel(Colour(0x701E18), new Recorder());
        Harness.Pump();

        Assert.Equal(Red, vm.Zones[0].Selected?.Packed);
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

/// <summary>
/// A row that stops being part of the picture.
///
/// Sixteen of this keyboard's forty-one effects generate their own hue and never read
/// the one that was picked. Under those the colour is not unavailable — it is beside
/// the point, and a greyed control would only invite the reader to wonder what it would
/// do if it were not.
/// </summary>
public sealed class HiddenRowTests
{
    private static CapabilityState Effect(int selected) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.LightEffect,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Choice,
            Access = CapabilityAccess.ReadWrite,
            Title = "Effect",
            Choices =
            [
                new CapabilityChoice(0, "All Off"),
                new CapabilityChoice(1, "Solid Color"),
                new CapabilityChoice(11, "Cycle All"),
            ],
        },
        Value = CapabilityValue.Choice(selected),
    };

    private static CapabilityState Colour => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.LightColor,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Lighting,
            Access = CapabilityAccess.ReadWrite,
            Title = "Colour",
            Members = [new CapabilityMember(0, "Whole board")],
            HiddenBy = CapabilityIds.LightEffect,
            HiddenWhen = new HashSet<int> { 0, 11 },
        },
        Value = CapabilityValue.Vector([CapabilityValue.Of(0xE0483C)]),
    };

    [Theory]
    [InlineData(1, true)]    // Solid Color reads the picked hue
    [InlineData(11, false)]  // Cycle All drives it from a timer
    [InlineData(0, false)]   // and All Off lights nothing at all
    public void TheColour_IsShownOnlyUnderAnEffectThatReadsIt(int effect, bool shown)
    {
        Harness.Bind();

        var chooser = new ChoiceCapabilityViewModel(Effect(effect), null);
        var colour = new LightingCapabilityViewModel(Colour, null);
        colour.HideWith(chooser);
        Harness.Pump();

        Assert.Equal(shown, colour.IsShown);
    }

    /// <summary>And it follows the effect afterwards, not only when the page was built.</summary>
    [Fact]
    public void ChangingTheEffect_TakesTheColourAwayAndBringsItBack()
    {
        Harness.Bind();

        var chooser = new ChoiceCapabilityViewModel(Effect(1), null);
        var colour = new LightingCapabilityViewModel(Colour, null);
        colour.HideWith(chooser);
        Harness.Pump();

        Assert.True(colour.IsShown);

        chooser.Selected = chooser.Options.Single(o => o.Index == 11);
        Assert.False(colour.IsShown);

        chooser.Selected = chooser.Options.Single(o => o.Index == 1);
        Assert.True(colour.IsShown);
    }

    /// <summary>A row that names no chooser is always part of the picture.</summary>
    [Fact]
    public void ARowWithNoChooser_IsAlwaysShown()
    {
        Harness.Bind();

        CapabilityState plain = Colour with
        {
            Descriptor = Colour.Descriptor with { HiddenBy = null, HiddenWhen = new HashSet<int>() },
        };

        Assert.True(new LightingCapabilityViewModel(plain, null).IsShown);
    }
}
