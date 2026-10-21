using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Xunit;

namespace Kvazr.Race.Tests;

/// <summary>
/// Reading a pair of AirPods out of the air.
///
/// Every case here is a frame caught from one pair on a desk, in a state somebody could
/// see with their own eyes at the time. That matters more than usual: two published
/// write-ups of this beacon disagree about which half of byte 7 is the case and which is
/// the charging flags, and about which nibble is the left earphone. Where they disagreed,
/// the device decided, and these are its answers.
/// </summary>
public sealed class AppleProximityTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 26, 15, 0, 0, TimeSpan.Zero);

    private static ProximityReading Read(string hex) =>
        AppleProximity.Read(Convert.FromHexString(hex), -30, When)
        ?? throw new InvalidOperationException($"not read: {hex}");

    /// <summary>
    /// Both in the case with the lid open. Seen: two full earphones charging, a case at a
    /// tenth, and the case itself on no cable.
    /// </summary>
    [Fact]
    public void BothInTheCase_WithItsLidOpen()
    {
        ProximityReading heard = Read("071901132055AAB1110004");

        Assert.Equal(100, heard.Left);
        Assert.Equal(100, heard.Right);
        Assert.Equal(10, heard.Case);

        Assert.True(heard.LeftCharging);
        Assert.True(heard.RightCharging);
        Assert.False(heard.CaseCharging);
    }

    /// <summary>
    /// In ears, case shut. Nothing is charging and the case will not say how full it is —
    /// which is the reading that settles byte 7: taken the other way round this frame
    /// would be a case at eighty per cent with every charging flag set at once.
    /// </summary>
    [Fact]
    public void InEars_WithTheCaseShut()
    {
        ProximityReading heard = Read("071901132001998F000004");

        Assert.Equal(90, heard.Left);
        Assert.Equal(90, heard.Right);
        Assert.Null(heard.Case);

        Assert.False(heard.LeftCharging);
        Assert.False(heard.RightCharging);
        Assert.False(heard.CaseCharging);
    }

    /// <summary>
    /// The pair of frames that settled which earphone is which.
    ///
    /// The right one was out of the case and the left one charging in it. Two frames
    /// arrived seconds apart with opposite charging bits — and the bit that differs in the
    /// status byte is what makes them agree instead of contradicting each other. Both say
    /// the left is charging, which is what was true.
    /// </summary>
    [Fact]
    public void TheStatusBitSaysWhichEarphoneIsWhich()
    {
        ProximityReading one = Read("071901132071AA91310004");
        ProximityReading other = Read("071901132011AAA1010004");

        Assert.True(one.LeftCharging);
        Assert.False(one.RightCharging);

        Assert.True(other.LeftCharging);
        Assert.False(other.RightCharging);
    }

    /// <summary>
    /// The model bytes, backwards, are the product id Windows carries for the paired
    /// device. It is the only thing tying a beacon to a device: there is no serial in one,
    /// and its Bluetooth address is random and rotates.
    /// </summary>
    [Fact]
    public void TheModelIsTheProductIdBackwards()
    {
        Assert.Equal(0x2013, Read("071901132055AAB1110004").ProductId);
    }

    /// <summary>
    /// An earphone that is away says fifteen, which is not a level. Null rather than a
    /// number, because a missing earphone and a flat one look identical in an int.
    /// </summary>
    [Fact]
    public void AnEarphoneThatIsAway_HasNoLevelRatherThanAWrongOne()
    {
        ProximityReading heard = Read("071901132020F98F020004");

        Assert.Equal(90, heard.Left);
        Assert.Null(heard.Right);
        Assert.Null(heard.Case);
    }

    /// <summary>
    /// One number for a device with two batteries, and it is the emptier one: a pair only
    /// lasts as long as its worse half, and an average would report a comfortable seventy
    /// while one earphone is at forty and about to stop.
    /// </summary>
    [Fact]
    public void TheOneNumberForThePair_IsTheEmptierHalf()
    {
        Assert.Equal(40, Read("07190113200149B1110004").Lowest);
    }

    [Fact]
    public void SomethingThatIsNotThisMessage_IsNotRead()
    {
        // Apple broadcasts several kinds. 0x10 is the nearby-info one, and it carries no
        // battery at all.
        Assert.Null(AppleProximity.Read(Convert.FromHexString("100635 1D9434D218".Replace(" ", "")), -30, When));
        Assert.Null(AppleProximity.Read([], -30, When));
    }
}
