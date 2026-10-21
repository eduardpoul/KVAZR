using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;

namespace Kvazr.Core.Drivers.Audio;

/// <summary>Which knob on an endpoint a capability drives.</summary>
internal enum AudioControl
{
    Volume,

    /// <summary>
    /// Whether the endpoint is on. The knob behind it is Core Audio's mute, so the value
    /// is the opposite of it — see <c>AudioConnection</c>, which is the one place that
    /// knows.
    /// </summary>
    OnOff,

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
        IReadOnlyList<AudioEndpoint> all,
        DeviceInfo device,
        AudioFlow flow)
    {
        bool render = flow == AudioFlow.Render;

        // One device, one microphone — however many Windows counts.
        //
        // A headset paired over Bluetooth and sitting on its dongle enumerates two capture
        // endpoints, and they are the same capsule reached two ways. Drawn as they come,
        // that is two switches, two levels and two meters, each labelled with a link, for
        // a thing there is one of; and one of every pair is dead, because only one link
        // carries at a time. So where Windows says which it is using, that is the one, and
        // it needs no label to say which it is.
        //
        // Where it says nothing — no default of this kind on the machine — the several are
        // drawn as they are, named apart. A microphone with a headphone socket on its base
        // is genuinely two things and both belong on the page.
        IReadOnlyList<AudioEndpoint> endpoints =
            all.Count > 1 && all.Count(e => e.IsDefault) == 1
                ? [.. all.Where(e => e.IsDefault)]
                : all;

        bool several = endpoints.Count > 1;

        string volumeId = render ? CapabilityIds.AudioSystemVolume : CapabilityIds.MicSystemLevel;
        string onId = render ? CapabilityIds.AudioSystemOn : CapabilityIds.MicSystemOn;
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

            // Named for what it is rather than for what it does when pressed. It was
            // "Microphone off", which is the endpoint's own word — Core Audio has a mute
            // — and it reads as a double negative the moment the switch governs anything:
            // "Microphone off" turned on, with the level below it greyed out, is a
            // sentence nobody should have to parse. The value is inverted at the driver,
            // once, where the knob is actually written.
            string onTitle = Titled(
                monitoring ? "Monitoring" : render ? "Sound" : "Microphone",
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

            // The switch before the level, because it is the level's condition: a slider
            // above the thing that decides whether it does anything is a slider people
            // drag first and understand second.
            rows.Add(new AudioRow(
                Numbered(onId, i),
                endpoint.Id,
                AudioControl.OnOff,
                new Capability
                {
                    Id = Numbered(onId, i),
                    Domain = domain,
                    Primitive = CapabilityPrimitive.Toggle,
                    Title = onTitle,

                    // The switch Windows keeps, and the sentence promises no more than
                    // that.
                    //
                    // It used to say the switch follows the microphone's own mute button.
                    // That is true of some — a QuadCast muted by its own button reports
                    // the mute to Windows, and this switch moves with it, which is why the
                    // sentence was there at all. It is not true of others: an Audeze
                    // Maxwell muted at the headset leaves this switch on and goes on
                    // sending the same room noise down the wire, measured — not one silent
                    // packet in four hundred. A sentence that is right about one device and
                    // wrong about the next is worse than none, because it is the ones it
                    // is wrong about that send somebody looking for a fault.
                    Subtitle = several ? endpoint.Name
                        : render ? null
                        : "The mute Windows keeps — some microphones report their own button here",
                    IsVolatile = true,
                }));

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
                    EnabledBy = Numbered(onId, i),

                    // Anything on the machine can move it — the volume keys, a game, the
                    // mixer — so the page has to yield to the system rather than assume
                    // it is the only writer.
                    IsVolatile = true,
                }));

            if (render) continue;

            // The input level, and only for what a microphone actually records.
            //
            // Measured in decibels relative to full scale, which is the scale sound is
            // heard on and the only one that makes a meter readable: ordinary speech is
            // about a tenth of full amplitude, so on a linear percentage it and a quiet
            // room share the bottom of the bar.
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

                    // The meter measures what the switch governs, so it greys with it —
                    // and, less obviously, this is what keeps the switch as fresh as the
                    // meter: the fast lane reads a live row's gate along with the row.
                    EnabledBy = Numbered(onId, i),
                }));
        }
    }

    /// <summary>
    /// The bottom of the meter, and the whole of what makes it readable.
    ///
    /// A microphone in a silent room sits around −70 dBFS and a muted one at negative
    /// infinity; both are "nothing", and a scale that tried to show the difference would
    /// spend most of itself on it. At −60 it spent a sixth: an empty room lit the first
    /// blocks and kept moving, which reads as a microphone picking something up and sent
    /// somebody looking for what.
    ///
    /// So the floor is measured rather than chosen. Forty half-second windows of a quiet
    /// room on this desk: typically −66 to −76 dBFS, loudest −51. Speech into the same
    /// microphone peaked −15 to −25. Six decibels above the loudest silent window puts
    /// an empty room at nothing with margin, and still leaves two thirds of the bar for
    /// somebody talking.
    /// </summary>
    internal const int MinimumDecibels = -45;

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
