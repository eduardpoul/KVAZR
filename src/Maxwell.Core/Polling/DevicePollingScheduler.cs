using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Maxwell.Core.Logging;
using Maxwell.Core.Notifications;
using Maxwell.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxwell.Core.Polling;

/// <summary>
/// The timers in the application, both of them. Reads each device as often as the policy
/// says it is worth reading, raises the battery alerts, and keeps the fast lane going for
/// the readings that are meaningless slow.
///
/// One loop rather than a timer per device on purpose: with six devices, six
/// independent timers wake the process six times as often for no extra information,
/// and their reads interleave into the serialised sessions in an order nobody chose.
///
/// The second loop is the exception that proves it. A level meter at the ordinary pace is
/// not a slower meter, it is a broken one — and it cannot share the sweep, whose floor is
/// a quarter of a second and whose whole job is to be unhurried. So live readings get a
/// lane of their own: one device, the one whose page is open, and only its live rows.
/// </summary>
public sealed class DevicePollingScheduler : BackgroundService, IPollingCoordinator
{
    private static readonly string[] BatteryOnly =
        [CapabilityIds.PowerBattery, CapabilityIds.PowerCharging];

    /// <summary>Floor on the sleep, so a misconfigured interval cannot spin the loop.</summary>
    private static readonly TimeSpan MinSleep = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long one device may hold the sweep. Generous — a full read of a headset over a
    /// dongle takes seconds — but finite, because the pass that notices a device was
    /// unplugged comes after this one.
    /// </summary>
    private static readonly TimeSpan DeviceBudget = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often a live reading is taken while its page is open. Sixteen a second: fast
    /// enough that a level meter follows a voice, slow enough that the cost is a COM call
    /// and a snapshot rather than a frame budget.
    /// </summary>
    private static readonly TimeSpan LiveInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>How often the fast lane looks up to see whether anything live is on screen.</summary>
    private static readonly TimeSpan LiveIdle = TimeSpan.FromMilliseconds(400);

    private readonly IDeviceRegistry _registry;
    private readonly INotifier _notifier;
    private readonly ISettingsStore _settings;
    private readonly IOptionsMonitor<DevicePollingOptions> _options;
    private readonly ILogger<DevicePollingScheduler> _logger;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly BatteryWatchdog _watch = new();
    private readonly HashSet<string> _fullRefreshRequests = new(StringComparer.Ordinal);

    /// <summary>
    /// Devices that have been read once in full since they appeared. The ordinary policy
    /// applies to everything after that — battery in the tray, the rest only where
    /// something is actually looking.
    /// </summary>
    private readonly HashSet<string> _introduced = new(StringComparer.Ordinal);

    private readonly Lock _sync = new();

    private CancellationTokenSource _wake = new();
    private DateTimeOffset _nextDiscovery = DateTimeOffset.MinValue;

    public DevicePollingScheduler(
        IDeviceRegistry registry,
        INotifier notifier,
        ISettingsStore settings,
        IOptionsMonitor<DevicePollingOptions> options,
        ILogger<DevicePollingScheduler> logger,
        IDeviceChangeWatcher? changes = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _registry = registry;
        _notifier = notifier;
        _settings = settings;
        _options = options;
        _logger = logger;
        _changes = changes;

        // Optional so the harnesses and the tests can build a scheduler without a
        // platform under them; where it is absent the timed sweep is the whole story.
        if (_changes is not null) _changes.Changed += OnDevicesChanged;
    }

    private readonly IDeviceChangeWatcher? _changes;

    /// <summary>
    /// Something was plugged in or unplugged. Discovery is made due immediately and every
    /// device is queued for a full read: whatever changed, the values on screen were
    /// taken before it happened.
    ///
    /// The event arrives on whatever thread the platform raises it on and may arrive in
    /// bursts — one physical device announces several interfaces — so this only sets
    /// flags and wakes the loop, which is cheap enough to do a dozen times in a row.
    /// </summary>
    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            // Flags first, and for every notification without exception. They are cheap
            // and setting them twice changes nothing, so there is no reason to be clever
            // about which notification in a burst gets to set them.
            foreach (DeviceSnapshot device in _registry.Devices)
            {
                _fullRefreshRequests.Add(device.Info.Key);
                EntryFor(device.Info.Key).NextDue = DateTimeOffset.MinValue;
            }

