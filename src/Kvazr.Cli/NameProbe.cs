using System.Runtime.Versioning;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Reads a device's own name over and over, and shows what actually came back.
///
/// The page opened on a mouse called "Logitech c" followed by a box — a two-character
/// name with an unprintable byte in it — and a minute later the same mouse was named
/// properly and had its picture. A name is asked for once, when the device is first met,
/// so whatever the first read returns is what the page is built from and what the
/// catalogue is searched with; a wrong one costs the model name and the photograph
/// together.
///
/// So this asks the same question many times and prints the bytes rather than the string:
/// how long the device says its name is, and what the chunks carry. A name that comes
/// back short is either a device that answered badly or a reply that belonged to another
/// question, and the raw bytes are what tells the two apart.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NameProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort LongUsage = 0x02;
    private const ushort DeviceName = 0x0005;

    public static int Run(int times)
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

        Console.WriteLine($"[names] {face.ProductName}  {times} reads");
        Console.WriteLine();

        int bad = 0;

        for (int round = 0; round < times; round++)
        {
            using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

            foreach (byte slot in (byte[])[1, HidPlusPlus.Receiver])
            {
                if (HidPlusPlusReader.Interrogate(channel, slot) is not { } paired) continue;

                byte? feature = HidPlusPlusReader.FeatureIndex(channel, slot, DeviceName);
                byte[]? count = feature is { } at ? HidPlusPlusReader.Ask(channel, slot, at, 0x00) : null;
                byte[]? chunk = feature is { } at2 ? HidPlusPlusReader.Ask(channel, slot, at2, 0x01, 0x00) : null;

                bool wrong = paired.Name.Length < 4 || paired.Name.Any(c => c < 0x20 || c > 0x7E);
                if (wrong) bad++;

                Console.WriteLine(
                    $"  {round,3} slot {slot,3}  {(wrong ? "BAD " : "ok  ")}"
                    + $"name \"{paired.Name}\" ({paired.Name.Length})"
                    + $"  feature {(feature is { } f ? f.ToString() : "-")}"
                    + $"  count {Show(count)}"
                    + $"  chunk {Show(chunk)}");

                break;
            }
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "  Every read came back a name."
            : $"  {bad} of {times} reads came back something that is not a name.");

        return 0;
    }

    private static string Show(byte[]? reply) =>
        reply is null ? "none"
        : HidPlusPlus.IsError(reply) ? HidPlusPlus.Explain(reply)
        : Convert.ToHexString(reply.AsSpan(4, Math.Min(12, reply.Length - 4)));
}
