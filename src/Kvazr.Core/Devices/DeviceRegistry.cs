using Kvazr.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Kvazr.Core.Devices;

/// <summary>
/// Reconciles what the drivers report against what is already known, and keeps one
/// session per device alive across disconnects.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry, IDisposable
{
    private readonly IReadOnlyList<IDeviceDriver> _drivers;
    private readonly IReadOnlyList<ICapabilityContributor> _contributors;
    private readonly DriverSwitchboard _switches;
    private readonly ICapabilityLedger _ledger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DeviceRegistry> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, DeviceSession> _sessions = new(StringComparer.Ordinal);

    private bool _disposed;

    /// <summary>The switchboard version this last swept with. See DriverSwitchboard.Version.</summary>
    private int _swept;

    /// <summary>What was switched off at that point, so a change can be told apart from the rest.</summary>
    private HashSet<string> _lastOff = new(StringComparer.Ordinal);

    public DeviceRegistry(
        IEnumerable<IDeviceDriver> drivers,
        ICapabilityLedger ledger,
        ILoggerFactory loggerFactory,
        IEnumerable<ICapabilityContributor>? contributors = null,
        DriverSwitchboard? switches = null)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _drivers = [.. drivers];

        // Optional, and empty is a working configuration: contributors only ever add to
        // what a driver already found, so a build without any behaves exactly as before.
        _contributors = [.. contributors ?? []];

        // Optional, and an empty one is the shipping configuration: with nothing switched
        // off this behaves exactly as it did before there was a switchboard.
        _switches = switches ?? new DriverSwitchboard();
        _swept = _switches.Version;
        _lastOff = [.. _switches.SwitchedOff];
        _ledger = ledger;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DeviceRegistry>();
    }

    public IReadOnlyList<DeviceSnapshot> Devices
    {
        get
        {
            lock (_sessions)
            {
                return
                [
                    .. _sessions.Values
                        .Select(s => s.Snapshot)
                        .OrderByDescending(s => s.IsPresent)
                        .ThenBy(s => s.Info.DisplayName, StringComparer.CurrentCulture),
                ];
            }
        }
    }

    public event EventHandler? DevicesChanged;

    public IDeviceSession? Session(string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        lock (_sessions)
        {
            return _sessions.GetValueOrDefault(deviceKey);
        }
    }

    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Resettle();

            var found = new Dictionary<string, (DeviceInfo Info, IDeviceDriver Driver)>(StringComparer.Ordinal);

            foreach (IDeviceDriver driver in _drivers)
            {
                if (!_switches.IsOn(driver.DriverId)) continue;

                try
                {
                    IReadOnlyList<DeviceInfo> devices =
                        await driver.DiscoverAsync(cancellationToken).ConfigureAwait(false);

                    foreach (DeviceInfo info in devices) found[info.Key] = (info, driver);
                }
                catch (IOException ex)
                {
                    // One vendor's enumeration failing must not blind the app to every
                    // other device on the machine.
                    _logger.DiscoveryFailed(ex, driver.DriverId);
                }
            }

            var shadowedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (string shadowed in Shadowed(found))
            {
                _logger.DeviceShadowed(shadowed, found[shadowed].Driver.DriverId);
                shadowedKeys.Add(shadowed);
                found.Remove(shadowed);
            }

            foreach ((string key, (DeviceInfo info, IDeviceDriver driver)) in found)
            {
                DeviceSession session = GetOrCreate(key, info, driver);
                await session.MarkPresentAsync(info, cancellationToken).ConfigureAwait(false);
            }

            foreach (DeviceSession session in Missing(found.Keys))
            {
                // Superseded rather than gone. A device a deeper driver has just claimed
                // is still on the desk, and marking it absent would leave its row in the
                // list for ever beside the deeper one's — two rows for one headset, one
                // of them permanently offline. That is what happened the moment a vendor
                // driver was switched back on: the session the system driver had been
                // keeping meanwhile had nothing to remove it.
                if (shadowedKeys.Contains(session.DeviceKey))
                {
                    Retire(session);
                    continue;
                }

                if (!session.Snapshot.IsPresent) continue;

                _logger.DeviceDisappeared(session.DeviceKey);
                await session.MarkAbsentAsync(cancellationToken).ConfigureAwait(false);
            }

            // No event of our own: discovery runs on a timer and usually finds exactly
            // what it found last time. The sessions announce the passes that mattered.
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops the sessions the switchboard has just invalidated, and only those.
    ///
    /// Everything about a session was decided by who owned the device: which driver
    /// opened it, and which contributors added rows to it. Neither can be revised in
    /// place, so the session has to go and be built again.
    ///
    /// Which sessions, though, is the part that matters. The first version of this
    /// dropped all of them on any change, and that is not merely wasteful: every open
    /// device was closed and reopened within the same sweep, including the ones nothing
    /// had happened to, and a handle that Windows has not finished releasing answers the
    /// next open with "the requested resource is in use". So a driver being switched
    /// takes only its own devices with it. A contributor is the one case that still
    /// costs everything, because a contributor attaches to whatever any driver found and
    /// there is no cheaper way to know where it landed.
    ///
    /// Dropped rather than marked absent, which is what happens to a device that is
    /// merely unplugged. The hardware has not gone anywhere: it is about to be found
    /// again by a different driver, under that driver's own key, and leaving the old
    /// session behind would put two rows in the list for one headset — one of them
    /// permanently offline, with no way to tell which was real.
    /// </summary>
    private void Resettle()
    {
        int version = _switches.Version;
        if (version == _swept) return;

        _swept = version;

        var off = new HashSet<string>(_switches.SwitchedOff, StringComparer.Ordinal);
        var moved = new HashSet<string>(off, StringComparer.Ordinal);
        moved.SymmetricExceptWith(_lastOff);
        _lastOff = off;

        if (moved.Count == 0) return;

        var driverIds = new HashSet<string>(_drivers.Select(d => d.DriverId), StringComparer.Ordinal);
        bool everything = moved.Any(id => !driverIds.Contains(id));

        List<DeviceSession> doomed;

        lock (_sessions)
        {
            doomed =
            [
                .. _sessions.Values.Where(s => everything || moved.Contains(s.Snapshot.Info.DriverId)),
            ];

        }

        foreach (DeviceSession session in doomed) Retire(session);
    }

    /// <summary>
    /// Keys of devices that another driver found and understands better.
    ///
    /// The system driver finds every peripheral on the machine, which is the point of
    /// it, and that necessarily includes the ones a vendor driver already owns. Left
    /// alone the Maxwell would appear twice — once with sixteen capabilities and once
    /// with a battery reading — and the second row would look like a second headset.
    ///
    /// Overlap is decided on hardware ids rather than on names or models, because a name
    /// is what the two drivers are least likely to agree on: one reads it from the USB
    /// product string, the other has it written into a catalogue.
    ///
    /// Only strictly lower tiers are shadowed. Two drivers at the same tier claiming one
    /// device is a real conflict rather than the expected overlap, and silently dropping
    /// one of them would hide it.
    /// </summary>
    private static List<string> Shadowed(Dictionary<string, (DeviceInfo Info, IDeviceDriver Driver)> found)
    {
        var shadowed = new List<string>();

        foreach ((string key, (DeviceInfo info, IDeviceDriver driver)) in found)
        {
            if (info.HardwareIds.Count == 0) continue;

            bool covered = found.Any(other =>
                !string.Equals(other.Key, key, StringComparison.Ordinal)
                && other.Value.Driver.Tier > driver.Tier
                && other.Value.Info.HardwareIds.Overlaps(info.HardwareIds));

            if (covered) shadowed.Add(key);
        }

        return shadowed;
    }

    private DeviceSession GetOrCreate(string key, DeviceInfo info, IDeviceDriver driver)
    {
        lock (_sessions)
        {
            if (_sessions.TryGetValue(key, out DeviceSession? existing)) return existing;

            var session = new DeviceSession(
                info, driver, _ledger, _loggerFactory.CreateLogger<DeviceSession>(),
                [.. _contributors.Where(c => _switches.IsOn(c.ContributorId))]);
            session.SnapshotChanged += OnSnapshotChanged;
            _sessions[key] = session;

            _logger.DeviceAppeared(key, driver.DriverId);
            return session;
        }
    }

    private List<DeviceSession> Missing(IEnumerable<string> presentKeys)
    {
        var present = presentKeys.ToHashSet(StringComparer.Ordinal);

        lock (_sessions)
        {
            return [.. _sessions.Values.Where(s => !present.Contains(s.DeviceKey))];
        }
    }

    /// <summary>Closes one session and forgets it. The caller decides whether to announce.</summary>
    private void Retire(DeviceSession session)
    {
        lock (_sessions)
        {
            session.SnapshotChanged -= OnSnapshotChanged;
            session.Dispose();
            _sessions.Remove(session.DeviceKey);
        }

        Raise();
    }

    private void OnSnapshotChanged(object? sender, DeviceSnapshot snapshot) => Raise();

    private void Raise() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_sessions)
        {
            foreach (DeviceSession session in _sessions.Values)
            {
                session.SnapshotChanged -= OnSnapshotChanged;
                session.Dispose();
            }

            _sessions.Clear();
        }

        _gate.Dispose();
    }
}
