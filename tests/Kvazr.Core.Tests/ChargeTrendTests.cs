using Kvazr.Core.Drivers.Audeze;
using Xunit;

namespace Kvazr.Core.Tests;

/// <summary>
/// The bolt over the battery, which is a guess.
///
/// Nothing in the protocol says the headset is on a cable, so it is read off the trend in
/// the percentage — and a guess drawn as a fact is worth testing hard, because it is the
/// one thing on the panel nobody can check against the device. Every case here is a way
/// it has actually been wrong on this desk.
/// </summary>
public sealed class ChargeTrendTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingRead_SaysNothing()
    {
        // Not "not charging". A battery nobody has read yet and a battery not on a cable
        // are different states, and the panel draws them differently.
        Assert.False(new ChargeTrend().HasReading);
    }

    [Fact]
    public void OneReading_IsNotATrend()
    {
        var trend = new ChargeTrend();

        trend.Saw(94, Noon);

        Assert.True(trend.HasReading);
        Assert.False(trend.IsCharging(Noon));
    }

    [Fact]
    public void AClimbOfOne_IsNoise()
    {
        // Sitting on a dongle with no cable in it the headset reports 92, then 93, then
        // 93 again. One rising sample used to be enough, so the jitter lit the bolt.
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Saw(93, Noon);

        Assert.False(trend.IsCharging(Noon));
    }

    [Fact]
    public void AClimbOfTwo_IsNews()
    {
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Saw(94, Noon);

        Assert.True(trend.IsCharging(Noon));
    }

    [Fact]
    public void AFall_EndsItAtOnce()
    {
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Saw(94, Noon);
        trend.Saw(93, Noon);

        Assert.False(trend.IsCharging(Noon));
    }

    /// <summary>
    /// The claim used to be cleared only by a fall, and at 93% a fall does not come for
    /// hours. The bolt stayed lit long after the cable was out.
    /// </summary>
    [Fact]
    public void AClaimNothingConfirms_ExpiresOnItsOwn()
    {
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Saw(94, Noon);

        Assert.True(trend.IsCharging(Noon.AddMinutes(5)));
        Assert.False(trend.IsCharging(Noon.AddMinutes(7)));
    }

    /// <summary>
    /// And it expires against the clock rather than against the next reading.
    ///
    /// That distinction did not exist while a battery was read every thirty seconds no
    /// matter what. It does now: a headset that stops answering is not polled at all, so
    /// a claim examined only inside a reading froze exactly as it was — a bolt lit over a
    /// headset asleep in a drawer, and still lit when it woke.
    /// </summary>
    [Fact]
    public void AClaimExpires_ThoughNothingIsEverReadAgain()
    {
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Saw(94, Noon);

        Assert.False(trend.IsCharging(Noon.AddHours(3)));
    }

    /// <summary>
    /// The one that lit a bolt over a headset that had merely been switched off and on.
    ///
    /// While it sleeps nothing polls it, and the dongle answers the battery out of its own
    /// memory besides — so the readings either side of a nap have an unknown time between
    /// them and possibly a cable. Two samples with a hole between them are not a rise.
    /// </summary>
    [Fact]
    public void AReadingAcrossASleep_IsAFirstReadingRatherThanARise()
    {
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Lost();
        trend.Saw(94, Noon.AddMinutes(20));

        Assert.False(trend.IsCharging(Noon.AddMinutes(20)));
    }

    [Fact]
    public void ASleep_TakesTheClaimWithIt()
    {
        var trend = new ChargeTrend();

        trend.Saw(92, Noon);
        trend.Saw(94, Noon);
        Assert.True(trend.IsCharging(Noon));

        // A bolt over a device the page is at the same time calling unreachable is two
        // claims about one headset, and one of them is wrong.
        trend.Lost();

        Assert.False(trend.HasReading);
        Assert.False(trend.IsCharging(Noon));
    }

    [Fact]
    public void AConfirmedClimb_KeepsTheClaimAlive()
    {
        var trend = new ChargeTrend();

        trend.Saw(80, Noon);
        trend.Saw(82, Noon);
        trend.Saw(84, Noon.AddMinutes(5));

        // Still charging ten minutes in, because the second rise renewed it. Without
        // that a long charge would lose the bolt every six minutes.
        Assert.True(trend.IsCharging(Noon.AddMinutes(10)));
    }
}
