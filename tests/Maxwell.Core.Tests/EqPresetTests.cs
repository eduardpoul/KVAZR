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

/// <summary>
/// The factory curves, and the export format, checked against the one piece of ground
/// truth we have: a preset exported out of Audeze HQ on this hardware.
/// </summary>
public sealed class EqPresetCurveTests
{
    /// <summary>
    /// Contents of a preset exported from Audeze HQ, 2026-08-23. Ten bytes, no header.
    /// The two negative bands are the reason this test exists: read unsigned, 0xF4 is
    /// +244 dB rather than −12, and an import doing that still looks like it worked.
    /// </summary>
    private static readonly byte[] Exported = [0x00, 0x00, 0x0C, 0xF4, 0x00, 0x00, 0xF4, 0x00, 0x00, 0x0C];

    [Fact]
    public void Parse_ReadsGainsAsSigned()
    {
        IReadOnlyList<int>? curve = EqPresetFile.Parse(Exported);

        Assert.NotNull(curve);
        Assert.Equal([0, 0, 12, -12, 0, 0, -12, 0, 0, 12], curve);
    }

    [Fact]
    public void Write_RoundTripsTheExportedFile() =>
        Assert.Equal(Exported, EqPresetFile.Write(EqPresetFile.Parse(Exported)!));

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(11)]
    public void Parse_RejectsWrongLength(int length) => Assert.Null(EqPresetFile.Parse(new byte[length]));

    [Fact]
    public void Bands_AreTenFixedCentres() =>
        Assert.Equal([32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000], EqPresets.Bands);

    /// <summary>Every factory preset carries a curve, and it lines up with the bands.</summary>
    [Fact]
    public void FactoryPresets_AllCarryAFullCurve()
    {
        foreach (EqPreset preset in EqPresets.All.Where(p => !p.IsUserSlot))
        {
            Assert.NotNull(preset.Curve);
            Assert.Equal(EqPresets.Bands.Count, preset.Curve.Count);
        }
    }

    /// <summary>The user slots are stored on the headset, so this table must not guess.</summary>
    [Fact]
    public void UserSlots_CarryNoCurve() =>
        Assert.All(EqPresets.All.Where(p => p.IsUserSlot), p => Assert.Null(p.Curve));

    /// <summary>
    /// The curves corroborate the slot order on their own: the boost presets lift the
    /// half of the spectrum their names claim. This is the check that would have caught
    /// the user-guide ordering, which puts Treble Boost somewhere else entirely.
    /// </summary>
    [Fact]
    public void BoostPresets_LiftTheHalfTheyAreNamedFor()
    {
        static int Bass(EqPreset p) => p.Curve![0] + p.Curve[1];
        static int Treble(EqPreset p) => p.Curve![7] + p.Curve[8] + p.Curve[9];

        EqPreset treble = EqPresets.ByIndex(2)!;
        EqPreset bass = EqPresets.ByIndex(3)!;
        EqPreset immersive = EqPresets.ByIndex(4)!;

        Assert.Equal("Treble Boost", treble.Name);
        Assert.True(Treble(treble) > 0 && Bass(treble) == 0);

        Assert.Equal("Bass Boost", bass.Name);
        Assert.True(Bass(bass) > 0 && Treble(bass) == 0);

        Assert.Equal("Immersive", immersive.Name);
        Assert.True(Bass(immersive) > 0 && Treble(immersive) > 0);
    }

    /// <summary>"Audeze" is the native tuning: flat, not absent.</summary>
    [Fact]
    public void AudezePreset_IsFlat() => Assert.All(EqPresets.ByIndex(1)!.Curve!, gain => Assert.Equal(0, gain));
}
