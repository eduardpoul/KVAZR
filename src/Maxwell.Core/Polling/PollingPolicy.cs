namespace Maxwell.Core.Polling;

/// <summary>What the interface currently needs from the device layer.</summary>
public enum PollingAttention
{
    /// <summary>No surface is open. Only the tray icon is looking, and it needs very little.</summary>
    Idle,

    /// <summary>The flyout is open: every device's battery matters, none of their details do.</summary>
    Glance,

    /// <summary>A device page is open. That one device is read in full; the rest stay background.</summary>
    Focused,
}

/// <summary>How much of a device to read on this pass.</summary>
public enum PollScope
{
    /// <summary>Nothing. Either there is no channel, or nobody is looking.</summary>
    None,

    Battery,

    /// <summary>
    /// Battery plus every capability the hardware can change on its own — the earcup
    /// wheel, a DPI button, a host-switch key. Without this the open page quietly
    /// disagrees with the device in the user's hands.
    /// </summary>
    BatteryAndVolatile,
}

public sealed class DevicePollingOptions
{
    public const string SectionName = "Polling";

    /// <summary>
    /// How often a watched device is read. Exposed in the settings screen, because the
    /// trade is real and personal: more often means a fresher percentage and a flatter
    /// battery in the device.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Multiplier applied when no window and no flyout is open. Six devices polled at
    /// the foreground rate all day is a real cost for a number nobody is reading.
    /// </summary>
    public int IdleMultiplier { get; set; } = 4;

    /// <summary>
    /// How often the one device whose page is open is read.
    ///
    /// Much shorter than the rest, and it has to be: the settings channel volunteers
    /// nothing, so anything the wearer changes on the device itself is invisible until the
    /// next read. At the ordinary interval a ChatMix wheel turned under someone's thumb
    /// took ten seconds to reach the panel they were looking at, which reads as the
    /// application being broken rather than patient.
    ///
    /// Affordable because it is one device, only while its page is open, and one reading
    /// costs a few tens of milliseconds. The volume wheel does not depend on this — it
    /// announces itself and is handled the moment it moves.
    /// </summary>
    public TimeSpan FocusedInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often to look for new hardware while none is attached.</summary>
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often to look for new hardware once something is attached.</summary>
    public TimeSpan IdleDiscoveryInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Ceiling on the consecutive-failure backoff, as a power of two.</summary>
    public int MaxBackoffShift { get; set; } = 4;
}

/// <summary>Everything the policy needs to know about one device.</summary>
public readonly record struct PollSubject(
    string DeviceKey,
    bool IsPresent,
    bool CanControl,
    int ConsecutiveFailures,
    bool IsHidden = false);

public readonly record struct PollPlan(PollScope Scope, TimeSpan Interval)
{
    public static readonly PollPlan Skip = new(PollScope.None, TimeSpan.Zero);
}

/// <summary>
/// Decides what to read and how often. Pure, so the whole schedule can be tested
/// without a timer, a device or a clock — the same reason the battery rules are pure.
/// </summary>
public static class PollingPolicy
{
    public static PollPlan Plan(
        PollSubject subject,
        PollingAttention attention,
        string? focusedDeviceKey,
        DevicePollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!subject.IsPresent) return PollPlan.Skip;

        // A device the user has put away costs its own battery to answer a question
        // nobody is going to read. Hiding it from the screens while still waking it
        // every thirty seconds would be the worst of both.
        if (subject.IsHidden) return PollPlan.Skip;

        // A device that is not answering is re-probed by discovery, which asks it one
        // cheap question on every sweep. Polling it as well would climb the failure
        // backoff forever while adding nothing.
        if (!subject.CanControl) return PollPlan.Skip;

        bool focused = attention == PollingAttention.Focused
            && string.Equals(subject.DeviceKey, focusedDeviceKey, StringComparison.Ordinal);

        PollScope scope = focused ? PollScope.BatteryAndVolatile : PollScope.Battery;

        TimeSpan interval = focused
            ? options.FocusedInterval
            : attention == PollingAttention.Idle
                ? options.Interval * Math.Max(1, options.IdleMultiplier)
                : options.Interval;

        return new PollPlan(scope, Backoff(interval, subject.ConsecutiveFailures, options));
    }

    /// <summary>
    /// Doubles the wait per consecutive failure up to a ceiling. A device that has just
    /// been unplugged should not be retried thirty times a minute, and one that comes
    /// back recovers immediately because the count resets on the first success.
    /// </summary>
    public static TimeSpan Backoff(TimeSpan interval, int failures, DevicePollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (failures <= 0) return interval;

        int shift = Math.Min(failures, Math.Max(0, options.MaxBackoffShift));
        return interval * (1 << shift);
    }

    /// <summary>
    /// How long to wait before looking for hardware again. Fast while the list is empty,
    /// because that is when the user is plugging something in and watching for it.
    /// </summary>
    public static TimeSpan DiscoveryDelay(bool anyDevicePresent, DevicePollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return anyDevicePresent ? options.IdleDiscoveryInterval : options.DiscoveryInterval;
    }
}
