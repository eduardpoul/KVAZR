using Maxwell.Core.Model;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The preset table is the one place where a plausible-looking list is silently wrong.
/// Nothing in the app can tell that index 1 is Audeze rather than Bass Boost, because
/// the only thing that disagrees is the voice prompt in the wearer's ears — so this got
/// shipped wrong twice: once with the order printed in the Audeze user guide, and once
/// with the vendor application's order numbered from zero.
///
/// The two anchors below are the ones actually confirmed by listening to the headset.
/// </summary>
public sealed class EqPresetTests
{
    [Theory]
    [InlineData(1, "Audeze")]
    [InlineData(2, "Treble Boost")]
    [InlineData(3, "Bass Boost")]
    [InlineData(4, "Immersive")]
    [InlineData(5, "Competition")]
    [InlineData(6, "Footsteps")]
    public void FactoryPreset_MatchesFirmwareOrder(byte index, string expected) =>
        Assert.Equal(expected, EqPresets.NameOf(index));

    /// <summary>
    /// The two readings taken from the device itself. Everything else in the table is
    /// interpolation between them, so if either moves the table is wrong again.
    /// </summary>
    [Fact]
    public void ConfirmedByListeningToTheHeadset()
    {
        Assert.Equal("Audeze", EqPresets.NameOf(1));
        Assert.Equal("Competition", EqPresets.NameOf(5));
    }

    /// <summary>
    /// Slot 0 is accepted by the firmware but is not one of the ten, and no one has
    /// established what it is. It must stay nameless rather than acquire a guess.
    /// </summary>
    [Fact]
    public void SlotZero_IsNotOneOfTheTen()
    {
        Assert.Null(EqPresets.ByIndex(0));
        Assert.DoesNotContain(EqPresets.All, preset => preset.Index == 0);
    }

    [Fact]
    public void Slots_AreNumberedOneToTen()
    {
        Assert.Equal(10, EqPresets.All.Count);

        for (int i = 0; i < EqPresets.All.Count; i++)
            Assert.Equal(i + 1, EqPresets.All[i].Index);
    }

    [Fact]
    public void SixFactoryPresets_ComeBeforeFourUserSlots()
    {
        Assert.Equal(6, EqPresets.All.Count(p => !p.IsUserSlot));
        Assert.Equal(4, EqPresets.All.Count(p => p.IsUserSlot));
        Assert.All(EqPresets.All.Take(6), p => Assert.False(p.IsUserSlot));
        Assert.All(EqPresets.All.Skip(6), p => Assert.True(p.IsUserSlot));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(255)]
    public void IndexOutsideTheRange_HasNoName(int index)
    {
        Assert.Null(EqPresets.ByIndex(index));
        Assert.Equal("—", EqPresets.NameOf(index));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(6, 7)]
    [InlineData(9, 10)]
    public void Next_StepsThroughTheSlots(int current, byte expected) =>
        Assert.Equal(expected, EqPresets.Next(current));

    /// <summary>
    /// The wrap is the reason Next exists: modulo on the raw index would land on 0,
    /// which the ten slots do not include.
    /// </summary>
    [Fact]
    public void Next_WrapsPastTheLastSlotToTheFirst() =>
        Assert.Equal(1, EqPresets.Next(10));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(42)]
    public void Next_FromOutsideTheRange_EntersAtTheFirstSlot(int index) =>
        Assert.Equal(1, EqPresets.Next(index));
}
