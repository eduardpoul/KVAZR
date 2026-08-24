using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.Audio;

/// <summary>Which knob on an endpoint a capability drives.</summary>
internal enum AudioControl
{
    Volume,
    Mute,

    /// <summary>The input level. Unlike the other two, reading it means listening.</summary>
    Peak,
}

/// <summary>One capability, and the endpoint knob behind it.</summary>
internal sealed record AudioRow(string CapabilityId, string EndpointId, AudioControl Control, Capability Descriptor);

/// <summary>
/// Turns a device's audio endpoints into rows.
///
/// The interesting case is a device with more than one output. A headset dongle presents
/// its game and chat mixes as two separate endpoints, each with its own volume, and
/// collapsing them into a single "volume" would silently drive one of them and leave the
/// other where it was — which is the complaint people have about every other utility
/// that does this.
/// </summary>
internal static class AudioCapabilities
{
    public static IReadOnlyList<AudioRow> Rows(IReadOnlyList<AudioEndpoint> endpoints, DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(device);

        var rows = new List<AudioRow>();

        // Ordered by name so the ids are the same on the next sweep. An id that moved
        // between endpoints would attach a remembered setting to the wrong output.
        Add(rows, [.. endpoints.Where(e => e.Flow == AudioFlow.Render).OrderBy(e => e.Name, StringComparer.Ordinal)],
            device, AudioFlow.Render);

        Add(rows, [.. endpoints.Where(e => e.Flow == AudioFlow.Capture).OrderBy(e => e.Name, StringComparer.Ordinal)],
            device, AudioFlow.Capture);

        return rows;
    }

