using System.Runtime.Versioning;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Logitech;

/// <summary>
/// What a Logitech pointer says about its own resolution, buttons and identity.
///
/// Every number here comes from the device and none of them is written down anywhere in
/// this application. That is not tidiness — it is the difference between a driver for the
/// range and a driver for one mouse. The resolution range of a Superlight is 100 to 44000
/// and the range of the mouse next to it is something else; a literal would be right once
/// and quietly wrong forever after.
/// </summary>
public sealed record PointerFacts
{
    /// <summary>The resolution now, or null where the device does not offer one.</summary>
    public int? Dpi { get; init; }

    public int? DefaultDpi { get; init; }

    /// <summary>The ends of what it will accept, as it described them.</summary>
    public int? MinDpi { get; init; }

    public int? MaxDpi { get; init; }

    /// <summary>The steps it offers by name, which become the notches on the row.</summary>
    public IReadOnlyList<int> Presets { get; init; } = [];

    /// <summary>
    /// How many buttons it actually has, which is knowable here and nowhere else: its HID
    /// descriptor declares sixteen for a five-button mouse and Windows repeats the
    /// sixteen. Two features state it and they agree.
    /// </summary>
    public int? Buttons { get; init; }

    /// <summary>
    /// The unit id — the only thing anywhere that tells two identical mice apart. Neither
    /// the descriptor nor Windows carries one.
    /// </summary>
    public string? Serial { get; init; }

    /// <summary>How many profiles it stores on board.</summary>
    public int? Profiles { get; init; }

    /// <summary>How often it reports now, in hertz.</summary>
    public int? ReportRate { get; init; }

    /// <summary>Which rates it offers on the link it is using, in hertz.</summary>
    public IReadOnlyList<int> ReportRates { get; init; } = [];
}

