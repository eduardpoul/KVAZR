using System.Runtime.Versioning;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Which report rates the mouse offers, asked link by link, and which link it is on.
///
/// The page shows four — 125 to 1000 — and the question is whether that is the device's
/// answer or ours. The rate feature is asked per connection, and a mouse can offer one set
/// on a cable and another on its receiver; the page picks the list that contains the rate
/// the mouse says it is running at, which is a guess whenever both lists contain it.
///
/// So this asks every function of the feature with every link number and prints what came
/// back, rather than deciding anything. Read-only.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RateListProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort LongUsage = 0x02;
    private const ushort ExtendedRate = 0x8061;
    private const ushort PlainRate = 0x8060;
    private const ushort OnboardProfiles = 0x8100;

    /// <summary>The protocol's own order for the bits in the mask.</summary>
    private static readonly int[] Rates = [125, 250, 500, 1000, 2000, 4000, 8000];

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

        Console.WriteLine($"[rates] {face.ProductName}, device {device}");
        Console.WriteLine();

        foreach ((ushort id, string what) in new (ushort, string)[] { (ExtendedRate, "0x8061"), (PlainRate, "0x8060") })
        {
            if (HidPlusPlusReader.FeatureIndex(channel, device, id) is not { } feature)
            {
                Console.WriteLine($"  {what}  not on this device");
                continue;
            }

            Console.WriteLine($"  {what} at index {feature}");

            for (byte function = 0; function <= 2; function++)
            {
                foreach (byte link in (byte[])[0x00, 0x01, 0x02])
                {
                    byte[]? reply = HidPlusPlusReader.Ask(channel, device, feature, function, link);

                    string said = reply is null ? "no answer"
                        : HidPlusPlus.IsError(reply) ? HidPlusPlus.Explain(reply)
                        : Convert.ToHexString(reply.AsSpan(4, Math.Min(8, reply.Length - 4)));

                    string reading = string.Empty;

                    if (reply is { Length: > 5 } && !HidPlusPlus.IsError(reply))
                    {
                        int mask = (reply[4] << 8) | reply[5];
                        reading = $"   as a mask: {string.Join(", ", Rates.Where((_, i) => (mask & (1 << i)) != 0))}"
                            + $"   as a rate: {(reply[4] < Rates.Length ? Rates[reply[4]] + " Hz" : "-")}";
                    }

                    Console.WriteLine($"    fn {function}, link {link}   {said,-20}{reading}");
                }
            }

            Console.WriteLine();
        }

        // And what the profile keeps, which is the other half of the same question: two
        // bytes, and the one the mouse follows is the link it is on.
        if (HidPlusPlusReader.FeatureIndex(channel, device, OnboardProfiles) is { } profiles)
        {
            byte[]? head = HidPlusPlusReader.Ask(channel, device, profiles, 0x05, 0x00, 0x01, 0x00, 0x00);

            if (head is { Length: > 6 } && !HidPlusPlus.IsError(head))
            {
                Console.WriteLine($"  profile header   byte 0 = {head[4]} ({Name(head[4])})"
                    + $"   byte 1 = {head[5]} ({Name(head[5])})");
            }
        }

        return 0;
    }

    private static string Name(byte index) => index < Rates.Length ? Rates[index] + " Hz" : "?";
}