    private static void Add(
        List<AudioRow> rows,
        IReadOnlyList<AudioEndpoint> endpoints,
        DeviceInfo device,
        AudioFlow flow)
    {
        bool render = flow == AudioFlow.Render;
        bool several = endpoints.Count > 1;

        string volumeId = render ? CapabilityIds.AudioSystemVolume : CapabilityIds.MicSystemLevel;
        string muteId = render ? CapabilityIds.AudioSystemMute : CapabilityIds.MicSystemMute;
        CapabilityDomain domain = render ? CapabilityDomain.Audio : CapabilityDomain.Microphone;

        // Playback on a microphone is the headphone socket on its base, not a speaker.
        // Windows calls it "Speakers (HyperX QuadCast S)" because Windows has no word for
        // it; the page does, and it is the word printed next to the socket.
        bool monitoring = render && device.Class == DeviceClass.Microphone;

        for (int i = 0; i < endpoints.Count; i++)
        {
            AudioEndpoint endpoint = endpoints[i];
            string? label = several ? Distinguish(endpoint.Name, device) : null;

            string volumeTitle = Titled(
                monitoring ? "Monitoring volume" : render ? "Windows volume" : "Recording level",
                label);

            string muteTitle = Titled(
                monitoring ? "Monitoring off" : render ? "Sound off" : "Microphone off",
                label);

            // The subtitle says whose number this is. A headset with its own amplifier
            // carries two volumes that both look like "volume" and behave completely
            // differently, and the difference has to be readable without touching either.
            string? source = several
                ? endpoint.Name
                : render
                    // What the socket is for, rather than what it is. The artboard says
                    // it this way and it is the better sentence: a person plugs
                    // headphones into a microphone to hear themselves as they speak,
                    // and "the headphone output" leaves them to work that out.
                    ? monitoring ? "Your own voice in the headphones, with no delay" : "The system volume, not the hardware one"
                    : "The recording level in Windows";

            rows.Add(new AudioRow(
                Numbered(volumeId, i),
                endpoint.Id,
                AudioControl.Volume,
                new Capability
                {
                    Id = Numbered(volumeId, i),
                    Domain = domain,
                    Primitive = CapabilityPrimitive.Range,
                    Title = volumeTitle,
                    Subtitle = source,
                    Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },

                    // Anything on the machine can move it — the volume keys, a game, the
                    // mixer — so the page has to yield to the system rather than assume
                    // it is the only writer.
                    IsVolatile = true,
                }));

            rows.Add(new AudioRow(
                Numbered(muteId, i),
                endpoint.Id,
                AudioControl.Mute,
                new Capability
                {
                    Id = Numbered(muteId, i),
                    Domain = domain,
                    Primitive = CapabilityPrimitive.Toggle,
                    Title = muteTitle,
                    Subtitle = several ? endpoint.Name : null,
                    IsVolatile = true,
                }));

            if (render) continue;

            // The input level, and only for what a microphone actually records.
            //
            // Measured in decibels relative to full scale, which is the scale sound is
            // heard on and the only one that makes a meter readable: ordinary speech is
            // about a tenth of full amplitude, so on a linear percentage it and a quiet
            // room share the bottom of the bar. The floor is −60, below which the
            // difference between "quiet" and "nothing" stops being information.
            rows.Add(new AudioRow(
                Numbered(CapabilityIds.MicSystemPeak, i),
                endpoint.Id,
                AudioControl.Peak,
                new Capability
                {
                    Id = Numbered(CapabilityIds.MicSystemPeak, i),
                    Domain = domain,
                    Primitive = CapabilityPrimitive.Readout,
                    Title = Titled("Input level", label),
                    Subtitle = "Measured while this page is open, so Windows shows the microphone as in use",
                    Range = new CapabilityRange { Min = MinimumDecibels, Max = 0, Unit = "dB" },
                    IsLive = true,
                }));
        }
    }

    /// <summary>
    /// The bottom of the meter. A microphone in a silent room sits around −70 dBFS and a
    /// muted one at negative infinity; both are "nothing", and a scale that tried to show
    /// the difference would spend most of itself on it.
    /// </summary>
    internal const int MinimumDecibels = -60;

    /// <summary>
    /// Amplitude as the meter reports it, 0..1, turned into the decibels the row carries.
    ///
    /// Kept here rather than in the platform layer because it is a presentation decision:
    /// what the microphone delivered is a fraction of full scale, and choosing to show
    /// that logarithmically — and where to stop — is this layer's business.
    /// </summary>
    public static int Decibels(double fraction) =>
        fraction <= 0
            ? MinimumDecibels
            : (int)Math.Round(Math.Clamp(20 * Math.Log10(fraction), MinimumDecibels, 0));

    private static string Titled(string title, string? label) => label is null ? title : $"{title} · {label}";

    /// <summary>
    /// The part of an endpoint name that is not the device's own name: "Game" out of
    /// "Speakers (Game-Audeze Maxwell)".
    ///
    /// Every name the device answers to is tried, longest first, because they overlap —
    /// stripping the model "Maxwell" out of "Chat-Audeze Maxwell" leaves "Chat-Audeze",
    /// which reads as a third device rather than as a channel of this one.
    ///
    /// Only used when a device has more than one endpoint of a kind, and only for the
    /// label. Nothing depends on it working: when nothing is left it returns null and the
    /// full Windows name is shown instead, which is still the string the person saw in
    /// the sound settings.
    /// </summary>
    public static string? Distinguish(string endpointName, DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(endpointName);
        ArgumentNullException.ThrowIfNull(device);

        string inner = endpointName;

        int open = endpointName.IndexOf('(', StringComparison.Ordinal);
        int close = endpointName.LastIndexOf(')');
        if (open >= 0 && close > open) inner = endpointName[(open + 1)..close];

        foreach (string name in ((string[])[device.DisplayName, device.Model, device.Vendor])
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .OrderByDescending(n => n.Length))
        {
            int at = inner.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) inner = inner.Remove(at, name.Length);
        }

        inner = inner.Trim(' ', '-', '–', '—', '_', ':', ',');

        return inner.Length == 0 ? null : inner;
    }

    /// <summary>
    /// The first endpoint of a kind keeps the plain id, the rest are numbered. A device
    /// with one output — which is most of them — therefore reads exactly as it would
    /// have without any of this.
    /// </summary>
    private static string Numbered(string id, int index) => index == 0 ? id : $"{id}.{index + 1}";
}
