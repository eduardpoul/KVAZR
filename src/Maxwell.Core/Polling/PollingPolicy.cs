using Maxwell.Core.Devices;

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

    /// <summary>
    /// How often to ask a silent device whether it is back, while its page is open.
    ///
    /// Longer than the focused interval on purpose: a question that goes unanswered costs
    /// the protocol's whole retry window — four attempts across about a second and a half
    /// — where one that is answered costs a few tens of milliseconds. So the wait is set
    /// by what a failure costs, and the recovery it is watching for is the cheap case.
    /// </summary>
    public TimeSpan WakeInterval { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>Everything the policy needs to know about one device.</summary>
/// <param name="IsStirred">
/// Something changed on this machine a moment ago — a device announced itself to Windows,
/// which is what switching a headset on looks like from here even though its dongle never
/// left the socket. Measured: the announcement lands about a second before the headset can
/// answer anything, so it says when to start asking rather than what the answer is.
/// </param>
public readonly record struct PollSubject(
    string DeviceKey,
    bool IsPresent,
    bool CanControl,
    int ConsecutiveFailures,
    bool IsHidden = false,
    bool IsStirred = false);

/// <summary>
/// What to do with one device on this pass. <paramref name="Probe"/> is the liveness
/// question — asked apart from the reads because it is wanted in the two cases where
/// reading settles nothing: a device that has gone quiet has nothing worth reading, and a
/// device that is answering can stop between one pass and the next without any read
/// failing at all.
/// </summary>
public readonly record struct PollPlan(PollScope Scope, TimeSpan Interval, bool Probe = false)
{
    public static readonly PollPlan Skip = new(PollScope.None, TimeSpan.Zero);
}

/// <summary>
/// Decides what to read and how often. Pure, so the whole schedule can be tested
/// without a timer, a device or a clock — the same reason the battery rules are pure.
/// </summary>
public static class PollingPolicy
{
    /// <summary>
    /// Which capabilities the fast lane should read, which is almost always none.
    ///
    /// Three conditions, and each of them is there to keep a measurement from being taken
    /// behind the user's back. The page has to be open — not the tray, not the flyout,
    /// which show batteries and never a level. It has to be <em>this</em> device's page,
    /// because a microphone two rows down in a list nobody is reading must not be
    /// listened to. And the device has to be answering, or there is nothing to measure.
    /// </summary>
    public static IReadOnlyList<string> Live(
        DeviceSnapshot device,
        PollingAttention attention,
        string? focusedDeviceKey)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (attention != PollingAttention.Focused) return [];
        if (!string.Equals(device.Info.Key, focusedDeviceKey, StringComparison.Ordinal)) return [];
        if (!device.IsPresent || !device.CanControl) return [];

        var wanted = new List<string>();
        var already = new HashSet<string>(StringComparer.Ordinal);

        foreach (CapabilityState live in device.Capabilities.Values
                     .Where(c => c.Descriptor.IsLive && c.Descriptor.IsReadable))
        {
            if (already.Add(live.Id)) wanted.Add(live.Id);
        }

        // The switch a live row hangs from is read on the same lane as the row.
        //
        // Not an optimisation — a correctness rule. A microphone's level and the switch
        // above it are two readings of one thing, and reading one of them sixteen times a
        // second while the other waits for the ordinary sweep puts a moving meter above a
        // switch that says the microphone is off, for a second at a time.
        foreach (string gate in wanted
                     .Select(id => device.Capabilities[id].Descriptor.EnabledBy)
                     .OfType<string>()
                     .ToList())
        {
            if (device.Capabilities.TryGetValue(gate, out CapabilityState? state)
                && state.Descriptor.IsReadable
                && already.Add(gate))
            {
                wanted.Add(gate);
            }
        }

        return wanted;
    }

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

        bool focused = attention == PollingAttention.Focused
            && string.Equals(subject.DeviceKey, focusedDeviceKey, StringComparison.Ordinal);

        // A device that has gone quiet is still a device. A headset switched off on its
        // dongle enumerates exactly as it did a moment ago and simply stops replying, so
        // reading it is pointless — every answer would be empty — but asking whether it
        // is back is not, and nothing was asking. Discovery was, once every twenty
        // seconds, which is how long somebody who has just switched their headset on sat
        // looking at a page that said it was off.
        //
        // Something just happened to the hardware: ask hard for a moment, whoever is or
        // is not looking.
        //
        // This is the whole of what an event buys. Switching a headset on does announce
        // itself — Windows raises a burst of device notifications, because the dongle
        // presents interfaces that come and go with the radio link even though the dongle
        // itself never leaves the socket. What the burst cannot do is answer the question:
        // measured on this desk, it lands about a second before the headset will reply to
        // anything, so a single read triggered by it finds the device still asleep and the
        // wait falls back on the timer. Read as "start asking now" instead, it costs a
        // handful of readings and the wait disappears.
        if (!subject.CanControl)
        {
            if (subject.IsStirred) return new PollPlan(PollScope.None, options.FocusedInterval, Probe: true);

            // Otherwise only while somebody is looking at the application at all, and
            // quicker for the page that is actually open. A device in a list is a click
            // away from being looked at; a machine with nothing on screen has nobody to
            // tell, and waking a dongle every few seconds to tell nobody is worse than
            // the wait.
            //
            // And no backoff on this lane, deliberately: the one question whose job is to
            // notice a recovery must not get slower the longer the recovery takes.
            if (attention == PollingAttention.Idle) return PollPlan.Skip;

            return new PollPlan(
                PollScope.None,
                focused ? options.WakeInterval : options.WakeInterval * 2,
                Probe: true);
        }

        PollScope scope = focused ? PollScope.BatteryAndVolatile : PollScope.Battery;

        TimeSpan interval = focused
            ? options.FocusedInterval
            : attention == PollingAttention.Idle
                ? options.Interval * Math.Max(1, options.IdleMultiplier)
                : options.Interval;

        // The open page asks the same question in the other direction. A device can stop
        // answering without a single read throwing — the values simply come back empty,
        // which the sweep is right to ignore, since one empty row is not evidence of
        // anything. So the page went on offering controls for a headset in a drawer until
        // discovery came round. One extra reading a second, on the one device being
        // watched, buys the answer.
        //
        // And after an announcement, whoever is looking. Switching a headset off announces
        // itself exactly as loudly as switching it on, and until this asked, the burst's
        // only effect on a device still believed alive was to order a full read of it —
        // fifteen questions that each cost the protocol's whole retry window, taken from a
        // headset that had just stopped answering, in front of the one question worth
        // asking.
        return new PollPlan(
            scope,
            Backoff(interval, subject.ConsecutiveFailures, options),
            Probe: focused || subject.IsStirred);
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