/// <summary>
/// Asks a device the questions above, once, when it is first connected.
///
/// Only questions. In HID++ 2.0 the first function of a feature is a getter by
/// convention and the ones that change something sit further down; every call here was
/// walked up to one step at a time on real hardware, each step taken only after the
/// previous answer matched the shape the layout predicted.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PointerReader
{
    private const ushort ExtendedResolution = 0x2202;
    private const ushort Resolution = 0x2201;
    private const ushort OnboardProfiles = 0x8100;

    /// <summary>A value at or above this in the range stream is a step, not a resolution.</summary>
    private const int StepMarker = 0xE000;

    public static PointerFacts Read(WindowsHidChannel channel, byte device)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // Asked first, deliberately: it is one exchange and the rest are a dozen, and a
        // shared receiver path is exactly where the last question in a queue is the one
        // that goes unanswered.
        string? serial = Serial(channel, device);

        byte? extended = HidPlusPlusReader.FeatureIndex(channel, device, ExtendedResolution);
        byte? plain = extended is null ? HidPlusPlusReader.FeatureIndex(channel, device, Resolution) : null;

        (int? dpi, int? standard, int? lowest, int? highest, IReadOnlyList<int> presets) =
            extended is { } index ? Extended(channel, device, index)
            : plain is { } older ? Older(channel, device, older)
            : (null, null, null, null, []);

        (int? buttons, int? profiles) = Profiles(channel, device);
        (int? rate, IReadOnlyList<int> rates) = Rate(channel, device);

        return new PointerFacts
        {
            Dpi = dpi,
            DefaultDpi = standard,
            MinDpi = lowest,
            MaxDpi = highest,
            Presets = presets,
            Buttons = buttons,
            Profiles = profiles,
            ReportRate = rate,
            ReportRates = rates,
            Serial = serial,
        };
    }

    /// <summary>
    /// The newer resolution feature, whose answers were walked through on hardware:
    /// a sensor count, that sensor's capabilities, the range, the named steps, the
    /// lift-off list, and then the current setting.
    /// </summary>
    private static (int? Dpi, int? Default, int? Min, int? Max, IReadOnlyList<int> Presets) Extended(
        WindowsHidChannel channel, byte device, byte index)
    {
        // Two bytes of header — the sensor and the direction — and then the steps. Three
        // was a byte too many, and a byte too many does not fail: it reads the tail of one
        // number and the head of the next, so 800 came out as 8196 and the page offered a
        // list of resolutions no mouse has.
        List<int> presets = Values(HidPlusPlusReader.Ask(channel, device, index, 0x03, 0x00, 0x00, 0x00), 2);
        (int? lowest, int? highest) = Range(channel, device, index);

        // Anything outside the range the device itself described is a misread rather than
        // a step. This is the check that would have caught the byte above without anybody
        // noticing the numbers looked odd.
        if (lowest is { } floor && highest is { } ceiling)
        {
            presets = [.. presets.Where(step => step >= floor && step <= ceiling)];
        }

        byte[]? now = HidPlusPlusReader.Ask(channel, device, index, 0x05, 0x00);

        // sensorIdx, then dpiX, defaultDpiX, dpiY, defaultDpiY as sixteen-bit words.
        int? dpi = Word(now, 5);
        int? standard = Word(now, 7);

        return (dpi, standard, lowest ?? presets.FirstOrDefault(), highest ?? presets.LastOrDefault(), presets);
    }

    /// <summary>
    /// The older feature, which has no range description at all — only the named steps
    /// and the current one. The ends then come from the steps, which is what the device
    /// gave and all it gave.
    /// </summary>
    private static (int? Dpi, int? Default, int? Min, int? Max, IReadOnlyList<int> Presets) Older(
        WindowsHidChannel channel, byte device, byte index)
    {
        // One byte of header here, the sensor. Not verified against hardware: no mouse
        // old enough to carry this feature has been on the desk.
        List<int> presets = Values(HidPlusPlusReader.Ask(channel, device, index, 0x01, 0x00), 1);
        int? dpi = Word(HidPlusPlusReader.Ask(channel, device, index, 0x02, 0x00), 5);

        return (dpi, null, presets.Count > 0 ? presets.Min() : null, presets.Count > 0 ? presets.Max() : null, presets);
    }

    /// <summary>
    /// The two ends of the range, read out of a description that arrives in pieces.
    ///
    /// The pieces are a single stream of sixteen-bit words that can break across a
    /// reply — the first chunk of the mouse measured here ends mid-word — so they are
    /// joined before anything is read out of them. A word at or above 0xE000 is a step
    /// size rather than a resolution, which is how the description says "and every
    /// hundred from here".
    /// </summary>
    private static (int? Min, int? Max) Range(WindowsHidChannel channel, byte device, byte index)
    {
        var stream = new List<byte>();

        for (byte chunk = 0; chunk < 8; chunk++)
        {
            byte[]? reply = HidPlusPlusReader.Ask(channel, device, index, 0x02, 0x00, 0x00, chunk);
            if (reply is null || reply.Length < 8) break;

            // Four bytes of report header, then the sensor, the direction and the chunk
            // number, and the stream after them.
            stream.AddRange(reply[7..]);

            if (reply[7..].All(b => b == 0)) break;
        }

        var values = new List<int>();

        for (int i = 0; i + 1 < stream.Count; i += 2)
        {
            int word = (stream[i] << 8) | stream[i + 1];

            if (word is > 0 and < StepMarker) values.Add(word);
        }

        return values.Count > 0 ? (values.Min(), values.Max()) : (null, null);
    }

    /// <summary>
    /// The button count and the profile count, both from the onboard-profiles feature —
    /// which states them whether or not anybody intends to touch a profile.
    /// </summary>
    private static (int? Buttons, int? Profiles) Profiles(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.FeatureIndex(channel, device, OnboardProfiles) is not { } index)
        {
            return (null, null);
        }

        byte[]? reply = HidPlusPlusReader.Ask(channel, device, index, 0x00);

        // memory model, profile format, macro format, profile count, count out of the
        // box, button count.
        return reply is { Length: > 9 } ? (reply[9], reply[7]) : (null, null);
    }

    /// <summary>The unit id, which is also what the device is known by across its routes.</summary>
    private static string? Serial(WindowsHidChannel channel, byte device) => HidPlusPlusReader.UnitId(channel, device);

    /// <summary>
    /// How often the device reports, and which rates it offers on the link it is using.
    ///
    /// Two halves from two places, and only one of them is knowledge held here. Which
    /// rates exist at all is the protocol's own vocabulary — seven of them, from eight
    /// milliseconds down to a hundred and twenty-five microseconds, in that fixed order —
    /// and it is a dictionary rather than a device's answer. Which of the seven this mouse
    /// offers is the device's answer, and it differs by link: the one measured here offers
    /// four on one and all seven on the other.
    ///
    /// Which link is not something the device will say. It answers the same current rate
    /// whichever one is named, and both its lists contain that rate whenever it is a
    /// common one — so "the list containing what it is doing" picked the first list every
    /// time, and this mouse was offered four rates on a link that offers seven. Measured:
    /// writing 2000 into the profile moved it, and 2000 is only in the longer list.
    ///
    /// So the link comes from how the device is attached, which is known here for nothing:
    /// one answering on its own index is on a cable, one answering through a receiver's
    /// slot is on the air. The wireless half of that is what the 2000 above proved; the
    /// wired half is by elimination, the device rejecting any link but those two.
    /// </summary>
    private static (int? Rate, IReadOnlyList<int> Rates) Rate(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.FeatureIndex(channel, device, ExtendedRate) is not { } index)
        {
            return (null, []);
        }

        byte[]? now = HidPlusPlusReader.Ask(channel, device, index, 0x02, 0x00);
        if (now is null || now.Length < 5 || now[4] >= Rates.Length) return (null, []);

        int rate = Rates[now[4]];

        // The other one second, not never: if the device disagrees with the deduction —
        // it is running at a rate the link it should be on does not offer — then the
        // deduction is what was wrong, and its own answer wins.
        byte[] links = device == HidPlusPlusReader.DirectDevice ? [0x00, 0x01] : [0x01, 0x00];

        foreach (byte link in links)
        {
            byte[]? offered = HidPlusPlusReader.Ask(channel, device, index, 0x00, link);
            if (offered is null || offered.Length < 6) continue;

            int mask = (offered[4] << 8) | offered[5];
            List<int> list = [.. Rates.Where((_, i) => (mask & (1 << i)) != 0)];

            if (list.Contains(rate)) return (rate, list);
        }

        return (rate, []);
    }

    /// <summary>
    /// The protocol's list of report rates, in the order its bitmap names them. A
    /// dictionary, not a device's capabilities: which of these a given mouse offers comes
    /// out of the mask it answers with.
    /// </summary>
    internal static readonly int[] Rates = [125, 250, 500, 1000, 2000, 4000, 8000];

    private const ushort ExtendedRate = 0x8061;

    /// <summary>Sixteen-bit words out of a reply, from the given payload offset, until a zero.</summary>
    private static List<int> Values(byte[]? reply, int skip)
    {
        var values = new List<int>();
        if (reply is null) return values;

        for (int i = 4 + skip; i + 1 < reply.Length; i += 2)
        {
            int word = (reply[i] << 8) | reply[i + 1];
            if (word == 0) break;

            values.Add(word);
        }

        return values;
    }

    private static int? Word(byte[]? reply, int at) =>
        reply is not null && at + 1 < reply.Length && ((reply[at] << 8) | reply[at + 1]) is var word and > 0
            ? word
            : null;
}
