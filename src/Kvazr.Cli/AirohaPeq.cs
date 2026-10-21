using System.Runtime.InteropServices;

namespace Kvazr.Cli;

/// <summary>
/// Audeze HQ's coefficient generator, called directly.
///
/// This is a deliberate crutch and must not migrate into the product: the library ships
/// with the vendor application, so depending on it would mean our app needs theirs
/// installed — the opposite of the point. It is here to answer one question that cannot
/// be answered any other way, which is whether the headset can be made to play a curve
/// we hand it at all. Once that is known, the same maths gets reimplemented and checked
/// against this as the reference.
///
/// It is pure arithmetic: 31 KB importing nothing but the C runtime, with no HID and no
/// device access of its own. Declared with DllImport rather than LibraryImport purely to
/// keep unsafe code out of a project that has none.
/// </summary>
internal static class AirohaPeq
{
    private const string Dll = @"C:\Program Files\Audeze\AudezeApp\AirohaPeqLibrary.dll";

    /// <summary>The ten band centres the Maxwell's equaliser is fixed to.</summary>
    public static readonly int[] Bands = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>
    /// Width of each bell, in Hz — <b>not</b> a quality factor.
    ///
    /// The library's fifth argument is a bandwidth, and mistaking it for Q is silent: a
    /// curve built with Q's usual 1.41 passed straight through as 1.41 Hz produces ten
    /// needles a couple of hertz wide. They measure correctly at the band centres and are
    /// all but inaudible on music, which is exactly what a working equaliser that does
    /// nothing sounds like.
    ///
    /// Half the centre frequency is what the headset itself stores for every band, giving
    /// Q = 2 and bells that overlap into a continuous curve.
    /// </summary>
    public static double Width(int frequency) => frequency / 2.0;

    public static bool Available => File.Exists(Dll);

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

    [DllImport(Dll, EntryPoint = "get_Rescaling_dB")]
    private static extern double GetRescalingDb(int ctx);

    /// <summary>
    /// Builds the blob the headset expects: a four-byte header, the coefficient count,
    /// then that many sixteen-bit words. The shape is Audeze HQ's, read out of the
    /// vector it assembles before handing it to the transport.
    /// </summary>
    /// <summary>
    /// The sample rates a curve has to be generated for — all of them, every time.
    ///
    /// The headset does not pick one and ask for it; it is handed a filter for each rate
    /// it can run at and chooses when the stream changes. Audeze HQ keeps exactly this
    /// list and walks it.
    /// </summary>
    public static readonly (int Code, double Hz)[] Rates =
        [(1, 44100.0), (2, 48000.0), (5, 88200.0), (6, 96000.0)];

    /// <summary>
    /// Builds the blob the headset expects: a count of blocks, then one block per sample
    /// rate holding its own coefficient set.
    ///
    /// The count is the part that has to be right. Read as a fixed four-byte header —
    /// which is what it looks like, since it is always 04 00 00 00 — it invites sending a
    /// single block under a header claiming four. The headset believes the count, reads
    /// past the end of what arrived, and plays whatever it finds: a loud crackle, and
    /// twice a watchdog reboot. Verified against a capture of Audeze HQ: 844 bytes,
    /// consumed exactly.
    /// </summary>
    public static byte[] Blob(IReadOnlyList<int> gains)
    {
        ArgumentNullException.ThrowIfNull(gains);

        if (gains.Count != Bands.Length)
        {
            throw new ArgumentException($"The equaliser has {Bands.Length} bands.", nameof(gains));
        }

        var blob = new List<byte>(844) { (byte)Rates.Length, 0, 0, 0 };

        foreach ((int code, double hz) in Rates)
        {
            SetParam(0, hz, Bands.Length, 1, 0, 0);
            for (int i = 0; i < Bands.Length; i++) SetPeqPoint(0, i, Bands[i], gains[i], Width(Bands[i]));

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

    /// <summary>The vendor's raw coefficient words for one rate, for comparison only.</summary>
    public static ushort[] Words(IReadOnlyList<int> gains, double sampleRate)
    {
        ArgumentNullException.ThrowIfNull(gains);

        SetParam(0, sampleRate, Bands.Length, 1, 0, 0);
        for (int i = 0; i < Bands.Length; i++) SetPeqPoint(0, i, Bands[i], gains[i], Width(Bands[i]));

        GenerateCofe(0);
        ChangeRescaleCofe(0, 0.0);

        ushort count = GetCofeCount(0);
        IntPtr words = GetCofeParam(0);

        var copy = new ushort[count];
        for (int i = 0; i < count; i++) copy[i] = (ushort)Marshal.ReadInt16(words, i * 2);
        return copy;
    }

    public static double Rescaling => GetRescalingDb(0);
}
