using Maxwell.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Maxwell.Core.Devices;

/// <summary>
/// Holds the connection to one device and keeps its snapshot current.
///
/// Owned by <see cref="IDeviceRegistry"/>, which decides when a device appears and
/// disappears; everything else talks to it through <see cref="IDeviceSession"/>.
/// </summary>
public sealed class DeviceSession : IDeviceSession, IDisposable
{
    /// <summary>Wording comes from the design system, where this string is specified.</summary>
    private const string WriteRefused = "Устройство не приняло значение. Вернули прежнее";

    private readonly IDeviceDriver _driver;
    private readonly ICapabilityLedger _ledger;
    private readonly ILogger<DeviceSession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, CapabilityState> _states = new(StringComparer.Ordinal);

    /// <summary>
    /// Capabilities this session wrote since the last read. A volatile value that comes
    /// back changed is an external change only if we were not the one who changed it.
    /// </summary>
    private readonly HashSet<string> _writtenSinceRefresh = new(StringComparer.Ordinal);

    private IDeviceConnection? _connection;
    private DeviceInfo _info;
    private bool _present;
    private DateTimeOffset? _updatedAt;
    private DateTimeOffset? _lastSeenAt;
    private bool _disposed;

    public DeviceSession(
        DeviceInfo info,
        IDeviceDriver driver,
        ICapabilityLedger ledger,
        ILogger<DeviceSession> logger)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(logger);

        _info = info;
        _driver = driver;
        _ledger = ledger;
        _logger = logger;

