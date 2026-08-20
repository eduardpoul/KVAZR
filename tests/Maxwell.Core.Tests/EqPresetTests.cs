using Maxwell.Core.Model;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The preset table is the one place where a plausible-looking list is silently wrong:
/// nothing in the app can tell that index 1 is Treble Boost rather than Bass Boost,
/// because the only thing that disagrees is the voice prompt in the user's ears. The
/// order shipped for a while taken from the Audeze user guide, which lists Treble Boost
/// last, and every selection past index 0 announced the wrong preset.
///
/// These tests lock the firmware order so it cannot drift back to the printed one.
/// </summary>
public sealed class EqPresetTests
{
    /// <summary>
    /// The vendor application's own sequence, read out of the literal block that
    /// follows its getCurrentEqName routine.
    /// </summary>
    [Theory]
    [InlineData(0, "Audeze")]
    [InlineData(1, "Treble Boost")]
    [InlineData(2, "Bass Boost")]
    [InlineData(3, "Immersive")]
    [InlineData(4, "Competition")]
    [InlineData(5, "Footsteps")]
    public void FactoryPreset_MatchesFirmwareOrder(byte index, string expected) =>
        Assert.Equal(expected, EqPresets.NameOf(index));

    [Fact]
    public void TrebleBoost_IsNotLast()
    {
        // Guarding the specific mistake rather than the general shape: the guide order
        // puts Treble Boost at 5, and that is the value that has to stay wrong.
        Assert.NotEqual("Treble Boost", EqPresets.NameOf(5));
        Assert.Equal("Treble Boost", EqPresets.NameOf(1));
    }

    [Fact]
    public void Slots_AreTenContiguousIndices()
    {
        Assert.Equal(10, EqPresets.All.Count);

        for (byte i = 0; i < EqPresets.All.Count; i++)
            Assert.Equal(i, EqPresets.All[i].Index);
    }

    [Fact]
    public void SixFactoryPresets_ComeBeforeFourUserSlots()
    {
        Assert.Equal(6, EqPresets.All.Count(p => !p.IsUserSlot));
        Assert.Equal(4, EqPresets.All.Count(p => p.IsUserSlot));
        Assert.All(EqPresets.All.Take(6), p => Assert.False(p.IsUserSlot));
        Assert.All(EqPresets.All.Skip(6), p => Assert.True(p.IsUserSlot));
    }

    /// <summary>An index the headset has no slot for must not produce a confident name.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(255)]
    public void IndexOutsideTheRange_HasNoName(int index)
    {
        Assert.Null(EqPresets.ByIndex(index));
        Assert.Equal("—", EqPresets.NameOf(index));
    }
}
