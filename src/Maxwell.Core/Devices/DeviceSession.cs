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
    private const string WriteRefused = "The device refused the value. The previous one is back";

    private readonly IDeviceDriver _driver;
    private readonly IReadOnlyList<ICapabilityContributor> _contributors;
    private readonly ICapabilityLedger _ledger;
    private readonly ILogger<DeviceSession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, CapabilityState> _states = new(StringComparer.Ordinal);

    /// <summary>
    /// Capabilities this session wrote since the last read. A volatile value that comes
    /// back changed is an external change only if we were not the one who changed it.
    /// </summary>
    private readonly HashSet<string> _writtenSinceRefresh = new(StringComparer.Ordinal);

    /// <summary>
    /// Contributors that attached to this device, in the order they were asked.
    ///
    /// Kept apart from the owning connection because they come and go independently of
    /// it: an audio endpoint disappears when the headset sleeps while the vendor
    /// protocol carries on answering over its dongle.
    /// </summary>
    private readonly List<Attachment> _attachments = [];

    /// <summary>
    /// Which channel answers for which capability. Rebuilt on every seed, in channel
    /// order with the owning connection first, which is what makes the owner win when
    /// two sources declare the same id.
    /// </summary>
    private readonly Dictionary<string, IDeviceConnection> _owner = new(StringComparer.Ordinal);

    private IDeviceConnection? _connection;
    private DeviceInfo _info;
    private bool _present;

    /// <summary>
    /// Whether the device has answered anything. Opening an interface proves nothing:
    /// a dongle whose headset is switched off or out of range enumerates exactly like
    /// one with it awake, and swallows every request in silence. The only way to tell
    /// them apart is to ask.
    /// </summary>
    private bool _answers;

    /// <summary>
    /// The reading this device's own driver says means it is awake, remembered from the
    /// moment it connected.
    ///
    /// Remembered rather than asked of the connection each time, because the question
    /// matters most exactly when there is no connection to ask. A handle that has just
    /// been dropped is the strongest evidence a device is not answering, and looking the
    /// heartbeat up through the handle turns that into "the driver named nothing", which
    /// is a different device's rules.
    /// </summary>
    private string? _heartbeat;
    private DateTimeOffset? _updatedAt;
    private DateTimeOffset? _lastSeenAt;
    private bool _disposed;

    public DeviceSession(
        DeviceInfo info,
        IDeviceDriver driver,
        ICapabilityLedger ledger,
        ILogger<DeviceSession> logger,
        IReadOnlyList<ICapabilityContributor>? contributors = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(logger);

        _info = info;
        _driver = driver;
        _ledger = ledger;
        _logger = logger;
        _contributors = contributors ?? [];

        Snapshot = Compose();
    }

    public string DeviceKey => _info.Key;

    public DeviceSnapshot Snapshot { get; private set; }

    public event EventHandler<DeviceSnapshot>? SnapshotChanged;

    // ---------- presence, driven by the registry ----------

    /// <summary>
    /// The device is attached. Opens a connection if there is not one already and then
    /// asks the device something, because being open proves nothing about the hardware
    /// being awake. Neither failure is an error — a headset that is switched off is an
    /// ordinary situation, and it is asked again on the next sweep.
    /// </summary>
    public async Task MarkPresentAsync(DeviceInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool wasPresent = _present;

            // The channel moved. A connection is bound to one path — a dongle's or a
            // cable's — and when the headset is unplugged from the cable and picked up by
            // its dongle, the old handle does not fail loudly: it simply stops answering.
            // Nothing threw, so nothing dropped the connection, and the application went
            // on questioning a path that was no longer there until something finally
            // raised an IO error. That was the minute between pulling a cable and the
            // application noticing.
            if (_connection is not null && MovedChannel(_info, info))
            {
                _logger.DeviceChannelChanged(info.Key);

                // Contributors go with it. The lead changed, and what a contributor knows
                // is tied to the same lead: the audio endpoint the dongle presented is not
                // the one that appears on the cable.
                DropAll();
            }

            _info = info;
            _present = true;
            _lastSeenAt = DateTimeOffset.Now;

            bool connected = false;

            if (_connection is null)
            {
                try
                {
                    _connection = await _driver.ConnectAsync(info, cancellationToken).ConfigureAwait(false);
                    _heartbeat = _connection.Heartbeat;
                    Listen(_connection);
                    connected = true;
                    _answers = false;
                }
                catch (IOException ex)
                {
                    _logger.DeviceHasNoControlChannel(ex, info.Key);
                }
            }

            connected |= await AttachAsync(info, cancellationToken).ConfigureAwait(false);

            // Seeded once both are in, so that the owning connection is first in the
            // channel order and keeps any capability a contributor also declares.
            if (connected) SeedCapabilities();

            // Asked on every sweep, in both directions.
            //
            // It used to be asked only while the answer was already "no", which meant a
            // device could come back to life but never go quiet: nothing revisited the
            // claim unless a read had thrown. A headset that simply stops replying throws
            // nothing at all, so its page went on offering controls for a device in a
            // drawer.
            //
            // One reading per sweep for the devices that have a channel at all, which is
            // the handful with a driver rather than every peripheral on the machine.
            bool probed = false;

            if (HasChannel)
            {
                _answers = await AnswersAsync(cancellationToken).ConfigureAwait(false);
                probed = true;
            }

            // Discovery runs on a timer and mostly finds exactly what it found last
            // time. Announcing that is pure noise for everyone downstream.
            if (wasPresent && !connected && !probed) return;

            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Whether the device is reachable by a different set of channels than before.
    ///
    /// Compared by kind and presence rather than by label: a label carries a product id
    /// that can differ between two ways into the same device, and comparing those would
    /// rebuild the connection on every sweep.
    ///
    /// Only the channels that carry control count, because a connection is bound to one
    /// of those and to nothing else. A headset that gains a Bluetooth channel has not
    /// moved anywhere as far as the open handle is concerned — it can now also be heard
    /// over the radio — and rebuilding the handle for that throws away the applied
    /// equaliser curve and starts a full re-read for no reason anybody asked for.
    /// </summary>
    private static bool MovedChannel(DeviceInfo before, DeviceInfo after)
    {
        static HashSet<TransportKind> Live(DeviceInfo info) =>
            [.. info.Transports.Where(t => t.IsPresent && t.CarriesControl).Select(t => t.Kind)];

        return !Live(before).SetEquals(Live(after));
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
            DropAll();
            _present = false;
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// How many user actions are waiting for the channel.
    ///
    /// One device is one conversation, so a write still waits for whatever reading is in
    /// flight — but it should not wait for the twenty after that. A sweep runs for ten
    /// seconds or more when several capabilities are answering slowly, and pressing a
    /// button behind one was indistinguishable from a hang. The sweep checks this between
    /// readings and stands aside; what it skipped comes round on the next tick, which
    /// costs nothing anybody can see.
    /// </summary>
    private int _waiting;

    /// <summary>
    /// Re-reads what a touch of the device's own controls could have changed.
    ///
    /// The point of listening at all: the wearer turns the wheel and the panel follows at
    /// once, instead of whenever the next sweep comes round. Only the readings the driver
    /// names are asked for — the event says something was touched, not what it now is, and
    /// re-reading everything would take five seconds and throw the benefit away.
    /// </summary>
    private async void OnActivity(object? sender, IReadOnlyList<string> capabilityIds)
    {
        try
        {
            await RefreshAsync(capabilityIds).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The session is closing; there is nothing left to refresh.
        }
        catch (Exception ex)
        {
            _logger.DeviceReadFailed(ex, _info.Key);
        }
    }

    // ---------- reads ----------

    /// <summary>
    /// Everything worth having a stored value for. Live readings are not: they are
    /// measurements taken while somebody watches, and one sample of a microphone's level
    /// during a discovery sweep would light the in-use indicator to learn a number nobody
    /// asked for. The fast lane asks for those by name.
    /// </summary>
    public Task RefreshAllAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(
            [.. _states.Values.Where(s => s.Descriptor.IsReadable && !s.Descriptor.IsLive).Select(s => s.Id)],
            cancellationToken);

    /// <summary>
    /// Whether the device is answering — asked on its own, and settled in both directions.
    ///
    /// Discovery asks the same question, and used to be the only thing that did. That put
    /// twenty seconds between switching a headset on and the page admitting it, and the
    /// same twenty between switching one off and the page giving up its controls. Neither
    /// is a wait anybody attributes to a polling interval; both read as the application
    /// being broken.
    /// </summary>
    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_present || !HasChannel) return;

            bool was = _answers;
            DateTimeOffset? stamped = _updatedAt;

            _answers = await AnswersAsync(cancellationToken).ConfigureAwait(false);

            // The probe keeps whatever it managed to read — over a radio link a reading
            // is too dear to ask for and throw away — so a pass that changed nothing
            // about the verdict may still have brought a value home.
            if (_answers != was || _updatedAt != stamped) Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAsync(
        IReadOnlyList<string> capabilityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilityIds);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!HasChannel) return;

            bool changed = false;

            foreach (string id in capabilityIds)
            {
                // Somebody is waiting to change something. The rest of the sweep can
                // wait; they cannot.
                if (Volatile.Read(ref _waiting) > 0) break;

                if (!_states.TryGetValue(id, out CapabilityState? state)) continue;
                if (!state.Descriptor.IsReadable) continue;
                if (!_owner.TryGetValue(id, out IDeviceConnection? source)) continue;

                CapabilityValue value;

                try
                {
                    value = await source.ReadAsync(id, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    // That handle is finished. The others are not, and the difference now
                    // matters: with one channel per device, a failure meant the device was
                    // gone and dropping everything said the same thing. It does not any
                    // more — a microphone whose volume endpoint vanished still has a
                    // battery, and abandoning the sweep would lose it too.
                    _logger.DeviceReadFailed(ex, _info.Key);
                    Drop(source);
                    Publish();
                    continue;
                }

                // A refusal to answer is not a reason to overwrite a good reading with
                // nothing; a boom mic that is merely unplugged should not blank the
                // level the user saw a second ago.
                if (value.IsAbsent && !state.Value.IsAbsent) continue;

                bool moved = value != state.Value;

                _states[id] = state with
                {
                    Value = value,

                    // A first reading is not a change. Without this the very first
                    // poll flags every volatile capability as "moved on the device",
                    // which is both wrong and the sort of wrong that teaches people
                    // to ignore the marker.
                    ChangedExternally = moved
                        && !state.Value.IsAbsent
                        && state.Descriptor.IsVolatile
                        && !_writtenSinceRefresh.Contains(id),
                };

                changed |= moved;

                // Published as it lands, not held until the sweep is done.
                //
                // One device is read one capability at a time — a single handle and a
                // request-and-reply protocol, so its own reads cannot overlap — and
                // holding every answer back until the last one arrived left the panel
                // empty for as long as the slowest capability took, then filled it in
                // all at once. Publishing per reading costs one snapshot compose, and
                // only when something actually moved.
                if (moved)
                {
                    _updatedAt = DateTimeOffset.Now;
                    Publish();
                }
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

        Interlocked.Increment(ref _waiting);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        try
        {
            if (!_states.TryGetValue(capabilityId, out CapabilityState? state)) return false;
            if (!state.CanWrite) return false;

            // Whoever declared the id takes the write. Sending everything to the owning
            // connection would hand a contributor's row to a driver that has never heard
            // of it, and the refusal would be reported against the wrong device.
            if (!_owner.TryGetValue(capabilityId, out IDeviceConnection? source)) return false;

            CapabilityValue previous = state.Value;

            _states[capabilityId] = state with { IsWriting = true, WriteError = null };
            Publish();

            bool accepted;
            try
            {
                accepted = await source.WriteAsync(capabilityId, value, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.DeviceWriteFailed(ex, _info.Key, capabilityId);
                Drop(source);
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

    /// <summary>
    /// Rebuilds the capability set and the routing table from every open channel.
    ///
    /// Channel order decides ownership, and the owning connection is first, so a
    /// capability two sources both declare belongs to the driver that understands the
    /// device rather than to whichever contributor happened to attach first.
    /// </summary>
    private void SeedCapabilities()
    {
        var seeded = new Dictionary<string, CapabilityState>(StringComparer.Ordinal);
        _owner.Clear();

        foreach (IDeviceConnection channel in Channels())
        {
            foreach (Capability descriptor in channel.Capabilities)
            {
                // Two sources claiming one id is a mistake somewhere rather than a merge
                // to be guessed at — their values would fight each other on every poll.
                // The owner keeps it and the collision is logged where it can be fixed.
                if (!_owner.TryAdd(descriptor.Id, channel))
                {
                    _logger.CapabilityClaimedTwice(_info.Key, descriptor.Id);
                    continue;
                }

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
        }

        _states.Clear();
        foreach ((string id, CapabilityState state) in seeded) _states[id] = state;

        // Transports belong to whoever owns the device. A contributor knows one thing
        // about it and has no business describing how it is attached.
        //
        // The connection is not the whole story either, and taking it as the whole story
        // cost twenty seconds of every minute. It describes the lead it talks over;
        // discovery can know of a channel it does not use — the Maxwell's Bluetooth, which
        // carries sound and a battery reading and no control at all. Overwritten with the
        // connection's list, that channel vanished the moment the device connected, the
        // next sweep found it again, the difference read as the headset having moved, and
        // the whole connection was torn down and rebuilt. Every twenty seconds, for as
        // long as the application ran: measured six times in ninety seconds of log.
        if (_connection is not null)
        {
            HashSet<TransportKind> owned = [.. _connection.Transports.Select(t => t.Kind)];

            _info = _info with
            {
                Transports =
                [
                    .. _connection.Transports,
                    .. _info.Transports.Where(t => !owned.Contains(t.Kind)),
                ],
            };
        }
    }

    /// <summary>
    /// Offers the device to every contributor that is not already attached, and says
    /// whether any of them took it.
    ///
    /// Asked again on each sweep by design: a contributor's subject can arrive later than
    /// the device. A headset dongle enumerates the moment it is plugged in, while its
    /// audio endpoints appear only when the headset itself wakes — asking once, at
    /// discovery, would mean the volume rows never showed up at all.
    /// </summary>
    private async Task<bool> AttachAsync(DeviceInfo info, CancellationToken cancellationToken)
    {
        bool attached = false;

        foreach (ICapabilityContributor contributor in _contributors)
        {
            // The owner says it has the deeper answer here. Core Audio beside a vendor
            // protocol is two volumes on one page that are both real and are not the
            // same number, which reads as a bug however carefully either is labelled.
            if (info.Supersedes.Contains(contributor.ContributorId)) continue;

            if (_attachments.Any(a => string.Equals(a.Id, contributor.ContributorId, StringComparison.Ordinal)))
            {
                continue;
            }

            IDeviceConnection? extra;

            try
            {
                extra = await contributor.AttachAsync(info, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                // It recognised the device and could not open it — another process holds
                // the endpoint, or it went away mid-sweep. Never fatal: the device keeps
                // everything its owning driver gives it and we ask again next time.
                _logger.ContributorFailed(ex, info.Key, contributor.ContributorId);
                continue;
            }

            if (extra is null) continue;

            _attachments.Add(new Attachment(contributor.ContributorId, extra));
            Listen(extra);
            attached = true;

            _logger.ContributorAttached(info.Key, contributor.ContributorId, extra.Capabilities.Count);
        }

        return attached;
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
                Choices = Offer(state.Descriptor),
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
        : HasChannel && _answers ? DeviceAvailability.Active
        : DeviceAvailability.Unreachable;

    /// <summary>Whether anything is open at all: the owning connection, or a contributor.</summary>
    private bool HasChannel => _connection is not null || _attachments.Count > 0;

    /// <summary>Every open channel, the owning connection first.</summary>
    private IEnumerable<IDeviceConnection> Channels()
    {
        if (_connection is not null) yield return _connection;

        foreach (Attachment attachment in _attachments) yield return attachment.Connection;
    }

    /// <summary>
    /// Asks the device one question and sees whether anything comes back. The battery is
    /// the natural probe — every device has one and it is the cheapest read — and the
    /// answer is kept rather than thrown away, because over a radio link a full refresh
    /// takes long enough that discarding it would leave the page blank meanwhile.
    ///
    /// The probe may land on a contributor, and that is correct: a device whose only
    /// reader is the HID battery usage is answering, and calling it unreachable because
    /// its owning driver has nothing to ask would be a lie about working hardware.
    ///
    /// Several questions rather than one, and that is the correction that matters. It
    /// asked exactly one capability and read an absent answer as a dead device — so a
    /// single row that is legitimately empty condemned everything beside it. A QuadCast
    /// put "the headset is not answering" over a page of working sliders, because the
    /// first readable row it found was a polar selector, and that one says nothing at all
    /// until a hand turns it. One silent row is not evidence; every row silent is.
    /// </summary>
    private async Task<bool> AnswersAsync(CancellationToken cancellationToken)
    {
        // The driver's own heartbeat, where it named one.
        //
        // Only a driver can know which reading means the device itself is awake, and
        // guessing cost two rounds of this. A battery looks like the obvious choice and is
        // wrong here: a Maxwell's dongle answers 94% with the headset in a drawer, and its
        // firmware version out of the same memory. Anything a dongle remembers describes
        // the dongle.
        //
        // Without one, a few of the device's own rows, and silent only when every one of
        // them is: a microphone's page is built from rows that are legitimately empty
        // until something happens to them, and one of those must not condemn the other
        // five.
        if (_heartbeat is { Length: > 0 } beat)
        {
            // Named and unaskable is not permission to ask something else.
            //
            // The fallback below asks whatever is readable, contributors included, and
            // that is right for a device whose driver named nothing. Here it is a
            // different device answering: the audio endpoint a dongle presents is open
            // and talkative with the headset in a drawer, and letting it speak for the
            // headset is how a page of dead controls comes back to life on its own.
            return _owner.TryGetValue(beat, out IDeviceConnection? heart)
                && await AnsweredAsync(heart, beat, cancellationToken).ConfigureAwait(false);
        }

        List<string> probes =
        [
            .. _states.Values
                .Where(s => s.Descriptor.IsReadable && _owner.ContainsKey(s.Id))
                .OrderBy(s => ReferenceEquals(_owner[s.Id], _connection) ? 0 : 1)
                .ThenBy(s => s.Id == CapabilityIds.PowerBattery ? 0 : 1)
                .Select(s => s.Id)
                .Take(Probes),
        ];

        // Nothing readable to ask. Being open is the best evidence available.
        if (probes.Count == 0) return true;

        foreach (string probe in probes)
        {
            if (await AnsweredAsync(_owner[probe], probe, cancellationToken).ConfigureAwait(false)) return true;
        }

        // Every question asked and every answer empty. A device that will not say one
        // true thing about itself is what unreachable means.
        return false;
    }

    /// <summary>
    /// Asks one channel one question, and says whether anything came back.
    ///
    /// A refusal and a failure both answer "no", and the failure used to answer "yes":
    /// the loop counted only questions that were successfully put, so a handle that threw
    /// on every one of them came out as "could not be asked, so presume it is fine" —
    /// having just been dropped for throwing. The strongest evidence a device is not
    /// answering was being read as evidence that it was.
    ///
    /// Whatever it does say is kept. Over a radio link a reading is too dear to ask for
    /// and throw away, and the panel would otherwise sit empty until the next sweep.
    /// </summary>
    private async Task<bool> AnsweredAsync(
        IDeviceConnection source,
        string capabilityId,
        CancellationToken cancellationToken)
    {
        try
        {
            CapabilityValue value =
                await source.ReadAsync(capabilityId, cancellationToken).ConfigureAwait(false);

            if (value.IsAbsent) return false;

            // Only when it moved. The probe runs once a second on the device whose page
            // is open, and stamping the clock for an answer identical to the last one
            // would republish the whole snapshot at that rate — every consumer
            // re-rendering to be told nothing.
            if (_states.TryGetValue(capabilityId, out CapabilityState? state) && value != state.Value)
            {
                _states[capabilityId] = state with { Value = value };
                _updatedAt = DateTimeOffset.Now;
            }

            return true;
        }
        catch (IOException ex)
        {
            _logger.DeviceReadFailed(ex, _info.Key);
            Drop(source);

            return false;
        }
    }

    /// <summary>
    /// How many questions the liveness probe is allowed. A sweep runs this per device on
    /// every pass, so it is a handful rather than the whole page — and if a device has
    /// answered none of four, the fifth was not going to change the verdict.
    /// </summary>
    private const int Probes = 4;

    private static bool IsRequirementMet(Capability descriptor, DeviceAvailability availability) =>
        descriptor.Requirement switch
        {
            RequirementKind.ControlChannel => availability == DeviceAvailability.Active,

            // Was: the boom mic announces itself by answering its level read at all.
            // It does not answer, ever, so this could only ever be false — and the rows
            // that asked for it are gone. Kept as the plain reachability test so a device
            // that declares it is disabled rather than silently shown as working.
            RequirementKind.BoomMicrophone => availability == DeviceAvailability.Active,

            _ => true,
        };

    /// <summary>
    /// The options for a choice whose list is another capability's reading.
    ///
    /// Empty where the descriptor names no such source or where that source has not
    /// answered yet — the state falls back to the declared list, which is the right thing
    /// to draw while waiting rather than an empty dropdown.
    /// </summary>
    private IReadOnlyList<CapabilityChoice> Offer(Capability descriptor)
    {
        if (descriptor.ChoicesFromCapability is not { Length: > 0 } source) return [];
        if (!_states.TryGetValue(source, out CapabilityState? from)) return [];

        IReadOnlyList<CapabilityValue> items = from.Value.AsVector();
        if (items.Count == 0) return [];

        string unit = descriptor.ChoiceUnit is { Length: > 0 } written ? " " + written : string.Empty;

        // Ordered and deduplicated, because the source is a ladder rather than a menu: a
        // mouse whose steps read 1600/1200/1600 would otherwise offer 1600 twice, and two
        // identical options are two ways to do the same thing that a person has to choose
        // between.
        return
        [
            .. items.Select(v => v.AsNumber(-1))
                .Where(n => n > 0)
                .Distinct()
                .Order()
                .Select(n => new CapabilityChoice(n, $"{n}{unit}")),
        ];
    }

    private CapabilityRange? Resolve(Capability descriptor)
    {
        CapabilityRange? range = descriptor.Range;

        if (range is not { Source: RangeSource.Device, MaxFromCapability: { } source }) return range;

        return _states.TryGetValue(source, out CapabilityState? ceiling) && ceiling.Value is NumberValue max
            ? range with { Max = max.Value }
            : range;
    }

    /// <summary>Subscribes to a channel that can announce changes instead of waiting to be asked.</summary>
    private void Listen(IDeviceConnection channel)
    {
        // Not every source can say when something moved; those that can save the panel
        // from trailing the device by a polling interval.
        if (channel is IDeviceNotifier notifier) notifier.Activity += OnActivity;
    }

    private void Detach(IDeviceConnection channel)
    {
        if (channel is IDeviceNotifier notifier) notifier.Activity -= OnActivity;

        channel.Dispose();
    }

    /// <summary>
    /// Closes one channel and unroutes what it owned, leaving the rest of the device
    /// working.
    ///
    /// Readings stay behind on purpose. A row whose source went away keeps its last
    /// value, exactly as the whole device does when it goes offline; deleting the row
    /// instead would make a momentary handle failure look like hardware that lost a
    /// feature, and the row would reappear a sweep later as if nothing had happened.
    /// </summary>
    private void Drop(IDeviceConnection channel)
    {
        foreach (string id in _owner.Where(p => ReferenceEquals(p.Value, channel)).Select(p => p.Key).ToList())
        {
            _owner.Remove(id);
        }

        if (ReferenceEquals(channel, _connection))
        {
            Detach(channel);
            _connection = null;
            _answers = false;
            return;
        }

        int index = _attachments.FindIndex(a => ReferenceEquals(a.Connection, channel));
        if (index < 0) return;

        Detach(channel);
        _attachments.RemoveAt(index);
    }

    private void DropAll()
    {
        if (_connection is not null) Detach(_connection);

        foreach (Attachment attachment in _attachments) Detach(attachment.Connection);

        _attachments.Clear();
        _owner.Clear();
        _connection = null;
        _answers = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DropAll();
        _gate.Dispose();
    }

    /// <summary>One contributor and the channel it opened.</summary>
    private sealed record Attachment(string Id, IDeviceConnection Connection);
}
