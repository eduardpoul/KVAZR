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

            AudioControl.Mute => binding.Channel.Mute is { } muted
                ? CapabilityValue.Of(muted)
                : CapabilityValue.Absent,

            AudioControl.Peak => Level(binding.EndpointId),

            _ => CapabilityValue.Absent,
        };

        return Task.FromResult(value);
    }

    /// <summary>
    /// Asking for the level is what starts the microphone, and the asking is the timer:
    /// stop asking and the watchdog puts it out.
    /// </summary>
    private CapabilityValue Level(string endpointId) =>
        _listeners.TryGetValue(endpointId, out Listener? listener) && listener.Read() is { } fraction
            ? CapabilityValue.Of(AudioCapabilities.Decibels(fraction))
            : CapabilityValue.Absent;

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
            (AudioControl.Mute, FlagValue flag) => binding.Channel.SetMute(flag.Value),
            _ => false,
        };

        return Task.FromResult(accepted);
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

        private IAudioLevelMeter? _meter;
        private DateTimeOffset _asked;
        private bool _refused;

        /// <summary>The stream's own account of itself, for a log or a page that wants to explain a flat meter.</summary>
        public (long Packets, long Silent) Tally
        {
            get
            {
                lock (_lock) return _meter?.Tally ?? (0, 0);
            }
        }

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

                // The stream died under us — unplugged, or taken exclusively. Let go of it
                // so the next glance can try again from scratch.
                if (level is null) Close();

                return level;
            }
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
        }
    }
}
