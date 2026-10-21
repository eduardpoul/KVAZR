using System.Runtime.InteropServices;
using Kvazr.Race;

namespace Kvazr.Core.Drivers.Audeze;

/// <summary>
/// Coefficients for a curve, borrowed from the library Audeze HQ ships.
///
/// This is a dependency we would rather not have, and it is deliberately optional: the
/// file belongs to Airoha and arrives with the vendor application, so an installation
/// without it must still work. When it is missing the equaliser reads and switches
/// presets and refuses to write, which is exactly what it did before this existed.
///
/// The alternative was to reproduce the arithmetic ourselves. The wire format is fully
/// understood and our own generator matches this one in 98 words of 103 for a flat
/// curve — but the remaining five are the whole problem: the five values in a stage are
/// not a biquad in any arrangement, and every guess at what they are instead reached the
/// wearer as a loud distortion. Until the structure is actually known rather than
/// inferred, borrowing the answer is the honest option; see docs/FEATURES.md.
///
/// The library is pure arithmetic — thirty-one kilobytes importing nothing but the C
/// runtime, with no device access of its own.
/// </summary>
internal static class AirohaPeqBridge
{
    private const string Dll = @"C:\Program Files\Audeze\AudezeApp\AirohaPeqLibrary.dll";

    /// <summary>Band centres, in the order the headset stores them.</summary>
    public static readonly int[] Bands = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private static readonly (int Code, double Hz)[] Rates =
        [(1, 44100.0), (2, 48000.0), (5, 88200.0), (6, 96000.0)];

    private static readonly Lock Gate = new();

    /// <summary>False when the vendor application is not installed; the equaliser then stays read-only.</summary>
    public static bool Available { get; } = File.Exists(Dll);

    [DllImport(Dll, EntryPoint = "setParam")]
    private static extern void SetParam(int ctx, double sampleRate, int bands, int a4, int a5, int a6);

    [DllImport(Dll, EntryPoint = "set_peq_point")]
    private static extern void SetPeqPoint(int ctx, int index, double frequency, double gain, double width);

    [DllImport(Dll, EntryPoint = "generate_cofe")]
    private static extern void GenerateCofe(int ctx);

    [DllImport(Dll, EntryPoint = "change_rescale_cofe")]
    private static extern void ChangeRescaleCofe(int ctx, double dB);

    [DllImport(Dll, EntryPoint = "get_cofe_count")]
    private static extern ushort GetCofeCount(int ctx);

    [DllImport(Dll, EntryPoint = "get_cofe_param")]
    private static extern IntPtr GetCofeParam(int ctx);

    /// <summary>
    /// The payload for <c>0x0E03</c>: a count of blocks, then one block per sample rate.
    ///
    /// All four rates go every time — the headset is handed a filter for each and chooses
    /// as the stream changes. The count is load-bearing: sending one block under a count
    /// of four makes the chip read past what arrived, which crackles and then trips its
    /// watchdog.
    /// </summary>
    public static byte[] Blob(IReadOnlyList<int> gains)
    {
        ArgumentNullException.ThrowIfNull(gains);

        if (gains.Count != Bands.Length)
        {
            throw new ArgumentException($"The equaliser has {Bands.Length} bands.", nameof(gains));
        }

        // The library keeps one context per index and rebuilds it on every setParam, so
        // two curves generated at once would tread on each other.
        lock (Gate)
        {
            var blob = new List<byte>(844) { (byte)Rates.Length, 0, 0, 0 };

            foreach ((int code, double hz) in Rates)
            {
                SetParam(0, hz, Bands.Length, 1, 0, 0);

                // The fifth argument is a bandwidth in Hz, not a quality factor. Half the
                // centre is what the headset stores for every band it ships with; a Q
                // passed here instead gives bells a couple of hertz wide, which measure
                // correctly and cannot be heard.
                for (int i = 0; i < Bands.Length; i++)
                {
                    SetPeqPoint(0, i, Bands[i], gains[i], Bands[i] / 2.0);
                }

                GenerateCofe(0);
                ChangeRescaleCofe(0, 0.0);

                ushort count = GetCofeCount(0);
                IntPtr words = GetCofeParam(0);

                blob.Add((byte)code);
                blob.Add((byte)(code >> 8));
                blob.Add((byte)count);
                blob.Add((byte)(count >> 8));

                for (int i = 0; i < count; i++)
                {
                    ushort word = (ushort)Marshal.ReadInt16(words, i * 2);
                    blob.Add((byte)word);
                    blob.Add((byte)(word >> 8));
                }
            }

            return [.. blob];
        }
    }
}
