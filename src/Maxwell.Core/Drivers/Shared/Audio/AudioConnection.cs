using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.Audio;

/// <summary>
/// The volume and mute of one device's audio endpoints, contributed to whichever driver
/// owns the device.
///
/// It never owns anything itself, and that is the point: the Maxwell belongs to the RACE
/// driver, the QuadCast to the system tier, and both get their volume rows from here
/// without either driver knowing this exists.
/// </summary>
internal sealed class AudioConnection : IDeviceConnection
{
    private readonly Dictionary<string, Binding> _bindings;
    private readonly List<IAudioEndpointChannel> _channels;
    private readonly Dictionary<string, Listener> _listeners;
    private readonly Timer? _watchdog;

    private AudioConnection(
        Dictionary<string, Binding> bindings,
        List<IAudioEndpointChannel> channels,
        Dictionary<string, Listener> listeners)
    {
        _bindings = bindings;
        _channels = channels;
        _listeners = listeners;

        Capabilities = [.. bindings.Values.Select(b => b.Descriptor)];

        // Only a device with a level row has anything to shut down.
        //
        // The timer is what makes the microphone's lifetime a fact rather than a promise.
        // The level is read while its page is open and simply stops being read when the
        // page closes — there is no "close" call to forget, and no path through the
        // application that can leave the machine listening because something was not
        // tidied up on the way out.
        if (listeners.Count > 0)
        {
            _watchdog = new Timer(_ => CloseIdleListeners(), null, Sweep, Sweep);
        }
    }

    /// <summary>How often the watchdog looks for a microphone nobody is asking about.</summary>
    private static readonly TimeSpan Sweep = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a microphone stays open after the last time its level was asked for.
    ///
    /// Long enough to ride out a slow frame — the level is asked for many times a second,
    /// and stopping and restarting a capture stream between two of them would make the
    /// indicator flicker and cost far more than holding it. Short enough that closing the
    /// page puts the light out while the user is still looking at the screen.
    /// </summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Opens what can be opened and describes what opened. Returns null when nothing did,
    /// which is the honest answer for a device whose endpoints another process has taken
    /// exclusively — better than a page of rows that read as absent for ever.
    /// </summary>
    public static AudioConnection? Open(
        IAudioEndpoints endpoints,
        IReadOnlyList<AudioRow> rows,
        out int refused)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(rows);

        var opened = new Dictionary<string, IAudioEndpointChannel>(StringComparer.Ordinal);
        var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        var listeners = new Dictionary<string, Listener>(StringComparer.Ordinal);

        refused = 0;

        foreach (AudioRow row in rows)
        {
            if (!opened.TryGetValue(row.EndpointId, out IAudioEndpointChannel? channel))
            {
                try
                {
                    channel = endpoints.Open(row.EndpointId);
                }
                catch (IOException)
                {
                    // One endpoint of several. The rest of the device is unaffected, and
                    // the next sweep tries again from scratch.
                    refused++;
                    continue;
                }

                opened[row.EndpointId] = channel;
            }

            // Nothing is opened for a level row here, deliberately. The endpoint is the
            // settings; the stream is the microphone, and it starts the first time
            // somebody looks at the number and not a moment earlier.
            if (row.Control == AudioControl.Peak && !listeners.ContainsKey(row.EndpointId))
            {
                listeners[row.EndpointId] = new Listener(endpoints, row.EndpointId);
            }

            bindings[row.CapabilityId] = new Binding(channel, row.EndpointId, row.Control, row.Descriptor);
        }

