using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Core.Logging;
using Maxwell.Core.Notifications;
using Maxwell.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxwell.Core.Polling;

/// <summary>
/// The only timer in the application. Reads each device as often as the policy says it
/// is worth reading, and raises the battery alerts.
///
/// One loop rather than a timer per device on purpose: with six devices, six
/// independent timers wake the process six times as often for no extra information,
/// and their reads interleave into the serialised sessions in an order nobody chose.
/// </summary>
public sealed class DevicePollingScheduler : BackgroundService, IPollingCoordinator
{
    private static readonly string[] BatteryOnly =
        [CapabilityIds.PowerBattery, CapabilityIds.PowerCharging];

    /// <summary>Floor on the sleep, so a misconfigured interval cannot spin the loop.</summary>
    private static readonly TimeSpan MinSleep = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(60);

    private readonly IDeviceRegistry _registry;
    private readonly INotifier _notifier;
    private readonly ISettingsStore _settings;
    private readonly IOptionsMonitor<DevicePollingOptions> _options;
    private readonly ILogger<DevicePollingScheduler> _logger;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly BatteryWatchdog _watch = new();
    private readonly HashSet<string> _fullRefreshRequests = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    private CancellationTokenSource _wake = new();
    private DateTimeOffset _nextDiscovery = DateTimeOffset.MinValue;

    public DevicePollingScheduler(
        IDeviceRegistry registry,
        INotifier notifier,
        ISettingsStore settings,
        IOptionsMonitor<DevicePollingOptions> options,
        ILogger<DevicePollingScheduler> logger)
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
    }

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

        _logger.PollingStopped();
    }

    private async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
    {
        DevicePollingOptions options = _options.CurrentValue;
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

            bool wantsFull = TakeFullRefreshRequest(key);

            if (plan.Scope == PollScope.None && !wantsFull) continue;

            if (!wantsFull && now < entry.NextDue)
            {
                if (entry.NextDue < nextWake) nextWake = entry.NextDue;
                continue;
            }

            await PollAsync(device, plan.Scope, wantsFull, entry, cancellationToken).ConfigureAwait(false);

            entry.NextDue = now + (plan.Interval > TimeSpan.Zero ? plan.Interval : options.Interval);
            if (entry.NextDue < nextWake) nextWake = entry.NextDue;
        }

        TimeSpan sleep = nextWake - DateTimeOffset.Now;

        return sleep < MinSleep ? MinSleep : sleep > MaxSleep ? MaxSleep : sleep;
    }

    private async Task PollAsync(
        DeviceSnapshot device,
        PollScope scope,
        bool full,
        Entry entry,
        CancellationToken cancellationToken)
    {
        IDeviceSession? session = _registry.Session(device.Info.Key);
        if (session is null) return;

        if (full)
        {
            await session.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await session.RefreshAsync(Wanted(device, scope), cancellationToken).ConfigureAwait(false);
        }

        // The session swallows a dead handle and drops the channel rather than throwing,
        // so a device that stopped answering shows up here as a loss of control.
        if (session.Snapshot.CanControl) entry.Failures = 0;
        else entry.Failures++;

        await EvaluateBatteryAsync(session.Snapshot, cancellationToken).ConfigureAwait(false);
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

    private async Task EvaluateBatteryAsync(DeviceSnapshot device, CancellationToken cancellationToken)
    {
        if (device.BatteryPercent is not { } percent) return;

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
                    $"Низкий заряд — осталось {percent}%",
                    cancellationToken).ConfigureAwait(false);
                break;

            case BatteryAlert.ChargeComplete when settings.NotifyChargeComplete:
                await _notifier.NotifyAsync(
                    device.Info.DisplayName,
                    "Заряжено полностью",
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

    private bool TakeFullRefreshRequest(string key)
    {
        lock (_sync) return _fullRefreshRequests.Remove(key);
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
        _wake.Dispose();
        base.Dispose();
    }

    private sealed class Entry
    {
        public DateTimeOffset NextDue { get; set; } = DateTimeOffset.MinValue;

        public int Failures { get; set; }
    }
}
