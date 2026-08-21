using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The value hierarchy exists to keep one promise: a reading that was never taken can
/// never be mistaken for a zero. Every case here is that promise under a different
/// shape, plus the equality the session depends on to tell a change from a repeat.
/// </summary>
public sealed class CapabilityValueTests
{
    [Fact]
    public void AbsenceIsNotZero()
    {
        // The whole reason this is a closed hierarchy rather than a nullable int. A boom
        // microphone that declines its level read must not present as silence.
        Assert.True(CapabilityValue.Absent.IsAbsent);
        Assert.False(CapabilityValue.Of(0).IsAbsent);
        Assert.NotEqual(CapabilityValue.Of(0), CapabilityValue.Absent);
    }

    [Fact]
    public void AVectorKeepsAbsenceOfItsMembers()
    {
        CapabilityValue curve = CapabilityValue.Vector(
            [CapabilityValue.Of(3), CapabilityValue.Absent, CapabilityValue.Of(-2)]);

        // The band nobody asked about stays unanswered instead of collapsing to 0 dB,
        // which is a real setting and would draw as a confident flat band.
        Assert.False(curve.IsAbsent);
        Assert.Equal(3, curve.Member(0).AsNumber());
        Assert.True(curve.Member(1).IsAbsent);
        Assert.Equal(-2, curve.Member(2).AsNumber());
    }

    [Fact]
    public void AMemberPastTheEndIsAbsentRatherThanAnError()
    {
        // A driver may answer with fewer bands than the descriptor declares. A control
        // asking for the tenth has to get "not read", not an exception and not a zero.
        CapabilityValue pair = CapabilityValue.Numbers(1600, 1200);

        Assert.True(pair.Member(2).IsAbsent);
        Assert.True(pair.Member(-1).IsAbsent);
        Assert.True(CapabilityValue.Absent.Member(0).IsAbsent);
    }

    [Fact]
    public void NestingSurvives_SoAnUnreadLayerIsAbsentAsAWhole()
    {
        CapabilityValue layout = CapabilityValue.Vector(
        [
            CapabilityValue.Vector([CapabilityValue.Of("Esc"), CapabilityValue.Of("Q")]),
            CapabilityValue.Absent,
        ]);

        Assert.Equal("Esc", layout.Member(0).Member(0).AsText());
        Assert.True(layout.Member(1).IsAbsent);

        // And an unread layer is not a layer of blanks: asking inside it still says
        // "not read" rather than handing back an empty legend.
        Assert.True(layout.Member(1).Member(0).IsAbsent);
    }

    /// <summary>
    /// The session decides whether a reading moved by comparing it with the previous
    /// one. A record's generated equality compares its list by reference, so without
    /// structural equality every poll would look like a change and the "changed on the
    /// device" marker would light on every curve every few seconds until it meant
    /// nothing at all.
    /// </summary>
    [Fact]
    public void EqualVectorsAreEqual_EvenAsDifferentInstances()
    {
        CapabilityValue first = CapabilityValue.Numbers(1600, 1200);
        CapabilityValue second = CapabilityValue.Numbers(1600, 1200);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first != second);
    }

    [Fact]
    public void VectorsThatDifferAnywhereAreUnequal()
    {
        CapabilityValue baseline = CapabilityValue.Numbers(1600, 1200);

        Assert.NotEqual(baseline, CapabilityValue.Numbers(1600, 1201));
        Assert.NotEqual(baseline, CapabilityValue.Numbers(1600));
        Assert.NotEqual(baseline, CapabilityValue.Numbers(1600, 1200, 800));

        // Including one level down, or a nested change would poll as no change at all.
        Assert.NotEqual(
            CapabilityValue.Vector([CapabilityValue.Numbers(1, 2)]),
            CapabilityValue.Vector([CapabilityValue.Numbers(1, 3)]));
    }

    [Fact]
    public void AVectorOfAbsencesIsStillNotAnEmptyVector()
    {
        // "We asked and got nothing back for either channel" and "this capability has no
        // channels" are different facts, and the shapes have to keep them apart.
        Assert.NotEqual(
            CapabilityValue.Vector([CapabilityValue.Absent, CapabilityValue.Absent]),
            CapabilityValue.Vector([]));
    }

    [Fact]
    public void ReadingAVectorAsAScalarFallsBackRatherThanThrowing()
    {
        CapabilityValue pair = CapabilityValue.Numbers(1600, 1200);

        Assert.Equal(-1, pair.AsNumber(-1));
        Assert.Null(pair.AsText());
        Assert.Empty(CapabilityValue.Of(5).AsVector());
    }
}
