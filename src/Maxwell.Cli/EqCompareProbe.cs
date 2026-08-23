using Maxwell.Race;

namespace Maxwell.Cli;

/// <summary>
/// Our coefficient generator beside the vendor's, for the same curve.
///
/// The point is not to match them digit for digit — the filters are our own arithmetic
/// and are allowed to differ — but to catch the differences that matter before anything
/// reaches a headset: a wrong stage count, a value in the wrong slot, a sign flipped, a
/// scale out by a factor of two. A flat curve is the strict case, because there the
/// vendor's own numbers are plain textbook biquads and ours should land on them exactly.
/// </summary>
internal static class EqCompareProbe
{
    public static int Run()
    {
        if (!AirohaPeq.Available)
        {
            Console.WriteLine("AirohaPeqLibrary.dll not found — nothing to compare against.");
            return 1;
        }

        (string Label, int[] Gains)[] curves =
        [
            ("flat", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
            ("one band, +6 at 1 kHz", [0, 0, 0, 0, 0, 6, 0, 0, 0, 0]),
            ("cut everything 12 dB", [-12, -12, -12, -12, -12, -12, -12, -12, -12, -12]),
        ];

        foreach ((string label, int[] gains) in curves)
        {
            ushort[] mine = MaxwellEqCoefficients.Build(AirohaPeq.Bands, gains, 44100.0);
            ushort[] theirs = AirohaPeq.Words(gains, 44100.0);

            Console.WriteLine($"{label}:");
            Console.WriteLine($"   words     ours {mine.Length}   vendor {theirs.Length}");

            if (mine.Length != theirs.Length)
            {
                Console.WriteLine("   -> different length, nothing further to compare");
                continue;
            }

            int[] apart = [.. Enumerable.Range(0, mine.Length).Where(i => mine[i] != theirs[i])];
            Console.WriteLine($"   differing {apart.Length} of {mine.Length}");

            foreach (int i in apart.Take(6))
            {
                Console.WriteLine($"      [{i,3}] ours {mine[i]:X4}  vendor {theirs[i]:X4}"
                                  + $"   ({Decode(mine, i):+0.000000;-0.000000} vs {Decode(theirs, i):+0.000000;-0.000000})");
            }

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>The Q23 fraction a word pair carries, given the index of either half.</summary>
    private static double Decode(ushort[] words, int index)
    {
        // Pairs start after the three header words, not at zero.
        int at = index < 3 ? index : 3 + ((index - 3) / 2 * 2);
        if (at + 1 >= words.Length) return 0;

        int raw = ((words[at] << 8) | (words[at + 1] >> 8)) & 0xFFFFFF;
        if ((raw & 0x800000) != 0) raw -= 1 << 24;
        return raw / 8388608.0;
    }
}
