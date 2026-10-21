using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kvazr.Core.Drivers.Logitech;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Everything the driver asks a pointer when it first meets it, printed as it came back.
///
/// Through <see cref="PointerReader"/> rather than a copy of it, because what the page
/// will draw is decided by these numbers and by nothing else. The ends of the range in
/// particular are worth seeing before a control is built on them: a scale a person is
/// meant to land 800 on is a different design from one that runs to forty thousand.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FactsProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort LongUsage = 0x02;

    public static int Run()
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

        byte device = 0;

        foreach (byte candidate in (byte[])[1, 2, 3, 4, 5, 6, HidPlusPlus.Receiver])
        {
            if (HidPlusPlusReader.Interrogate(channel, candidate) is null) continue;
            device = candidate;
            break;
        }

        if (device == 0)
        {
            Console.WriteLine("Nothing answered.");
            return 1;
        }

        PointerFacts facts = PointerReader.Read(channel, device);

        Console.WriteLine($"[facts] {face.ProductName}, device {device}");
        Console.WriteLine();
        Console.WriteLine($"  resolution   {facts.Dpi}   default {facts.DefaultDpi}");
        Console.WriteLine($"  range        {facts.MinDpi} … {facts.MaxDpi}");
        Console.WriteLine($"  steps        {string.Join(", ", facts.Presets)}");
        Console.WriteLine($"  report rate  {facts.ReportRate}   of {string.Join(", ", facts.ReportRates)}");
        Console.WriteLine($"  buttons      {facts.Buttons}");
        Console.WriteLine($"  profiles     {facts.Profiles}");
        Console.WriteLine($"  serial       {facts.Serial}");
        Console.WriteLine();

        // The range as the device spells it, before anything reduces it to two ends. It
        // arrives as a run of sixteen-bit words: a resolution, then a marker with a step
        // in its low byte, then the resolution that step runs to, and so on. Which
        // granularity a control should offer is in here and nowhere else.
        if (HidPlusPlusReader.FeatureIndex(channel, device, 0x2202) is { } sensor)
        {
            var stream = new List<byte>();

            for (byte chunk = 0; chunk < 8; chunk++)
            {
                byte[]? reply = HidPlusPlusReader.Ask(channel, device, sensor, 0x02, 0x00, 0x00, chunk);
                if (reply is null || reply.Length < 8 || HidPlusPlus.IsError(reply)) break;

                stream.AddRange(reply[7..]);
                if (reply[7..].All(b => b == 0)) break;
            }

            Console.WriteLine($"  range, raw   {Convert.ToHexString(CollectionsMarshal.AsSpan(stream))}");
            Console.WriteLine();

            for (int i = 0; i + 1 < stream.Count; i += 2)
            {
                int word = (stream[i] << 8) | stream[i + 1];
                if (word == 0) break;

                Console.WriteLine(word >= 0xE000
                    ? $"    step {word & 0x1FFF}"
                    : $"    {word}");
            }
        }

        return 0;
    }
}
