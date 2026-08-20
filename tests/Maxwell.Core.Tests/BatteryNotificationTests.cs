using Maxwell.Core.Notifications;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The rules are a pure function of (previous state, new reading), so every case here
/// runs without a device, a timer or a clock.
/// </summary>
public sealed class BatteryNotificationTests
{
    private static (BatteryWatchState State, BatteryDecision Decision) Step(
        BatteryWatchState state, int percent, bool charging = false) =>
        BatteryNotifications.Evaluate(state, percent, charging);

    [Fact]
    public void CrossingThreshold_Warns()
    {
        (BatteryWatchState state, _) = Step(BatteryWatchState.Initial, 40);

        (_, BatteryDecision decision) = Step(state, 29);

        Assert.Equal(BatteryAlert.LowBattery, decision.Alert);
        Assert.Equal(30, decision.Threshold);
    }

    /// <summary>
    /// The reason hysteresis exists: a reading that hovers on the boundary would
    /// otherwise re-announce itself on every single poll.
    /// </summary>
    [Fact]
    public void HoveringOnTheThreshold_WarnsOnlyOnce()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 40);

        var alerts = new List<BatteryAlert>();
        foreach (int reading in new[] { 29, 30, 29, 30, 28, 30, 29 })
        {
            (state, BatteryDecision decision) = Step(state, reading);
            if (decision.ShouldNotify) alerts.Add(decision.Alert);
        }

        Assert.Single(alerts);
    }

    [Fact]
    public void ClimbingClearOfTheThreshold_RearmsIt()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 40);
        (state, _) = Step(state, 29);

        // Back above 30 + margin, then down again.
        (state, _) = Step(state, 40);
        (_, BatteryDecision decision) = Step(state, 29);

        Assert.Equal(BatteryAlert.LowBattery, decision.Alert);
    }

    [Fact]
    public void RisingWithinTheMargin_DoesNotRearm()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 40);
        (state, _) = Step(state, 29);

        // 32 is inside the re-arm margin of 30 + 3.
        (state, _) = Step(state, 32);
        (_, BatteryDecision decision) = Step(state, 29);

        Assert.False(decision.ShouldNotify);
    }

    /// <summary>A sudden drop should report the most urgent level, not one alert per threshold.</summary>
    [Fact]
    public void FallingPastSeveralThresholds_WarnsOnceAtTheLowest()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 40);

        (state, BatteryDecision decision) = Step(state, 3);

        Assert.Equal(BatteryAlert.LowBattery, decision.Alert);
        Assert.Equal(5, decision.Threshold);

        // The higher thresholds are considered spoken for.
        (_, BatteryDecision next) = Step(state, 12);
        Assert.False(next.ShouldNotify);
    }

    [Fact]
    public void Charging_SuppressesLowBatteryWarnings()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 40);

        (_, BatteryDecision decision) = Step(state, 10, charging: true);

        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public void ChargeComplete_AnnouncedOnceWhileCharging()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 90, charging: true);

        (state, BatteryDecision first) = Step(state, 100, charging: true);
        (_, BatteryDecision second) = Step(state, 100, charging: true);

        Assert.Equal(BatteryAlert.ChargeComplete, first.Alert);
        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void ChargeComplete_CanAnnounceAgainAfterUnplugging()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 100, charging: true);
        (state, _) = Step(state, 95);

        (_, BatteryDecision decision) = Step(state, 100, charging: true);

        Assert.Equal(BatteryAlert.ChargeComplete, decision.Alert);
    }

    /// <summary>
    /// A dropout must not look like a full battery, or reconnecting would re-arm every
    /// threshold and warn all over again.
    /// </summary>
    [Fact]
    public void UnreadableBattery_LeavesStateUntouched()
    {
        BatteryWatchState state = BatteryWatchState.Initial;
        (state, _) = Step(state, 40);
        (state, _) = Step(state, 29);

        (BatteryWatchState afterDropout, BatteryDecision decision) = Step(state, -1);

        Assert.False(decision.ShouldNotify);
        Assert.Equal(state, afterDropout);
    }

    [Fact]
    public void HealthyBattery_SaysNothing()
    {
        (_, BatteryDecision decision) = Step(BatteryWatchState.Initial, 80);

        Assert.False(decision.ShouldNotify);
    }

    /// <summary>Starting the app already below a threshold should still warn.</summary>
    [Fact]
    public void FirstReadingBelowThreshold_Warns()
    {
        (_, BatteryDecision decision) = Step(BatteryWatchState.Initial, 12);

        Assert.Equal(BatteryAlert.LowBattery, decision.Alert);
        Assert.Equal(15, decision.Threshold);
    }
}
