namespace Maxwell.Race;

/// <summary>
/// Turns a ten-band curve into the coefficient blob the headset plays from.
///
/// The blob's shape was read off the wire from Audeze HQ; the arithmetic inside it is our
/// own. That distinction matters: the vendor's generator lives in a closed library we
/// cannot ship, but the headset runs whatever cascade it is handed and has no opinion on
/// how the numbers were arrived at. So the format is theirs and the filters are ordinary
/// textbook biquads.
///
/// Layout, per sample rate:
///
///     &lt;u16 stages&gt; &lt;two words of header&gt; then stages × 10 words
///
/// and each stage is five values — <c>a1/2, a2, b1/2, b2, b0</c> — every one a signed
/// 24-bit fraction in Q23, split across two words as
/// <c>(v &gt;&gt; 8) &amp; 0xFFFF</c> and <c>(v &amp; 0xFF) &lt;&lt; 8</c>. The halving is
/// not decoration: the first coefficient of each pair reaches ±2 and the format holds
/// only ±1.
/// </summary>
public static class MaxwellEqCoefficients
{
    /// <summary>Words one stage occupies.</summary>
    private const int StageWords = 10;

    /// <summary>Scale of the fixed-point format: values are twenty-four bits, signed.</summary>
    private const double Scale = 8388608.0;   // 2^23

    private const int Ceiling = 8388607;      // 2^23 - 1

    /// <summary>
    /// Two words the vendor emits ahead of the first stage and never varies. Carried
    /// through unchanged rather than guessed at — it decodes as a plain fraction, but
    /// what the chip does with it is not established, and a value invented here would be
    /// audible before it was understood.
    /// </summary>
    private static readonly ushort[] Header = [0x6B90, 0x2200];

    /// <summary>
    /// What the vendor puts in the last stage's <c>b0</c> for a flat curve.
    ///
    /// It reads as roughly a fortieth, which cannot be a plain output gain — a flat curve
    /// carrying thirty decibels of attenuation would be unmissable, and on hardware it is
    /// inaudible. Whatever the chip makes of it, matching the vendor is the safe side of
    /// the guess: too small is quiet, and too large is the kind of loud that damages
    /// things.
    /// </summary>
    private const double OutputScale = 0.037188;

    /// <summary>
    /// Build the coefficient words for one sample rate.
    /// </summary>
    /// <param name="frequencies">Band centres in Hz.</param>
    /// <param name="gains">Gain per band, dB.</param>
    /// <param name="sampleRate">Rate the filters are designed against.</param>
    public static ushort[] Build(
        IReadOnlyList<int> frequencies, IReadOnlyList<int> gains, double sampleRate)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(gains);

        if (frequencies.Count != gains.Count)
        {
            throw new ArgumentException("One gain per band.", nameof(gains));
        }

        var words = new List<ushort>(3 + (frequencies.Count * StageWords))
        {
            (ushort)frequencies.Count,
            Header[0],
            Header[1],
        };

        for (int i = 0; i < frequencies.Count; i++)
        {
            // Bandwidth is half the centre, which is what the headset stores for every
            // band of every slot it ships with — a Q of two, and bells that overlap into
            // one continuous curve rather than ten separate needles.
            Biquad stage = Biquad.Peaking(frequencies[i], gains[i], frequencies[i] / 2.0, sampleRate);

            // The gain of the whole chain rides on the last stage, as the vendor's does.
            double b0 = i == frequencies.Count - 1 ? stage.B0 * OutputScale : stage.B0;

            // Numerator first, then denominator. Not obvious and not guessable from a flat
            // curve, where the two are equal and the order cannot be told apart: it took
            // an active band, whose third and fourth values matched a textbook denominator
            // exactly. Emitted the other way round the section is turned upside down —
            // which is stable enough to be accepted and sounds like a rasp.
            Emit(words, stage.B1 / 2);
            Emit(words, stage.B2);
            Emit(words, stage.A1 / 2);
            Emit(words, stage.A2);
            Emit(words, b0);
        }

        return [.. words];
    }

    /// <summary>
    /// The whole payload for <c>0x0E03</c>: a count of blocks and then one block per
    /// sample rate, each naming its rate and carrying its own coefficients.
    ///
    /// All four rates go every time. The headset is not told which one the stream is
    /// using; it is handed a filter for each and picks as the stream changes. Sending one
    /// block under a count of four is what made an early attempt crackle and then trip the
    /// firmware watchdog — the chip believes the count and reads past what arrived.
    /// </summary>
    public static byte[] Blob(IReadOnlyList<int> frequencies, IReadOnlyList<int> gains)
    {
        ArgumentNullException.ThrowIfNull(frequencies);

        var blob = new List<byte> { (byte)Rates.Count, 0, 0, 0 };

        foreach ((int code, double hz) in Rates)
        {
            ushort[] words = Build(frequencies, gains, hz);

            blob.Add((byte)code);
            blob.Add((byte)(code >> 8));
            blob.Add((byte)words.Length);
            blob.Add((byte)(words.Length >> 8));

            foreach (ushort word in words)
            {
                blob.Add((byte)word);
                blob.Add((byte)(word >> 8));
            }
        }

        return [.. blob];
    }

    /// <summary>
    /// Rates the headset runs at, with the codes it names them by. Taken from the vendor's
    /// own table rather than assumed: the codes are not 0..3 and not the rates themselves.
    /// </summary>
    public static readonly IReadOnlyList<(int Code, double Hz)> Rates =
        [(1, 44100.0), (2, 48000.0), (5, 88200.0), (6, 96000.0)];

    private static void Emit(List<ushort> words, double value)
    {
        int fixedPoint = (int)Math.Round(value * Scale, MidpointRounding.AwayFromZero);
        fixedPoint = Math.Clamp(fixedPoint, -Ceiling, Ceiling);

        int carried = fixedPoint & 0xFFFFFF;
        words.Add((ushort)((carried >> 8) & 0xFFFF));
        words.Add((ushort)((carried & 0xFF) << 8));
    }

    /// <summary>One second-order section, already divided through by a0.</summary>
    private readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2)
    {
        /// <summary>
        /// A peaking bell — the Audio EQ Cookbook form, normalised so a0 is one.
        ///
        /// A band at zero decibels is built the same way as any other and comes out with
        /// the numerator equal to the denominator, which is a pass-through. Short-circuiting
        /// it to literal zeroes would work as filtering and be wrong as data: the vendor
        /// writes the real coefficients for an untouched band, and matching that is what
        /// makes the two blobs comparable at all.
        /// </summary>
        public static Biquad Peaking(double frequency, double gainDb, double bandwidth, double sampleRate)
        {
            double a = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double q = frequency / bandwidth;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);

            double a0 = 1 + (alpha / a);

            return new Biquad(
                B0: (1 + (alpha * a)) / a0,
                B1: -2 * cos / a0,
                B2: (1 - (alpha * a)) / a0,
                A1: -2 * cos / a0,
                A2: (1 - (alpha / a)) / a0);
        }
    }
}
