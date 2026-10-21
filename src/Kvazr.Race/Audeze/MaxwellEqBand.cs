namespace Kvazr.Race;

/// <summary>
/// One parametric band of a user equaliser slot, as the headset stores it.
///
/// The wire keeps every figure as a signed 32-bit count of hundredths, which is why
/// these are decimals rather than floats: 5000 is 50.00 Hz and -250 is -2.50 dB. Audeze
/// HQ scales by exactly 0.01 before handing the band to the coefficient generator, so
/// the hundredth is the real unit and not a rounding of something finer.
/// </summary>
/// <param name="Tag">
/// The two bytes leading the record. Constant across every slot dumped so far; carried
/// through a write untouched rather than guessed at.
/// </param>
/// <param name="Frequency">Centre frequency, Hz.</param>
/// <param name="Gain">Band gain, dB. Negative cuts.</param>
/// <param name="Width">
/// Width of the bell, in Hz. This is the figure the coefficient generator is handed —
/// its fifth argument is a bandwidth, not a quality factor, and the headset stores half
/// the centre frequency for every band.
/// </param>
/// <param name="Q">
/// Quality factor, and a restatement of <paramref name="Width"/> rather than an
/// independent figure: every band the headset stores holds 2, which is what half the
/// centre frequency works out to. Carried through a write untouched.
/// </param>
public readonly record struct MaxwellEqBand(
    ushort Tag,
    decimal Frequency,
    decimal Gain,
    decimal Width,
    decimal Q)
{
    /// <summary>Bytes one band occupies on the wire.</summary>
    public const int WireSize = 18;

    /// <summary>Hundredths, the unit every figure in the record is counted in.</summary>
    private const decimal Scale = 100m;

    /// <summary>Decode one 18-byte record.</summary>
    public static MaxwellEqBand FromWire(ReadOnlySpan<byte> wire)
    {
        if (wire.Length < WireSize)
        {
            throw new ArgumentException($"A band record is {WireSize} bytes.", nameof(wire));
        }

        static int Word(ReadOnlySpan<byte> at) => at[0] | (at[1] << 8) | (at[2] << 16) | (at[3] << 24);

        return new MaxwellEqBand(
            Tag: (ushort)(wire[0] | (wire[1] << 8)),
            Frequency: Word(wire[2..]) / Scale,
            Gain: Word(wire[6..]) / Scale,
            Width: Word(wire[10..]) / Scale,
            Q: Word(wire[14..]) / Scale);
    }

    /// <summary>Encode this band back into its 18 wire bytes.</summary>
    public void WriteTo(Span<byte> wire)
    {
        if (wire.Length < WireSize)
        {
            throw new ArgumentException($"A band record is {WireSize} bytes.", nameof(wire));
        }

        static void Word(Span<byte> at, int value)
        {
            at[0] = (byte)value;
            at[1] = (byte)(value >> 8);
            at[2] = (byte)(value >> 16);
            at[3] = (byte)(value >> 24);
        }

        wire[0] = (byte)Tag;
        wire[1] = (byte)(Tag >> 8);
        Word(wire[2..], (int)decimal.Round(Frequency * Scale));
        Word(wire[6..], (int)decimal.Round(Gain * Scale));
        Word(wire[10..], (int)decimal.Round(Width * Scale));
        Word(wire[14..], (int)decimal.Round(Q * Scale));
    }

    public override string ToString() =>
        $"{Frequency:0.##} Hz  {Gain:+0.##;-0.##;0} dB  width {Width:0.##} Hz  Q {Q:0.##}";
}
