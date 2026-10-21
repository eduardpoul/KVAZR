namespace Kvazr.Race.Abstractions;

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

    /// <summary>
    /// True where this is the endpoint Windows is actually using for its kind right now.
    ///
    /// One device can offer several of the same kind and mean one thing by them: a
    /// headset paired over Bluetooth and sitting on its dongle has two microphones as far
    /// as Windows is concerned, and one microphone as far as its owner is concerned. This
    /// is how the one that is live is told from the one that is merely enumerated.
    /// </summary>
    public bool IsDefault { get; init; }

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

    /// <summary>
    /// The loudest sample to pass through this endpoint since the last time it was asked,
    /// 0..100, or null when the endpoint has gone away.
    ///
    /// Peak rather than an average on purpose: an average of a spoken syllable is a small
    /// number that sits near the floor and says nothing about whether the microphone is
    /// working, which is the one question a level meter exists to answer.
    ///
    /// It reads the audio engine's meter, so it moves only while something is streaming
    /// through the endpoint. That is a fact about Windows and not a bug here: with no
    /// stream there is nothing to measure, and the alternative — opening a capture stream
    /// of our own — would light the microphone-in-use indicator for as long as the page
    /// was open.
    /// </summary>
    int? Peak { get; }

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

    /// <summary>
    /// Starts listening to a capture endpoint so its level can be measured.
    ///
    /// Separate from <see cref="Open"/> because it is a different kind of act. Opening an
    /// endpoint reads settings and costs nothing anybody can observe; this one starts a
    /// stream from the microphone, which is what makes the level move at all — and what
    /// lights the in-use indicator. Whoever calls it is answerable for stopping it.
    /// </summary>
    /// <exception cref="IOException">The endpoint could not be captured.</exception>
    IAudioLevelMeter Listen(string endpointId);
}

/// <summary>
/// A microphone held open, and the loudest thing it has heard since it was last asked.
///
/// It exists as a separate object from the endpoint channel so that the listening has an
/// obvious lifetime: while one of these is alive the microphone is in use, and disposing
/// it is what puts the indicator out. Nothing about a level is worth a doubt over whether
/// the machine is still listening.
/// </summary>
public interface IAudioLevelMeter : IDisposable
{
    /// <summary>
    /// The loudest sample since the previous call as a fraction of full scale, 0..1, or
    /// null once the stream has failed — a device unplugged mid-sentence, an
    /// exclusive-mode application taking the endpoint.
    ///
    /// A fraction rather than a percentage, and that is not fussiness. Sound is measured
    /// on a logarithmic scale: a quiet room reads about 0.001 of full scale and ordinary
    /// speech about 0.1, so rounding the raw amplitude to whole percent throws away
    /// everything below a shout. What scale to draw it on is a decision for the layer
    /// that draws it; this one reports what it heard.
    /// </summary>
    double? Read();

    /// <summary>
    /// How the stream is arriving — "48 kHz · 32-bit float · 2 ch". Worth showing on the
    /// page next to the level, and worth having in a log when a meter reads zero.
    /// </summary>
    string Format { get; }

    /// <summary>
    /// What has arrived since the stream started: how many packets, and how many of them
    /// held nothing but zeroes.
    ///
    /// This exists because "the level is zero" has three quite different causes and they
    /// are indistinguishable from the number alone: nothing is arriving, or audio is
    /// arriving and it is digital silence — which is what a microphone muted by its own
    /// button sends, its own switch being invisible to the operating system — or the room
    /// really is that quiet, which no real microphone ever is. Packets with no silence is
    /// the third; packets that are all silence is the second.
    /// </summary>
    (long Packets, long Silent) Tally { get; }
}
