using Kvazr.Core.Notifications;

namespace Kvazr.Core.Polling;

/// <summary>
/// Runs the battery rules once per device.
///
/// The state has to be per device and it is easy to get wrong: with one shared watch,
/// a mouse crossing 15% would mark that threshold announced and silently swallow the
/// headset's warning half an hour later. Separated from the scheduler so exactly that
/// can be tested without a timer.
/// </summary>
public sealed class BatteryWatchdog
{
    private readonly Dictionary<string, BatteryWatchState> _states = new(StringComparer.Ordinal);

    public BatteryDecision Evaluate(
        string deviceKey,
        int percent,
        bool isCharging,
        IReadOnlyList<int>? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        BatteryWatchState previous = _states.GetValueOrDefault(deviceKey, BatteryWatchState.Initial);

        (BatteryWatchState next, BatteryDecision decision) =
            BatteryNotifications.Evaluate(previous, percent, isCharging, thresholds);

        _states[deviceKey] = next;
        return decision;
    }

    /// <summary>
    /// Drops a device's history. Used when a device is forgotten entirely, not when it
    /// merely disconnects — a headset that comes back from a drawer at the same level
    /// should not warn all over again.
    /// </summary>
    public void Forget(string deviceKey) => _states.Remove(deviceKey);

    /// <summary>
    /// The thresholds to apply: the user's choice plus the standard lower ones, so a
    /// sudden drop past several still warns, and warns about the most urgent.
    /// </summary>
    public static IReadOnlyList<int> ThresholdsFor(int userThreshold) =>
    [
        .. BatteryNotifications.DefaultThresholds
            .Append(userThreshold)
            .Distinct()
            .OrderDescending(),
    ];
}
