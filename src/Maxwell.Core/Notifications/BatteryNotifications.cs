using System.Collections.Immutable;

namespace Maxwell.Core.Notifications;

/// <summary>What the rules decided to tell the user about, if anything.</summary>
public enum BatteryAlert
{
    None,
    LowBattery,
    ChargeComplete,
}

/// <summary>A decision plus the threshold that triggered it.</summary>
public readonly record struct BatteryDecision(BatteryAlert Alert, int Threshold)
{
    public static readonly BatteryDecision Silent = new(BatteryAlert.None, 0);

    public bool ShouldNotify => Alert != BatteryAlert.None;
}

/// <summary>
/// Carried between evaluations. Immutable so the rules stay a pure function of
/// (previous state, new reading) — which is what makes them testable without a device.
/// </summary>
public sealed record BatteryWatchState
{
    public static readonly BatteryWatchState Initial = new();

    /// <summary>Thresholds already announced during the current discharge.</summary>
    public ImmutableHashSet<int> Announced { get; init; } = [];

    public int LastPercent { get; init; } = -1;

    public bool WasCharging { get; init; }

    public bool ChargeCompleteAnnounced { get; init; }
}

/// <summary>
/// Decides when to warn about battery level. Pure: no timers, no I/O, no clock.
///
/// The whole point is hysteresis. A naive "percent &lt;= threshold" test fires on every
/// poll while the reading hovers on the boundary, so the user gets the same warning
/// once a minute. A threshold is therefore announced once per discharge and only
/// re-armed when the battery climbs clear of it by a margin, or when charging starts.
/// </summary>
public static class BatteryNotifications
{
    public static readonly ImmutableArray<int> DefaultThresholds = [30, 15, 5];

    /// <summary>How far above a threshold the battery must climb before it re-arms.</summary>
    public const int RearmMargin = 3;

    /// <summary>Level at which a charge counts as complete.</summary>
    public const int ChargeCompletePercent = 100;

    public static (BatteryWatchState State, BatteryDecision Decision) Evaluate(
        BatteryWatchState previous,
        int percent,
        bool isCharging,
        IReadOnlyList<int>? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(previous);

        // An unreadable battery is not a low battery. Hold the previous state so a
        // dropout mid-discharge does not re-arm every threshold.
        if (percent < 0) return (previous, BatteryDecision.Silent);

        IReadOnlyList<int> levels = thresholds ?? DefaultThresholds;
        BatteryWatchState state = previous with { LastPercent = percent, WasCharging = isCharging };

        if (isCharging)
        {
            // Charging clears the discharge history: the next drop should warn again.
            state = state with { Announced = [] };

            if (percent >= ChargeCompletePercent && !previous.ChargeCompleteAnnounced)
            {
                return (state with { ChargeCompleteAnnounced = true },
                    new BatteryDecision(BatteryAlert.ChargeComplete, ChargeCompletePercent));
            }

            return (state, BatteryDecision.Silent);
        }

        // Unplugged: allow a future charge to announce completion again.
        state = state with { ChargeCompleteAnnounced = false };

        // Re-arm any threshold the battery has climbed clear of.
        ImmutableHashSet<int> announced = state.Announced;
        foreach (int threshold in levels)
        {
            if (announced.Contains(threshold) && percent > threshold + RearmMargin)
                announced = announced.Remove(threshold);
        }

        state = state with { Announced = announced };

        // Warn about the lowest threshold crossed that has not been announced yet, so
        // a sudden drop past several reports the most urgent one rather than a burst.
        int? crossed = null;
        foreach (int threshold in levels.Order())
        {
            if (percent > threshold || state.Announced.Contains(threshold)) continue;
            crossed = threshold;
            break;
        }

        if (crossed is not { } fired) return (state, BatteryDecision.Silent);

        // Mark every threshold at or above the fired one as announced: having warned
        // at 5% there is no sense in also warning at 15%.
        ImmutableHashSet<int> updated = state.Announced;
        foreach (int threshold in levels)
        {
            if (threshold >= fired) updated = updated.Add(threshold);
        }

        return (state with { Announced = updated }, new BatteryDecision(BatteryAlert.LowBattery, fired));
    }
}