        Snapshot = Compose();
    }

    public string DeviceKey => _info.Key;

    public DeviceSnapshot Snapshot { get; private set; }

    public event EventHandler<DeviceSnapshot>? SnapshotChanged;

    // ---------- presence, driven by the registry ----------

    /// <summary>
    /// The device is attached. Opens a connection if there is not one already; failing
    /// to open is an ordinary outcome, not an error — a Maxwell reachable only over its
    /// dongle has nothing to open and stays in monitor-only.
    /// </summary>
    public async Task MarkPresentAsync(DeviceInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool wasPresent = _present;

            _info = info;
            _present = true;
            _lastSeenAt = DateTimeOffset.Now;

            bool connected = false;

            if (_connection is null)
            {
                try
                {
                    _connection = await _driver.ConnectAsync(info, cancellationToken).ConfigureAwait(false);
                    SeedCapabilities(_connection);
                    connected = true;
                }
                catch (IOException ex)
                {
                    _logger.DeviceHasNoControlChannel(ex, info.Key);
                }
            }

            // Discovery runs on a timer and mostly finds exactly what it found last
            // time. Announcing that is pure noise for everyone downstream.
            if (wasPresent && !connected) return;

            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The device is gone. Values are kept rather than zeroed — the list shows the last
    /// reading with the time it was taken, which is honest, where a row of zeroes is not.
    /// </summary>
    public async Task MarkAbsentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DropConnection();
            _present = false;
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- reads ----------

    public Task RefreshAllAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync([.. _states.Values.Where(s => s.Descriptor.IsReadable).Select(s => s.Id)], cancellationToken);

    public async Task RefreshAsync(
        IReadOnlyList<string> capabilityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityIds);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IDeviceConnection? connection = _connection;
            if (connection is null) return;

            bool changed = false;

            try
            {
                foreach (string id in capabilityIds)
                {
                    if (!_states.TryGetValue(id, out CapabilityState? state)) continue;
                    if (!state.Descriptor.IsReadable) continue;

                    CapabilityValue value = await connection.ReadAsync(id, cancellationToken).ConfigureAwait(false);

                    // A refusal to answer is not a reason to overwrite a good reading with
                    // nothing; a boom mic that is merely unplugged should not blank the
                    // level the user saw a second ago.
                    if (value.IsAbsent && !state.Value.IsAbsent) continue;

                    bool moved = value != state.Value;

                    _states[id] = state with
                    {
                        Value = value,
                        ChangedExternally = moved
                            && state.Descriptor.IsVolatile
                            && !_writtenSinceRefresh.Contains(id),
                    };

                    changed |= moved;
                }
            }
            catch (IOException ex)
            {
                // The handle is finished. Keep the values, drop the channel, let the
                // registry reconnect on the next discovery pass.
                _logger.DeviceReadFailed(ex, _info.Key);
                DropConnection();
                Publish();
                return;
            }

            _writtenSinceRefresh.Clear();

            // Publishing an identical snapshot makes every consumer re-render for
            // nothing. At a poll every few seconds across several devices that is most
            // of the events the interface would ever see.
            if (!changed) return;

            _updatedAt = DateTimeOffset.Now;
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- writes ----------

    public async Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityId);
        ArgumentNullException.ThrowIfNull(value);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IDeviceConnection? connection = _connection;
            if (connection is null) return false;
            if (!_states.TryGetValue(capabilityId, out CapabilityState? state)) return false;
            if (!state.CanWrite) return false;

            CapabilityValue previous = state.Value;

            _states[capabilityId] = state with { IsWriting = true, WriteError = null };
            Publish();

            bool accepted;
            try
            {
                accepted = await connection.WriteAsync(capabilityId, value, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.DeviceWriteFailed(ex, _info.Key, capabilityId);
                DropConnection();
                Restore(capabilityId, previous);
                Publish();
                return false;
            }

            if (!accepted)
            {
                Restore(capabilityId, previous);
                Publish();
                return false;
            }

            _writtenSinceRefresh.Add(capabilityId);

            _states[capabilityId] = _states[capabilityId] with
            {
                Value = value,
                IsWriting = false,
                WriteError = null,
                ChangedExternally = false,
                Status = CapabilityStatus.Verified,
            };

            _updatedAt = DateTimeOffset.Now;
            Publish();

            // A command the hardware has now accepted is no longer a guess from a
            // disassembly, and the promotion outlives the process.
            if (state.Status == CapabilityStatus.Untested)
            {
                await _ledger.ConfirmAsync(_info.Key, capabilityId, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Restore(string capabilityId, CapabilityValue previous) =>
        _states[capabilityId] = _states[capabilityId] with
        {
            Value = previous,
            IsWriting = false,
            WriteError = WriteRefused,
        };

    // ---------- snapshot assembly ----------

    private void SeedCapabilities(IDeviceConnection connection)
    {
        var seeded = new Dictionary<string, CapabilityState>(StringComparer.Ordinal);

        foreach (Capability descriptor in connection.Capabilities)
        {
            // Values survive a reconnect. The device has not changed just because the
            // cable was reseated, and blanking the page would be a worse lie than a
            // reading that is a few seconds old.
            _states.TryGetValue(descriptor.Id, out CapabilityState? existing);

            seeded[descriptor.Id] = new CapabilityState
            {
                Descriptor = descriptor,
                Value = existing?.Value ?? CapabilityValue.Absent,
                Range = descriptor.Range,
                Status = _ledger.IsConfirmed(_info.Key, descriptor.Id)
                    ? CapabilityStatus.Verified
                    : descriptor.DeclaredStatus,
            };
        }

        _states.Clear();
        foreach ((string id, CapabilityState state) in seeded) _states[id] = state;

        _info = _info with { Transports = connection.Transports };
    }

    private void Publish()
    {
        DeviceAvailability availability = Availability();

        // Requirements and device-sourced bounds are resolved here rather than in the
        // driver: they depend on other readings, and only this layer sees all of them.
        foreach ((string id, CapabilityState state) in _states.ToList())
        {
            _states[id] = state with
            {
                IsAvailable = IsRequirementMet(state.Descriptor, availability),
                Range = Resolve(state.Descriptor),
            };
        }

        Snapshot = Compose(availability);
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private DeviceSnapshot Compose(DeviceAvailability? availability = null) => new()
    {
        Info = _info,
        Availability = availability ?? Availability(),
        Capabilities = new Dictionary<string, CapabilityState>(_states, StringComparer.Ordinal),
        UpdatedAt = _updatedAt,
        LastSeenAt = _lastSeenAt,
    };

    private DeviceAvailability Availability() =>
        !_present ? DeviceAvailability.Offline
        : _connection is null ? DeviceAvailability.MonitorOnly
        : DeviceAvailability.Active;

    private bool IsRequirementMet(Capability descriptor, DeviceAvailability availability) =>
        descriptor.Requirement switch
        {
            RequirementKind.ControlChannel => availability == DeviceAvailability.Active,

            // The boom mic announces itself by answering its level read at all. There is
            // no presence flag; silence is the signal.
            RequirementKind.BoomMicrophone =>
                availability == DeviceAvailability.Active
                && _states.TryGetValue(CapabilityIds.MicLevel, out CapabilityState? level)
                && !level.Value.IsAbsent,

            _ => true,
        };

    private CapabilityRange? Resolve(Capability descriptor)
    {
        CapabilityRange? range = descriptor.Range;

        if (range is not { Source: RangeSource.Device, MaxFromCapability: { } source }) return range;

        return _states.TryGetValue(source, out CapabilityState? ceiling) && ceiling.Value is NumberValue max
            ? range with { Max = max.Value }
            : range;
    }

    private void DropConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DropConnection();
        _gate.Dispose();
    }
}
