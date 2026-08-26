using System.Globalization;
using System.Runtime.Versioning;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// The first write this application has ever made to a mouse, and it puts it back.
///
/// Change, read back, restore — in that order and without asking, so that a run which
/// works and a run which goes wrong both end with the mouse as it was found. Everything
/// up to now has been read-only on purpose; this is where that stops, so it stops with
/// the smallest possible thing that can be checked: one number, out of the device's own
/// list of numbers, verified by asking for it again.
///
/// The function numbers are not guessed. Reading is function five and writing is six,
/// with the parameters laid out as sensor, resolution across, resolution down, lift-off
/// — read off Solaar's implementation rather than inferred from the shape of a reply,
/// because being one out here changes somebody's mouse rather than returning an error.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DpiWriteProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort LongUsage = 0x02;
    private const ushort ExtendedResolution = 0x2202;

    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(600);

    public static int Run(int wanted, bool keep)
    {
        HidInterface? face = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == Logitech
                && i.UsagePage == HidPlusPlusPage
                && i.Usage == LongUsage
                && i.OutputReportLength >= 20);

        if (face is null)
        {
            Console.WriteLine("No HID++ long-report interface found.");
            return 1;
        }

        using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

        // Which index answers is not knowable in advance: a mouse on a cable answers on
        // 0xFF, a mouse behind a receiver answers on its pairing slot, and there is no
        // way to tell one arrangement from the other except by asking.
        byte device = 0;
        byte feature = 0;

        foreach (byte candidate in (byte[])[HidPlusPlus.Receiver, 1, 2, 3, 4, 5, 6])
        {
            if (HidPlusPlusReader.FeatureIndex(channel, candidate, ExtendedResolution) is not { } found) continue;

            device = candidate;
            feature = found;
            break;
        }

        if (feature == 0)
        {
            Console.WriteLine("Nothing on this receiver has an extended resolution feature.");
            return 1;
        }

        Console.WriteLine($"[dpi] {face.ProductName}  feature at index {feature}, device {device}");
        Console.WriteLine();

        if (Current(channel, device, feature) is not { } before)
        {
            Console.WriteLine("  Could not read the resolution to start from — nothing was written.");
            return 1;
        }

        Console.WriteLine($"  before   {before.Dpi} DPI, lift-off {before.Lod}");

        // Which side is in charge. A gaming mouse running its own stored profile is not
        // obliged to accept a setting from a computer, and the refusal looks exactly like
        // a function that does not exist.
        if (HidPlusPlusReader.FeatureIndex(channel, device, OnboardProfiles) is { } profiles)
        {
            byte[]? mode = HidPlusPlusReader.Ask(channel, device, profiles, 0x02);

            Console.WriteLine(mode is { Length: > 4 } && !HidPlusPlus.IsError(mode)
                ? $"  mode     {mode[4]} ({Mode(mode[4])})"
                : "  mode     the mouse would not say");
        }

        if (!Write(channel, device, feature, wanted, before.Lod))
        {
            Console.WriteLine("  write    refused");
            return 1;
        }

        Console.WriteLine($"  wrote    {wanted} DPI");

        if (Current(channel, device, feature) is not { } after)
        {
            Console.WriteLine("  after    could not read back — try the mouse by hand");
            return 1;
        }

        Console.WriteLine($"  after    {after.Dpi} DPI, lift-off {after.Lod}");
        Console.WriteLine();
        Console.WriteLine(after.Dpi == wanted
            ? "  It took."
            : $"  It did not take: asked for {wanted}, the mouse says {after.Dpi}.");

        if (keep)
        {
            Console.WriteLine("  Left as written, because --keep was given.");
            return 0;
        }

        Console.WriteLine();

        if (Write(channel, device, feature, before.Dpi, before.Lod)
            && Current(channel, device, feature) is { } back
            && back.Dpi == before.Dpi)
        {
            Console.WriteLine($"  restored {back.Dpi} DPI");
            return 0;
        }

        Console.WriteLine($"  RESTORE FAILED — the mouse is not back at {before.Dpi}. Set it by hand.");
        return 1;
    }

    /// <summary>
    /// What the mode byte means. Named here rather than left as a number because the two
    /// values are the whole question: on its own profile, or taking orders.
    /// </summary>
    private static string Mode(byte mode) => mode switch
    {
        0x01 => "its own stored profile",
        0x02 => "taking settings from the computer",
        _ => "unknown",
    };

    private const ushort OnboardProfiles = 0x8100;

    /// <summary>Function five: the resolution now, its factory value, and the lift-off.</summary>
    private static (int Dpi, byte Lod)? Current(WindowsHidChannel channel, byte device, byte feature)
    {
        byte[]? reply = HidPlusPlusReader.Ask(channel, device, feature, 0x05, 0x00);

        // Four bytes of report header, the sensor index, then the resolution across, the
        // factory resolution, the resolution down, its factory value, and the lift-off.
        return reply is { Length: > 13 } && !HidPlusPlus.IsError(reply)
            ? ((reply[5] << 8) | reply[6], reply[13])
            : null;
    }

    /// <summary>Function six: sensor, across, down, lift-off.</summary>
    private static bool Write(WindowsHidChannel channel, byte device, byte feature, int dpi, byte lod)
    {
        byte[] parameters =
        [
            0x00,
            (byte)(dpi >> 8), (byte)(dpi & 0xFF),
            (byte)(dpi >> 8), (byte)(dpi & 0xFF),
            lod,
        ];

        byte[]? reply = channel.Request(
            HidPlusPlus.Request(channel.OutputLength, device, feature, 0x06, parameters),
            r => HidPlusPlus.Answers(r, device, feature, 0x06),
            Wait);

        if (reply is not null && HidPlusPlus.IsError(reply))
        {
            Console.WriteLine($"           {HidPlusPlus.Explain(reply)}");
            return false;
        }

        return reply is not null;
    }

    internal static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