        return bindings.Count == 0
            ? null
            : new AudioConnection(bindings, [.. opened.Values], listeners);
    }

    public IReadOnlyList<Capability> Capabilities { get; }

    /// <summary>
    /// None. Transports describe how a device is attached, and that belongs to whoever
    /// owns it — a contributor that volunteered one would be guessing.
    /// </summary>
    public IReadOnlyList<TransportInfo> Transports => [];

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (!_bindings.TryGetValue(capabilityId, out Binding? binding)) return Task.FromResult(CapabilityValue.Absent);

        CapabilityValue value = binding.Control switch
        {
            AudioControl.Volume => binding.Channel.Volume is { } percent
                ? CapabilityValue.Of(percent)
                : CapabilityValue.Absent,

            AudioControl.OnOff => OnOff(binding),

            AudioControl.Peak => Level(binding.EndpointId),

            _ => CapabilityValue.Absent,
        };

        return Task.FromResult(value);
    }

    /// <summary>
    /// Whether the endpoint is passing sound.
    ///
    /// Where there is a stream, the stream decides. Core Audio's mute is a flag, and the
    /// row is its opposite — the knob is "muted", the question a person is asking is
    /// "is it on" — but the flag is only a request, and on this desk the device answers
    /// it in hardware: muting the QuadCast's endpoint puts the light on its body out.
    /// Press the button on the device afterwards and the capsule comes back while the
    /// flag stays exactly where it was. Reading the flag first showed a microphone
    /// switched off with its own meter at 52% beside it.
    ///
    /// So the flag is the fallback, for the endpoints nothing is listening to: every
    /// playback endpoint, and a microphone whose level is not being read.
    /// </summary>
    private CapabilityValue OnOff(Binding binding) =>
        Capsule(binding.EndpointId) is { } passing ? CapabilityValue.Of(passing)
        : binding.Channel.Mute is { } muted ? CapabilityValue.Of(!muted)
        : CapabilityValue.Absent;

    /// <summary>
    /// Asking for the level is what starts the microphone, and the asking is the timer:
    /// stop asking and the watchdog puts it out.
    /// </summary>
    private CapabilityValue Level(string endpointId) =>
        _listeners.TryGetValue(endpointId, out Listener? listener) && listener.Read() is { } fraction
            ? CapabilityValue.Of(AudioCapabilities.Decibels(fraction))
            : CapabilityValue.Absent;

    /// <summary>
    /// What the stream says about the capsule, or null where there is no stream to ask —
    /// every playback endpoint, and a microphone whose level nobody is reading.
    ///
    /// Null rather than true on purpose. "Nobody is listening" is not evidence that the
    /// microphone is on, and turning it into evidence would flip the switch every time a
    /// page closed.
    /// </summary>
    private bool? Capsule(string endpointId) =>
        _listeners.TryGetValue(endpointId, out Listener? listener) ? listener.Capsule : null;

    private void CloseIdleListeners()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        foreach (Listener listener in _listeners.Values) listener.CloseIfIdle(now, Linger);
    }

    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        if (!_bindings.TryGetValue(capabilityId, out Binding? binding)) return Task.FromResult(false);

        bool accepted = (binding.Control, value) switch
        {
            (AudioControl.Volume, NumberValue number) => binding.Channel.SetVolume(number.Value),
            (AudioControl.OnOff, FlagValue flag) => Switch(binding, flag.Value),
            _ => false,
        };

        return Task.FromResult(accepted);
    }

    /// <summary>
    /// Turns the endpoint on or off, and holds the switch where the click put it.
    ///
    /// Two things had to be got right here, and the first one is not obvious. Windows'
    /// flag can already be at the value about to be written while the microphone is in
    /// the other state {d} the device carries its own mute, the button on its body moves
    /// it, and the flag stays where it was. Writing the same value again sends the device
    /// nothing at all, so the switch sprang straight back and the microphone could not be
    /// turned on or off from the page.
    ///
    /// So the flag is pulsed through the opposite value first, which gives the device a
    /// transition to follow. That costs nothing and exposes nothing: the value it passes
    /// through is the one the microphone is already in.
    ///
    /// Then the stream is told what to expect. For the moment after a click the click
    /// wins {d} the packets still arriving describe the microphone as it was, and a
    /// control that argued with the hand that moved it would be unusable.
    /// </summary>
    private bool Switch(Binding binding, bool on)
    {
        bool mute = !on;

        if (binding.Channel.Mute == mute) _ = binding.Channel.SetMute(on);

        if (!binding.Channel.SetMute(mute)) return false;

        if (_listeners.TryGetValue(binding.EndpointId, out Listener? listener)) listener.Expect(on);

        return true;
    }

    public void Dispose()
    {
        _watchdog?.Dispose();

        // The microphone goes first. A device disappearing while its page is open is
        // exactly when a stream would be left running, and it is the one case where the
        // user has no way to put the light out themselves.
        foreach (Listener listener in _listeners.Values) listener.Close();

        foreach (IAudioEndpointChannel channel in _channels) channel.Dispose();

        _listeners.Clear();
        _channels.Clear();
        _bindings.Clear();
    }

    private sealed record Binding(
        IAudioEndpointChannel Channel,
        string EndpointId,
        AudioControl Control,
        Capability Descriptor);

    /// <summary>
    /// One microphone, opened when its level is first asked for and closed again when
    /// nobody has asked for a while.
    ///
    /// A listener rather than a flag somebody sets and clears, because the thing being
    /// managed is not a resource but a light on the taskbar: as long as this holds a
    /// stream, Windows tells the user their microphone is in use, and the only honest way
    /// to keep that true is to tie it to somebody actually looking at the number.
    /// </summary>
    private sealed class Listener(IAudioEndpoints endpoints, string endpointId)
    {
        private readonly object _lock = new();

        /// <summary>
        /// How many empty packets in a row it takes to call the capsule silent — about a
        /// quarter of a second, at the ten milliseconds a packet covers.
        ///
        /// Asymmetric, and deliberately: one packet with anything in it turns the
        /// microphone back on at once, because that is the moment somebody has just
        /// pressed the button and is watching for the answer. Going the other way is a
        /// slower claim to make, so it is made slowly.
        /// </summary>
        private const int MutedAfter = 25;

        private IAudioLevelMeter? _meter;
        private DateTimeOffset _asked;
        private bool _refused;

        private long _seenPackets;
        private long _seenEmpty;
        private long _run;
        private bool? _capsule;
        private bool? _wanted;
        private DateTimeOffset _until;

        /// <summary>The stream's own account of itself, for a log or a page that wants to explain a flat meter.</summary>
        public (long Packets, long Silent) Tally
        {
            get
            {
                lock (_lock) return _meter?.Tally ?? (0, 0);
            }
        }

        /// <summary>
        /// Whether the capsule is passing sound, or null while nothing is streaming and
        /// there is therefore nothing to go on.
        ///
        /// Measured rather than asked, because there is nothing to ask: a microphone muted
        /// by its own button reports it on a vendor page or not at all. What it does do is
        /// send exact zeroes — not a quiet room, zeroes — and that is the whole signal.
        /// Sixty seconds of a QuadCast: every live half-second carried about fifty packets
        /// and none of them empty, every muted one carried fifty and all of them empty,
        /// and the quietest live window still measured −57 dBFS. The two states are
        /// not neighbours on a scale, which is why no threshold in decibels appears here:
        /// the meter's own floor is −60, so a quiet room and a dead capsule draw alike.
        /// </summary>
        public bool? Capsule
        {
            get
            {
                lock (_lock)
                {
                    if (_wanted is { } wanted)
                    {
                        if (_capsule == wanted || DateTimeOffset.Now >= _until) _wanted = null;
                        else return wanted;
                    }

                    return _capsule;
                }
            }
        }

        /// <summary>
        /// Says what the microphone has just been told to do, and holds the answer there
        /// until the stream agrees or the wait runs out.
        ///
        /// Both halves matter. The packets already in flight describe the microphone as
        /// it was a moment ago {d} about seventy milliseconds of them, measured {d} and the
        /// verdict needs a quarter of a second of silence before it will call anything
        /// muted, so for a third of a second after a click the stream is still arguing
        /// for the old state.
        ///
        /// It ends the moment the stream agrees rather than running the clock down, so a
        /// switch that worked feels immediate; and it ends on the clock if it never
        /// agrees, because a device that refused is something the page has to admit
        /// rather than paper over.
        /// </summary>
        public void Expect(bool on)
        {
            lock (_lock)
            {
                _capsule = null;
                _run = 0;
                (_seenPackets, _seenEmpty) = _meter?.Tally ?? (0, 0);

                _wanted = on;
                _until = DateTimeOffset.Now + Patience;
            }
        }

        /// <summary>How long a click outranks the stream. Long enough to cover the packets
        /// in flight and the run of silence the verdict waits for, and short enough that a
        /// device which ignored the write is found out while the hand is still there.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

        public double? Read()
        {
            lock (_lock)
            {
                _asked = DateTimeOffset.Now;

                if (_meter is null)
                {
                    // One refusal is remembered: an endpoint held exclusively by another
                    // application refuses every time, and asking it sixteen times a second
                    // would turn a normal situation into a stream of failures.
                    if (_refused) return null;

                    try
                    {
                        _meter = endpoints.Listen(endpointId);
                    }
                    catch (IOException)
                    {
                        _refused = true;
                        return null;
                    }
                }

                double? level = _meter.Read();

                Follow();

                // The stream died under us — unplugged, or taken exclusively. Let go of it
                // so the next glance can try again from scratch.
                if (level is null) Close();

                return level;
            }
        }

        /// <summary>
        /// Reads the packets that arrived during the last <see cref="Read"/> and moves the
        /// verdict. Called under the lock, straight after the drain that produced them.
        /// </summary>
        private void Follow()
        {
            if (_meter is null) return;

            (long packets, long empty) = _meter.Tally;
            long arrived = packets - _seenPackets;
            long blank = empty - _seenEmpty;
            (_seenPackets, _seenEmpty) = (packets, empty);

            // Nothing came. That says nothing either way, and reading it as silence would
            // mute the row every time a frame ran long.
            if (arrived <= 0) return;

            if (blank < arrived)
            {
                _run = 0;
                _capsule = true;
                return;
            }

            _run += blank;
            if (_run >= MutedAfter) _capsule = false;
        }

        public void CloseIfIdle(DateTimeOffset now, TimeSpan linger)
        {
            lock (_lock)
            {
                if (_meter is null || now - _asked < linger) return;

                Close();

                // A refusal is worth forgetting once nobody is watching: whatever held the
                // endpoint may well have let go by the next time somebody looks.
                _refused = false;
            }
        }

        public void Close()
        {
            _meter?.Dispose();
            _meter = null;

            // Back to knowing nothing. A verdict kept past the end of the stream is a
            // claim about a microphone nobody is listening to any more, and the switch
            // would go on showing it for as long as the device stayed attached.
            _capsule = null;
            _wanted = null;
            _run = 0;
            _seenPackets = 0;
            _seenEmpty = 0;
        }
    }
}
