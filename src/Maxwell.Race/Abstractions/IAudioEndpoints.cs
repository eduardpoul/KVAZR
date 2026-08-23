namespace Maxwell.Race.Abstractions;

/// <summary>Which way the sound goes through an endpoint.</summary>
public enum AudioFlow
{
    /// <summary>Playback: headphones, speakers, a headset's game and chat outputs.</summary>
    Render,

    /// <summary>Capture: a microphone, a headset's boom, a mixer's loopback.</summary>
    Capture,
}

/// <summary>
/// One audio endpoint as the operating system presents it.
///
/// The container id is the point of this record. Windows already knows which endpoints
/// belong to which physical device and stamps them with the same container as that
/// device's other nodes — which is what lets a volume slider find its way onto the page
/// of a headset whose battery is read by a completely different protocol.
/// </summary>
public sealed record AudioEndpoint
{
    /// <summary>The endpoint's own id string, used to reopen it.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// What Windows calls it: "Speakers (Game-Audeze Maxwell)". Long and machine-ish,
    /// but it is the only thing that distinguishes a headset's game output from its
    /// chat output, and the person has seen that exact string in the sound settings.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The physical device this endpoint belongs to, assigned by Windows.</summary>
    public required Guid ContainerId { get; init; }

    public required AudioFlow Flow { get; init; }

    /// <summary>
    /// True for the endpoints Windows invents rather than finds: virtual cables, capture
    /// software, streaming sinks. They all share the container id
    /// <c>00000000-0000-0000-ffff-ffffffffffff</c>, which means "no container at all" —
    /// treating that as a real one would merge every virtual device on the machine into
    /// a single imaginary peripheral.
    /// </summary>
    public bool IsContainerless => ContainerId == VirtualContainer;

    internal static readonly Guid VirtualContainer = new("00000000-0000-0000-ffff-ffffffffffff");
}

/// <summary>
/// The volume, mute and level of one endpoint, for as long as it is held open.
///
/// Unlike a HID handle this is not exclusive and not scarce — Core Audio objects are
/// reference-counted COM interfaces that every audio application on the machine holds
/// at the same time — so the rule about opening handles per operation does not apply
/// here, and reopening per read would cost more than it saves.
/// </summary>
public interface IAudioEndpointChannel : IDisposable
{
    /// <summary>0..100, or null when the endpoint has gone away.</summary>
    int? Volume { get; }

    bool? Mute { get; }

    /// <summary>True when the device took the value.</summary>
    bool SetVolume(int percent);

    bool SetMute(bool mute);
}

/// <summary>
/// The machine's audio endpoints, and a way to operate one.
///
/// Class-level throughout: every USB and Bluetooth audio device on Windows answers here
/// with no vendor protocol whatsoever, which makes this one implementation the whole
/// baseline page for headsets, microphones and speakers alike.
/// </summary>
public interface IAudioEndpoints
{
    /// <summary>
    /// Active endpoints, both directions. Cheap enough to call on a discovery sweep;
    /// opens nothing that has to be closed.
    /// </summary>
    IReadOnlyList<AudioEndpoint> Enumerate();

    /// <summary>Opens one endpoint for reading and writing.</summary>
    /// <exception cref="IOException">The endpoint could not be opened.</exception>
    IAudioEndpointChannel Open(string endpointId);
}
