namespace Kvazr.Core.Model;

/// <summary>
/// The preset file Audeze HQ writes when the wearer exports a custom equaliser.
///
/// It is as small as a format gets: one signed byte of gain per band, in the band order
/// of <see cref="EqPresets.Bands"/>, and nothing else — no header, no name, no sample
/// rate, no length. A ten-byte file is the whole preset.
///
/// Worth knowing what this is not. It is the graphic curve the wearer drew, not what the
/// headset plays from; the headset runs biquad coefficients derived from it. So a file
/// like this can be shown and edited, and it says nothing about whether the headset can
/// be made to play it.
/// </summary>
public static class EqPresetFile
{
    /// <summary>Bytes an exported preset occupies — one per band.</summary>
    public static int Size => EqPresets.Bands.Count;

    /// <summary>
    /// Reads an exported curve, or null when the file is not one. Gains are signed:
    /// <c>0xF4</c> is −12 dB, and reading them unsigned turns every cut into a large
    /// boost, which is the kind of mistake that looks like a working import.
    /// </summary>
    public static IReadOnlyList<int>? Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length != Size) return null;

        int[] curve = new int[Size];
        for (int i = 0; i < Size; i++) curve[i] = (sbyte)file[i];

        return curve;
    }

    /// <summary>Writes a curve back out in the same form.</summary>
    public static byte[] Write(IReadOnlyList<int> curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        if (curve.Count != Size)
        {
            throw new ArgumentException($"A preset holds {Size} bands.", nameof(curve));
        }

        byte[] file = new byte[Size];
        for (int i = 0; i < Size; i++) file[i] = unchecked((byte)(sbyte)curve[i]);

        return file;
    }
}
