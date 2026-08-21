using Maxwell.App.Infrastructure;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// The three skeleton timing rules, which are requirements and not decoration:
/// nothing before 300 ms, nothing shorter than 500 ms once shown, and an error rather
/// than a placeholder after 10 s.
/// </summary>
public class LoadingGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double milliseconds) => Start.AddMilliseconds(milliseconds);

    [Fact]
    public void Nothing_is_drawn_before_the_delay()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);

        Assert.Equal(LoadingPhase.Waiting, gate.Advance(At(299)));
    }

    [Fact]
    public void The_skeleton_appears_at_three_hundred_milliseconds()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);

        Assert.Equal(LoadingPhase.Skeleton, gate.Advance(At(300)));
    }

    /// <summary>
    /// The common case: most polls answer inside 300 ms, and the point of the delay is
    /// that those never draw anything at all.
    /// </summary>
    [Fact]
    public void A_fast_answer_never_shows_a_skeleton()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);

        Assert.Equal(LoadingPhase.Content, gate.Resolve(At(120)));
        Assert.Equal(LoadingPhase.Content, gate.Advance(At(400)));
    }

    /// <summary>
    /// The flash, from the other end: a skeleton that appears and vanishes within a
    /// frame is worse than none, because the user sees movement and cannot read it.
    /// </summary>
    [Fact]
    public void A_shown_skeleton_stays_for_the_minimum()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);
        gate.Advance(At(300));

        Assert.Equal(LoadingPhase.Skeleton, gate.Resolve(At(310)));
        Assert.Equal(LoadingPhase.Skeleton, gate.Advance(At(700)));
        Assert.Equal(LoadingPhase.Content, gate.Advance(At(800)));
    }

    [Fact]
    public void A_skeleton_past_its_minimum_goes_straight_to_content()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);
        gate.Advance(At(300));

        Assert.Equal(LoadingPhase.Content, gate.Resolve(At(1200)));
    }

    /// <summary>
    /// A placeholder that pulses for ever is how a lost request gets mistaken for a
    /// slow one.
    /// </summary>
    [Fact]
    public void Ten_seconds_without_an_answer_becomes_an_error()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);
        gate.Advance(At(300));

        Assert.Equal(LoadingPhase.Skeleton, gate.Advance(At(9_999)));
        Assert.Equal(LoadingPhase.Failed, gate.Advance(At(10_000)));
    }

    /// <summary>Ten seconds is measured from the request, not from when the skeleton appeared.</summary>
    [Fact]
    public void The_deadline_runs_from_the_request()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);

        // Never ticked in between, so the gate is still Waiting when the deadline lands.
        Assert.Equal(LoadingPhase.Failed, gate.Advance(At(10_000)));
    }

    /// <summary>A late answer is still an answer; the error is not a dead end.</summary>
    [Fact]
    public void An_answer_after_the_deadline_replaces_the_error()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);
        gate.Advance(At(10_500));

        Assert.Equal(LoadingPhase.Content, gate.Resolve(At(12_000)));
    }

    /// <summary>
    /// The gate tells the caller when to wake up, so nothing has to poll it. Getting
    /// this wrong does not show as a wrong phase — it shows as a skeleton that arrives
    /// late or overstays, which is the failure the rules exist to prevent.
    /// </summary>
    [Fact]
    public void It_reports_when_it_next_has_something_to_do()
    {
        var gate = new LoadingGate();
        gate.Begin(Start);

        Assert.Equal(At(300), gate.NextDue(Start));

        gate.Advance(At(300));
        Assert.Equal(At(10_000), gate.NextDue(At(300)));

        gate.Resolve(At(400));
        Assert.Equal(At(800), gate.NextDue(At(400)));

        gate.Advance(At(800));
        Assert.Null(gate.NextDue(At(800)));
    }

    [Fact]
    public void An_idle_gate_has_nothing_pending()
    {
        var gate = new LoadingGate();

        Assert.Equal(LoadingPhase.Idle, gate.Phase);
        Assert.Null(gate.NextDue(Start));
    }

    /// <summary>The published numbers, so a well-meaning tweak has to argue with a test.</summary>
    [Fact]
    public void The_three_rules_are_the_published_ones()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(300), LoadingGate.AppearAfter);
        Assert.Equal(TimeSpan.FromMilliseconds(500), LoadingGate.MinimumVisible);
        Assert.Equal(TimeSpan.FromSeconds(10), LoadingGate.GiveUpAfter);
    }
}
