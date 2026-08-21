using Maxwell.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Maxwell.Core.Devices;

/// <summary>
/// Reconciles what the drivers report against what is already known, and keeps one
/// session per device alive across disconnects.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry, IDisposable
{
    private readonly IReadOnlyList<IDeviceDriver> _drivers;
    private readonly ICapabilityLedger _ledger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DeviceRegistry> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, DeviceSession> _sessions = new(StringComparer.Ordinal);

    private bool _disposed;

    public DeviceRegistry(
        IEnumerable<IDeviceDriver> drivers,
        ICapabilityLedger ledger,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _drivers = [.. drivers];
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
            var found = new Dictionary<string, (DeviceInfo Info, IDeviceDriver Driver)>(StringComparer.Ordinal);

            foreach (IDeviceDriver driver in _drivers)
            {
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

            foreach (string shadowed in Shadowed(found))
            {
                _logger.DeviceShadowed(shadowed, found[shadowed].Driver.DriverId);
                found.Remove(shadowed);
            }

            foreach ((string key, (DeviceInfo info, IDeviceDriver driver)) in found)
            {
                DeviceSession session = GetOrCreate(key, info, driver);
                await session.MarkPresentAsync(info, cancellationToken).ConfigureAwait(false);
            }

            foreach (DeviceSession session in Missing(found.Keys))
            {
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

            var session = new DeviceSession(info, driver, _ledger, _loggerFactory.CreateLogger<DeviceSession>());
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