            // The sweep itself is taken once, a moment after the burst rather than at its
            // head. One headset announces an audio interface, a HID interface and a
            // control interface, and at the first of those there is usually nothing that
            // can be opened yet — so the pass runs, finds the device half-arrived, and the
            // rest of the burst was being dropped as duplicates of an event that had
            // already been answered badly. What the person saw was a headset switched on
            // in front of them and a page that stayed empty until the twenty-second timer
            // came round.
            DateTimeOffset due = DateTimeOffset.Now + ChangeCoalesce;
            if (due < _nextDiscovery) _nextDiscovery = due;
        }

        Nudge();
    }

    /// <summary>How long one plug or unplug is given to finish announcing itself.</summary>
    private static readonly TimeSpan ChangeCoalesce = TimeSpan.FromMilliseconds(750);

    // ---------- coordinator ----------

    public PollingAttention Attention { get; private set; } = PollingAttention.Idle;

    public string? FocusedDeviceKey { get; private set; }

    public void SetAttention(PollingAttention attention, string? focusedDeviceKey = null)
    {
        bool changed;

        lock (_sync)
        {
            changed = Attention != attention
                || !string.Equals(FocusedDeviceKey, focusedDeviceKey, StringComparison.Ordinal);

            Attention = attention;
            FocusedDeviceKey = focusedDeviceKey;

            // Raising attention must not wait out an interval that was chosen while
            // nobody was looking: opening the flyout would show a two-minute-old number.
            if (changed) DueNow();
        }

        if (changed) Nudge();
    }

    public void RequestRefresh(string deviceKey, bool full = true)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        lock (_sync)
        {
            if (full) _fullRefreshRequests.Add(deviceKey);
            EntryFor(deviceKey).NextDue = DateTimeOffset.MinValue;
        }

        Nudge();
    }

    // ---------- loop ----------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.PollingStarted();

        // Started here rather than in the constructor: a notification that arrives before
        // the loop exists has nothing to wake.
        _changes?.Start();

        // Side by side, not awaited: the fast lane must not be held up by a headset that
        // takes four seconds to answer, which is the whole reason it is a separate lane.
        Task live = LiveAsync(stoppingToken);

        try
        {
            await SweepAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            await live.ConfigureAwait(false);
        }

        _logger.PollingStopped();
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan sleep;

            try
            {
                sleep = await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any single failure; one bad device cannot stop the rest.
            catch (Exception ex)
            {
                _logger.PollingIterationFailed(ex);
                sleep = _options.CurrentValue.Interval;
            }
#pragma warning restore CA1031

            if (!await SleepAsync(sleep, stoppingToken).ConfigureAwait(false)) break;
        }
    }

    /// <summary>
    /// The fast lane: the live rows of the one device whose page is open.
    ///
    /// Deliberately narrow. It runs only under <see cref="PollingAttention.Focused"/>,
    /// only for that device, and only for capabilities the driver marked live — which
    /// today is one row, a microphone's input level. Everything else keeps the ordinary
    /// pace, because everything else is a setting rather than a measurement.
    ///
    /// It asks and does not insist. The session serialises a device's reads, so a live
    /// read waits behind whatever the sweep is doing; a frame lost that way is a frame
    /// nobody misses, and the alternative — a second channel into the device — is how two
    /// readers end up disagreeing about the same hardware.
    /// </summary>
    private async Task LiveAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait = LiveIdle;

            try
            {
                if (await LiveTickAsync(stoppingToken).ConfigureAwait(false)) wait = LiveInterval;
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // Same rule as the sweep: one failed reading must not end the lane.
            catch (Exception ex)
            {
                _logger.PollingIterationFailed(ex);
            }
#pragma warning restore CA1031

            try
            {
                await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>True when there was something live to read, which decides the next wait.</summary>
    private async Task<bool> LiveTickAsync(CancellationToken cancellationToken)
    {
        (PollingAttention attention, string? focused) = Current();

        if (focused is null) return false;
        if (_registry.Session(focused) is not { } session) return false;

        IReadOnlyList<string> live = PollingPolicy.Live(session.Snapshot, attention, focused);
        if (live.Count == 0) return false;

        await session.RefreshAsync(live, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
    {
        DevicePollingOptions options = Effective(_options.CurrentValue, _settings.Current);
        DateTimeOffset now = DateTimeOffset.Now;

        if (now >= _nextDiscovery)
        {
            await _registry.DiscoverAsync(cancellationToken).ConfigureAwait(false);

            bool anyPresent = _registry.Devices.Any(d => d.IsPresent);
            _nextDiscovery = now + PollingPolicy.DiscoveryDelay(anyPresent, options);
        }

        DateTimeOffset nextWake = _nextDiscovery;

        AppSettings settings = _settings.Current;
        var hiddenKeys = settings.HiddenDevices.Keys.ToHashSet(StringComparer.Ordinal);

        // One task per device, awaited together.
        //
        // Devices share nothing: separate handles, separate radios, separate hardware.
        // Read one after another, the slowest of them set the pace for all — a headset
        // that takes seconds to answer held up a mouse that would have taken twenty
        // milliseconds, and nothing on the screen moved until the queue reached it. Each
        // device still reads its own capabilities one at a time, because a single
        // request-and-reply channel cannot carry two questions at once.

        foreach (DeviceSnapshot device in _registry.Devices)
        {
            string key = device.Info.Key;

            (PollingAttention attention, string? focused) = Current();
            Entry entry = EntryFor(key);

            // Read here rather than filtered out of the registry: the settings screen
            // has to be able to list what is hidden, so the registry stays complete and
            // every surface decides for itself what to show.
            bool hidden = DeviceVisibility.IsHidden(device.Info, hiddenKeys, settings.HideSilentDevices);

            PollPlan plan = PollingPolicy.Plan(
                new PollSubject(key, device.IsPresent, device.CanControl, entry.Failures, hidden),
                attention,
                focused,
                options);

            // Read once, in full, the first time a device is seen — and only then.
            //
            // Everything a device can do is a few kilobytes and is kept for as long as it
            // stays attached, so the cost of knowing it is paid once. Without this the
            // cost lands somewhere much worse: the tray reads nothing but the battery, so
            // the first person to open a device found its controls empty and watched them
            // fill in a second later, which reads as a fault rather than as a policy.
            // Hidden devices are excluded — nobody is going to open one.
            // Forgotten when it goes away and when it goes quiet, and the second half of
            // that was missing. A headset switched off on its dongle is still present and
            // still enumerated; it simply stops answering. Coming back to life it had
            // already been introduced, so it never got another full read and every row
            // its own protocol supplies stayed empty until the dongle was unplugged.
            if (!device.IsPresent || !device.CanControl) ForgetIntroduction(key);

            bool firstSight = !hidden
                && device.IsPresent
                && device.CanControl
                && TakeIntroduction(key);

            bool wantsFull = TakeFullRefreshRequest(key) || firstSight;

            if (plan.Scope == PollScope.None && !wantsFull && !plan.Probe) continue;

            if (!wantsFull && now < entry.NextDue)
            {
                if (entry.NextDue < nextWake) nextWake = entry.NextDue;
                continue;
            }

            // Due is stamped before the read rather than after it, so a device that takes
            // four seconds to answer is not rewarded with four extra seconds of quiet.
            entry.NextDue = now + (plan.Interval > TimeSpan.Zero ? plan.Interval : options.Interval);
            if (entry.NextDue < nextWake) nextWake = entry.NextDue;

            DeviceSnapshot subject = device;
            PollScope scope = plan.Scope;
            bool full = wantsFull;
            bool probe = plan.Probe;
            Entry target = entry;

            // Started, not awaited.
            //
            // Waiting for all of them meant the next sweep could not begin until the
            // slowest had finished, so the device whose page is open — the one asked for a
            // reading every second — actually got one at the pace of whichever unrelated
            // headset or dongle was answering worst. A device already busy is skipped
            // rather than queued, which keeps one conversation per device without making
            // the others wait for it.
            if (target.InFlight is { IsCompleted: false }) continue;

            target.InFlight = PollSafeAsync(subject, scope, full, probe, target, cancellationToken);
        }

        TimeSpan sleep = nextWake - DateTimeOffset.Now;

        return sleep < MinSleep ? MinSleep : sleep > MaxSleep ? MaxSleep : sleep;
    }

    /// <summary>
    /// One device's poll, sealed off from the others.
    ///
    /// The devices are read side by side now, and a sweep of eight must not end because
    /// one of them threw. The session already swallows a dead handle, but a driver, a
    /// notifier or a decoder can still fail in a way it does not cover, and without this
    /// that failure would surface out of the gathered task and take the whole tick's
    /// bookkeeping with it.
    /// </summary>
    private async Task PollSafeAsync(
        DeviceSnapshot device,
        PollScope scope,
        bool full,
        bool probe,
        Entry entry,
        CancellationToken cancellationToken)
    {
        // A device gets a slice of time, not all of it.
        //
        // The sweep gathers every device and waits for the lot, and discovery runs at the
        // top of the next one — so a single device that never answers stops not just its
        // own reading but the pass that would have noticed it had been unplugged. That is
        // exactly what happened swapping a headset from its dongle to a cable: the old
        // handle stopped answering, the tick never finished, and the application went on
        // showing the dongle until it was restarted.
        using var slice = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        slice.CancelAfter(DeviceBudget);

        try
        {
            await PollAsync(device, scope, full, probe, entry, slice.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Its own budget ran out. Counts as a failure so the backoff notices, and the
            // sweep carries on without it.
            entry.Failures++;
            _logger.DevicePollTimedOut(device.Info.Key);
        }
#pragma warning disable CA1031 // One device's failure is that device's business.
        catch (Exception ex)
        {
            entry.Failures++;
            _logger.PollingIterationFailed(ex);
        }
#pragma warning restore CA1031
    }

    private async Task PollAsync(
        DeviceSnapshot device,
        PollScope scope,
        bool full,
        bool probe,
        Entry entry,
        CancellationToken cancellationToken)
    {
        IDeviceSession? session = _registry.Session(device.Info.Key);
        if (session is null) return;

        if (probe)
        {
            await session.ProbeAsync(cancellationToken).ConfigureAwait(false);

            // Back from the dead, and this is the second somebody is watching for. Its
            // page holds either nothing or what it read before the device went quiet, so
            // the full read is asked for now rather than at the next tick — which is a
            // wait the person has already spent once, waiting for this.
            if (!device.CanControl && session.Snapshot.CanControl)
            {
                RequestRefresh(device.Info.Key);
            }
        }

        if (full)
        {
            await session.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (scope != PollScope.None)
        {
            await session.RefreshAsync(Wanted(device, scope), cancellationToken).ConfigureAwait(false);
        }

        // The session swallows a dead handle and drops the channel rather than throwing,
        // so a device that stopped answering shows up here as a loss of control.
        if (session.Snapshot.CanControl) entry.Failures = 0;
        else entry.Failures++;

        await EvaluateBatteryAsync(session.Snapshot, entry, cancellationToken).ConfigureAwait(false);
    }

    private static string[] Wanted(DeviceSnapshot device, PollScope scope)
    {
        if (scope != PollScope.BatteryAndVolatile) return BatteryOnly;

        return
        [
            .. BatteryOnly,

            // Anything the hardware can move on its own. Skipping these leaves the open
            // page disagreeing with the wheel under the user's thumb.
            .. device.Capabilities.Values
                .Where(c => c.Descriptor.IsVolatile)
                .Select(c => c.Id),
        ];
    }

    // ---------- battery alerts ----------

    private async Task EvaluateBatteryAsync(DeviceSnapshot device, Entry entry, CancellationToken cancellationToken)
    {
        if (device.BatteryPercent is not { } percent) return;

        // A battery does not fall thirty points in one poll. A reading that claims so is
        // a glitch until a second poll repeats it — a mouse sitting on its charger kept
        // reporting a phantom 15% and waking the user's corner with a low-battery toast,
        // several times in one evening. Real drops confirm themselves half a minute
        // later; phantoms never do. The probation is only against alerts — the interface
        // still shows whatever the device said, because hiding readings is a different
        // and worse policy.
        if (entry.LastBattery >= 0 && entry.LastBattery - percent > 30)
        {
            if (entry.SuspectBattery != percent)
            {
                entry.SuspectBattery = percent;
                return;
            }
        }

        entry.SuspectBattery = -1;
        entry.LastBattery = percent;

        AppSettings settings = _settings.Current;

        BatteryDecision decision = _watch.Evaluate(
            device.Info.Key,
            percent,
            device.IsCharging,
            BatteryWatchdog.ThresholdsFor(settings.LowBatteryThreshold));

        if (!decision.ShouldNotify) return;

        // With several devices the alert has to say which one; "battery low" on its own
        // sends the user hunting through a drawer.
        switch (decision.Alert)
        {
            case BatteryAlert.LowBattery when settings.NotifyLowBattery:
                await _notifier.NotifyAsync(
                    device.Info.DisplayName,
                    $"Low battery — {percent}% left",
                    cancellationToken).ConfigureAwait(false);
                break;

            case BatteryAlert.ChargeComplete when settings.NotifyChargeComplete:
                await _notifier.NotifyAsync(
                    device.Info.DisplayName,
                    "Fully charged",
                    cancellationToken).ConfigureAwait(false);
                break;

            case BatteryAlert.None:
            default:
                break;
        }
    }

    // ---------- plumbing ----------

    private (PollingAttention Attention, string? Focused) Current()
    {
        lock (_sync) return (Attention, FocusedDeviceKey);
    }

    private Entry EntryFor(string key)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out Entry? entry)) return entry;

            entry = new Entry();
            _entries[key] = entry;
            return entry;
        }
    }

    /// <summary>
    /// The configured options with the user's chosen interval applied.
    ///
    /// The settings screen has offered an interval all along and nothing read it: the
    /// scheduler took its period from configuration, so choosing "every 10 seconds"
    /// changed a stored number and nothing else. Zero means the user has not chosen, and
    /// the configured default stands.
    /// </summary>
    private static DevicePollingOptions Effective(DevicePollingOptions options, AppSettings settings)
    {
        if (settings.PollingSeconds <= 0) return options;

        return new DevicePollingOptions
        {
            Interval = TimeSpan.FromSeconds(settings.PollingSeconds),
            FocusedInterval = options.FocusedInterval,
            IdleMultiplier = options.IdleMultiplier,
            DiscoveryInterval = options.DiscoveryInterval,
            IdleDiscoveryInterval = options.IdleDiscoveryInterval,
            MaxBackoffShift = options.MaxBackoffShift,
        };
    }

    private bool TakeFullRefreshRequest(string key)
    {
        lock (_sync) return _fullRefreshRequests.Remove(key);
    }

    /// <summary>
    /// True once per device, the first time it is offered. A device that goes away and
    /// comes back is introduced again, because it may have been changed while it was
    /// gone — a headset re-paired to another host, a mouse whose profile was switched on
    /// the device itself.
    /// </summary>
    private bool TakeIntroduction(string key)
    {
        lock (_sync) return _introduced.Add(key);
    }

    /// <summary>
    /// Forgets that a device was introduced, so it is read in full again when it returns.
    /// A device that went away may come back changed — a headset re-paired to another
    /// host, a mouse whose profile was switched on the mouse itself.
    /// </summary>
    private void ForgetIntroduction(string key)
    {
        lock (_sync) _introduced.Remove(key);
    }

    private void DueNow()
    {
        foreach (Entry entry in _entries.Values) entry.NextDue = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Cuts the current sleep short. Cancelling a token the loop is already waiting on is
    /// the simplest thing that cannot lose a signal the way an event or a semaphore can.
    /// </summary>
    private void Nudge()
    {
        CancellationTokenSource previous = Interlocked.Exchange(ref _wake, new CancellationTokenSource());

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Shutting down; the loop is on its way out anyway.
        }

        previous.Dispose();
    }

    /// <summary>Returns false when the service is stopping.</summary>
    private async Task<bool> SleepAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        CancellationToken wake = Volatile.Read(ref _wake).Token;

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, wake);

        try
        {
            await Task.Delay(delay, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A nudge, not a shutdown, unless the outer token says otherwise.
        }

        return !stoppingToken.IsCancellationRequested;
    }

    public override void Dispose()
    {
        // Unsubscribed before anything else: the watcher calls back from a native thread
        // and a notification landing on a half-disposed scheduler is a crash with no
        // stack worth reading.
        if (_changes is not null) _changes.Changed -= OnDevicesChanged;

        _wake.Dispose();
        base.Dispose();
    }

    private sealed class Entry
    {
        public DateTimeOffset NextDue { get; set; } = DateTimeOffset.MinValue;

        /// <summary>
        /// This device's poll, while it is still running. One conversation per device is a
        /// hard rule — the channel is request-and-reply — but it is a rule about this
        /// device, not about the sweep, so the others carry on regardless.
        /// </summary>
        public Task? InFlight { get; set; }

        public int Failures { get; set; }

        /// <summary>The last battery reading that was believed, for the glitch filter.</summary>
        public int LastBattery { get; set; } = -1;

        /// <summary>An implausible reading is on probation until a second poll repeats it.</summary>
        public int SuspectBattery { get; set; } = -1;
    }
}
