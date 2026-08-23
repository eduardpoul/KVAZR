namespace Maxwell.Race;

/// <summary>
/// One user equaliser slot as it comes off the wire: a short header the headset owns,
/// then the bands themselves.
///
/// The header is carried rather than interpreted. Its first byte selects the sample rate
/// the coefficients were generated against — Audeze HQ indexes 44100, 48000, 88200 and
/// 96000 off it — and the rest has never differed between slots. Anything written back
/// must return it unchanged, so it is kept whole instead of split into fields we would
/// only be guessing at.
/// </summary>
public sealed record MaxwellEqSlot(byte Slot, byte[] Header, IReadOnlyList<MaxwellEqBand> Bands)
{
    /// <summary>Bytes of header ahead of the first band.</summary>
    public const int HeaderSize = 5;

    /// <summary>
    /// Decodes a slot payload, or null when the blob is not a whole number of bands —
    /// which is how an empty slot and a truncated reply both present.
    /// </summary>
    public static MaxwellEqSlot? Parse(byte slot, ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderSize) return null;

        int span = blob.Length - HeaderSize;
        if (span == 0 || span % MaxwellEqBand.WireSize != 0) return null;

        var bands = new MaxwellEqBand[span / MaxwellEqBand.WireSize];
        for (int i = 0; i < bands.Length; i++)
        {
            bands[i] = MaxwellEqBand.FromWire(
                blob.Slice(HeaderSize + i * MaxwellEqBand.WireSize, MaxwellEqBand.WireSize));
        }

        return new MaxwellEqSlot(slot, blob[..HeaderSize].ToArray(), bands);
    }

    /// <summary>Re-encodes header and bands into the payload a write expects.</summary>
    public byte[] ToWire()
    {
        byte[] wire = new byte[HeaderSize + (Bands.Count * MaxwellEqBand.WireSize)];
        Header.AsSpan(0, HeaderSize).CopyTo(wire);

        for (int i = 0; i < Bands.Count; i++)
        {
            Bands[i].WriteTo(wire.AsSpan(HeaderSize + (i * MaxwellEqBand.WireSize)));
        }

        return wire;
    }

    /// <summary>Sample rate the slot's coefficients were built for, or 0 when unrecognised.</summary>
    public int SampleRate => Header.Length > 0
        ? Header[0] switch { 1 => 44100, 2 => 48000, 5 => 88200, 6 => 96000, _ => 0 }
        : 0;
}
