using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// The level meter is a readout that is drawn rather than read, and the arithmetic that
/// decides how much of it is lit is the whole of it.
///
/// The scale is decibels, and that is the point rather than a detail: on amplitude,
/// ordinary speech sits in the first tenth of the bar and a quiet room is
/// indistinguishable from a muted microphone. These cases pin the ends and the middle so
/// that a change of unit in the driver cannot quietly turn the meter into decoration.
/// </summary>
public sealed class LevelMeterTests
{
    private static CapabilityState Level(int decibels) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.MicSystemPeak,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Readout,
            Access = CapabilityAccess.Read,
            Title = "Input level",
            Range = new CapabilityRange { Min = -45, Max = 0, Unit = "dB" },
            IsLive = true,
        },
        // The session fills this in from the descriptor, and for a range whose ceiling
        // comes from the device, from the reading. A hand-built state has to say it.
        Range = new CapabilityRange { Min = -45, Max = 0, Unit = "dB" },
        Value = CapabilityValue.Of(decibels),
    };

    /// <summary>
    /// A flag reading shows the word the capability gave it, not "yes".
    ///
    /// The microphone row is the reason: it stopped being a switch, and "Microphone -
    /// yes" is not a sentence. The two words belong to the thing being described rather
    /// than to the type the answer happens to be stored in.
    /// </summary>
    [Theory]
    [InlineData(true, "On")]
    [InlineData(false, "Muted")]
    public void AFlagReadout_UsesTheNamesItWasGiven(bool on, string expected)
    {
        CapabilityState state = Level(0) with
        {
            Descriptor = Level(0).Descriptor with
            {
                IsLive = false,
                Choices = [new CapabilityChoice(0, "Muted"), new CapabilityChoice(1, "On")],
            },
            Value = CapabilityValue.Of(on),
        };

        Assert.Equal(expected, new ReadoutCapabilityViewModel(state, null).Display);
    }

    /// <summary>And falls back to yes and no where it was given none.</summary>
    [Fact]
    public void AFlagReadoutWithoutNames_StillReads()
    {
        CapabilityState state = Level(0) with
        {
            Descriptor = Level(0).Descriptor with { IsLive = false },
            Value = CapabilityValue.Of(true),
        };

        Assert.False(string.IsNullOrWhiteSpace(new ReadoutCapabilityViewModel(state, null).Display));
    }

    [Fact]
    public void ALiveReadout_IsDrawnAsAMeter()
    {
        var meter = new ReadoutCapabilityViewModel(Level(-30), null);

        Assert.True(meter.IsMeter);
    }

    [Fact]
    public void AnOrdinaryReadout_IsNot()
    {
        CapabilityState state = Level(-30) with
        {
            Descriptor = Level(-30).Descriptor with { IsLive = false },
        };

        Assert.False(new ReadoutCapabilityViewModel(state, null).IsMeter);
    }

    [Theory]
    [InlineData(0, 1.0)]           // full scale: the loudest thing the microphone can hear
    [InlineData(-15, 2.0 / 3)]     // ordinary speech, two thirds up
    [InlineData(-30, 1.0 / 3)]     // a third of the way up
    [InlineData(-45, 0.0)]         // the floor, which is where a quiet room sits
    public void TheBarIsThePositionOnTheDecibelScale(int decibels, double expected)
    {
        var meter = new ReadoutCapabilityViewModel(Level(decibels), null);

        Assert.Equal(expected, meter.Fill, 3);
    }

    [Fact]
    public void ALevelBelowTheFloor_LightsNothingRatherThanGoingNegative()
    {
        // The driver clamps, but a bar with a negative width is the kind of thing that
        // draws as a full one.
        var meter = new ReadoutCapabilityViewModel(Level(-90), null);

        Assert.Equal(0, meter.Fill);
    }

    [Fact]
    public void AReadingThatHasNotArrived_ReadsAsSilenceRatherThanVanishing()
    {
        CapabilityState state = Level(0) with { Value = CapabilityValue.Absent };

        // An unlit meter is the honest picture of a microphone hearing nothing; a meter
        // that disappeared between frames would be a flicker in the page.
        Assert.Equal(0, new ReadoutCapabilityViewModel(state, null).Fill);
    }

    [Fact]
    public void TheBarFollowsTheReading()
    {
        var meter = new ReadoutCapabilityViewModel(Level(-45), null);

        Assert.Equal(0, meter.Fill);

        meter.Apply(Level(-15));

        Assert.Equal(2 / 3d, meter.Fill, 3);
    }
}
