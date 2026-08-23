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

    private AudioConnection(Dictionary<string, Binding> bindings, List<IAudioEndpointChannel> channels)
    {
        _bindings = bindings;
        _channels = channels;

        Capabilities = [.. bindings.Values.Select(b => b.Descriptor)];
    }

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

            bindings[row.CapabilityId] = new Binding(channel, row.Control, row.Descriptor);
        }

        return bindings.Count == 0
            ? null
            : new AudioConnection(bindings, [.. opened.Values]);
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

            _ => CapabilityValue.Absent,
        };

        return Task.FromResult(value);
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
        foreach (IAudioEndpointChannel channel in _channels) channel.Dispose();

        _channels.Clear();
        _bindings.Clear();
    }

    private sealed record Binding(IAudioEndpointChannel Channel, AudioControl Control, Capability Descriptor);
}
