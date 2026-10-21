using Kvazr.Race;
using Kvazr.Race.Protocol;
using Xunit;

namespace Kvazr.Race.Tests;

/// <summary>
/// The equaliser record layout, as lifted from Audeze HQ.
///
/// The scale is the part worth pinning down: every figure crosses the wire as a count of
/// hundredths, and Audeze HQ multiplies by 0.01 before handing the band to its
/// coefficient generator. Reading these as whole units puts every band a hundred times
/// too high, which is silent — the curve is simply wrong.
/// </summary>
public sealed class MaxwellEqBandTests
{
    /// <summary>1 kHz, +3 dB, Q 1.41 — an ordinary bell, written out by hand.</summary>
    private static byte[] Bell() =>
    [
        0x01, 0x00,                     // tag
        0xA0, 0x86, 0x01, 0x00,         // 100000 -> 1000.00 Hz
        0x2C, 0x01, 0x00, 0x00,         // 300    ->    3.00 dB
        0x50, 0xC3, 0x00, 0x00,         // 50000  ->  500.00 Hz wide
        0xC8, 0x00, 0x00, 0x00,         // 200    ->    2.00 Q
    ];

    [Fact]
    public void FromWire_ReadsHundredths()
    {
        MaxwellEqBand band = MaxwellEqBand.FromWire(Bell());

        Assert.Equal(1u, band.Tag);
        Assert.Equal(1000.00m, band.Frequency);
        Assert.Equal(3.00m, band.Gain);
        Assert.Equal(500.00m, band.Width);
        Assert.Equal(2.00m, band.Q);
    }

    /// <summary>A cut is a negative count, so the word has to be read signed.</summary>
    [Fact]
    public void FromWire_ReadsNegativeGain()
    {
        byte[] wire = Bell();
        wire[6] = 0xD4;
        wire[7] = 0xFE;
        wire[8] = 0xFF;
        wire[9] = 0xFF;   // -300

        Assert.Equal(-3.00m, MaxwellEqBand.FromWire(wire).Gain);
    }

    [Fact]
    public void WriteTo_RoundTrips()
    {
        byte[] wire = Bell();

        byte[] again = new byte[MaxwellEqBand.WireSize];
        MaxwellEqBand.FromWire(wire).WriteTo(again);

        Assert.Equal(wire, again);
    }

    [Fact]
    public void FromWire_RejectsShortRecord() =>
        Assert.Throws<ArgumentException>(() => MaxwellEqBand.FromWire(new byte[MaxwellEqBand.WireSize - 1]));
}

public sealed class MaxwellEqSlotTests
{
    private static byte[] Blob(int bands)
    {
        byte[] blob = new byte[MaxwellEqSlot.HeaderSize + (bands * MaxwellEqBand.WireSize)];
        blob[0] = 2;   // 48 kHz
        return blob;
    }

    [Fact]
    public void Parse_SplitsHeaderFromBands()
    {
        MaxwellEqSlot? slot = MaxwellEqSlot.Parse(7, Blob(10));

        Assert.NotNull(slot);
        Assert.Equal(7, slot.Slot);
        Assert.Equal(MaxwellEqSlot.HeaderSize, slot.Header.Length);
        Assert.Equal(10, slot.Bands.Count);
        Assert.Equal(48000, slot.SampleRate);
    }

    /// <summary>
    /// Audeze HQ refuses the blob unless what follows the header divides exactly into
    /// band records, and so do we: a payload that nearly fits means the layout is wrong,
    /// and guessing at the remainder would invent a band the headset never sent.
    /// </summary>
    [Fact]
    public void Parse_RejectsPartialBand()
    {
        byte[] blob = Blob(3);
        Assert.Null(MaxwellEqSlot.Parse(7, blob.AsSpan(0, blob.Length - 4)));
    }

    [Fact]
    public void Parse_RejectsHeaderOnly() => Assert.Null(MaxwellEqSlot.Parse(7, Blob(0)));

    [Fact]
    public void ToWire_RoundTrips()
    {
        byte[] blob = Blob(2);
        new MaxwellEqBand(1, 250m, -4.5m, 125m, 2m).WriteTo(blob.AsSpan(MaxwellEqSlot.HeaderSize));
        new MaxwellEqBand(1, 8000m, 2.25m, 4000m, 2m)
            .WriteTo(blob.AsSpan(MaxwellEqSlot.HeaderSize + MaxwellEqBand.WireSize));

        MaxwellEqSlot? slot = MaxwellEqSlot.Parse(9, blob);

        Assert.NotNull(slot);
        Assert.Equal(blob, slot.ToWire());
    }

    /// <summary>
    /// The six built-in presets are not stored as bands. Audeze HQ checks the slot before
    /// sending, and so must we — an out-of-range slot names a non-volatile key that holds
    /// something else entirely.
    /// </summary>
    /// <summary>
    /// The equaliser commands number the four user slots 0..3, while the preset index
    /// calls the same four 7..10. Captured from Audeze HQ: editing preset 10 writes the
    /// non-volatile key 0xEF03, not 0xEF0A.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(7, false)]
    [InlineData(10, false)]
    public void UserSlots_AreZeroToThree(int slot, bool expected) =>
        Assert.Equal(expected, RaceCommands.IsUserEqSlot(slot));

    [Theory]
    [InlineData(7, 0)]
    [InlineData(10, 3)]
    [InlineData(1, -1)]
    [InlineData(6, -1)]
    [InlineData(11, -1)]
    public void PresetIndex_MapsOntoTheEqualiserSlot(int preset, int expected) =>
        Assert.Equal(expected, RaceCommands.EqSlotOfPreset(preset));
}

/// <summary>
/// The refusal an unwritten slot gives back. It echoes the slot and the key like a real
/// answer does, so only the leading byte separates "empty" from "here is your curve" —
/// and reading past it would turn the header remainder into invented bands.
/// </summary>
public sealed class MaxwellEqReplyTests
{
    [Fact]
    public void Empty_IsNotOk()
    {
        var reply = new MaxwellEqReply(7, MaxwellEqReply.Empty, null);

        Assert.True(reply.IsEmpty);
        Assert.Null(reply.Bands);
    }

    [Fact]
    public void Ok_CarriesBands()
    {
        byte[] blob = new byte[MaxwellEqSlot.HeaderSize + MaxwellEqBand.WireSize];
        blob[0] = 2;

        var reply = new MaxwellEqReply(7, MaxwellEqReply.Ok, MaxwellEqSlot.Parse(7, blob));

        Assert.False(reply.IsEmpty);
        Assert.NotNull(reply.Bands);
        Assert.Single(reply.Bands.Bands);
    }

    /// <summary>Observed on hardware 2026-08-23: every untouched user slot answers 0xFC.</summary>
    [Fact]
    public void EmptyStatus_IsWhatUntouchedSlotsAnswer() => Assert.Equal(0xFC, MaxwellEqReply.Empty);
}

/// <summary>
/// Voice-prompt volume, measured on hardware 2026-08-23. The register is four bits and
/// the firmware masks rather than rejects, which is what made the control look broken:
/// the frame was right all along and the declared range was not.
/// </summary>
public sealed class VoicePromptRangeTests
{
    [Fact]
    public void Max_IsFourBits() => Assert.Equal(15, MaxwellFeatures.VpLevelMax);

    /// <summary>What the headset actually returned for each value written.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 8)]
    [InlineData(15, 15)]
    [InlineData(16, 0)]
    [InlineData(64, 0)]
    [InlineData(200, 8)]
    [InlineData(255, 15)]
    public void AboveMax_ComesBackMasked(int written, int observed) =>
        Assert.Equal(observed, written & MaxwellFeatures.VpLevelMax);
}

/// <summary>
/// The coefficient wire format, which is settled, as distinct from the filter design,
/// which is not. Everything here was read off a capture of Audeze HQ and cross-checked
/// against its own generator: for a flat curve our blob matches the vendor's in 98 of 103
/// words, the five apart being the lowest band — where the vendor pulls its poles in for
/// numerical safety — and one rounding step.
///
/// What these tests do NOT claim is that the headset plays the result. It does not: sent
/// to hardware, curves built this way are loud and distorted, so the arrangement of the
/// five values inside a stage is still wrong. The encoding around them is right, and
/// pinning it down keeps that much from having to be found twice.
/// </summary>
public sealed class MaxwellEqCoefficientTests
{
    private static readonly int[] Bands = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Reverses the packing, so a value can be checked as it will arrive.</summary>
    private static double Decode(ushort[] words, int at)
    {
        int raw = ((words[at] << 8) | (words[at + 1] >> 8)) & 0xFFFFFF;
        if ((raw & 0x800000) != 0) raw -= 1 << 24;
        return raw / 8388608.0;
    }

    [Fact]
    public void Build_HasAStagePerBandPlusTheHeader()
    {
        ushort[] words = MaxwellEqCoefficients.Build(Bands, new int[10], 44100.0);

        // Three words of header, then ten words per stage.
        Assert.Equal(3 + (Bands.Length * 10), words.Length);
        Assert.Equal(Bands.Length, words[0]);
    }

    /// <summary>
    /// A band left alone must come out as a pass-through — numerator equal to denominator,
    /// which cancels exactly because both are the same rounded numbers.
    /// </summary>
    [Fact]
    public void UntouchedBand_IsAPassThrough()
    {
        ushort[] words = MaxwellEqCoefficients.Build(Bands, new int[10], 44100.0);

        for (int stage = 0; stage < Bands.Length; stage++)
        {
            int at = 3 + (stage * 10);
            Assert.Equal(Decode(words, at), Decode(words, at + 4), 9);
            Assert.Equal(Decode(words, at + 2), Decode(words, at + 6), 9);
        }
    }

    /// <summary>
    /// The packing itself: a signed twenty-four bit fraction split so that the third byte
    /// of every four is zero. Reading it as a plain 32-bit word gives nonsense, which is
    /// why the pattern is worth a test of its own.
    /// </summary>
    [Fact]
    public void Values_AreTwentyFourBitFractionsWithAnEmptyThirdByte()
    {
        ushort[] words = MaxwellEqCoefficients.Build(Bands, [0, 6, -6, 0, 0, 0, 0, 0, 0, 0], 44100.0);

        for (int at = 3; at + 1 < words.Length; at += 2)
        {
            Assert.Equal(0, words[at + 1] & 0xFF);
            Assert.InRange(Decode(words, at), -1.0, 1.0);
        }
    }

    /// <summary>Every rate the headset runs at gets its own block, and all of them go together.</summary>
    [Fact]
    public void Blob_CarriesOneBlockPerSampleRate()
    {
        byte[] blob = MaxwellEqCoefficients.Blob(Bands, new int[10]);

        Assert.Equal(MaxwellEqCoefficients.Rates.Count, blob[0]);

        int at = 4;
        foreach ((int code, double _) in MaxwellEqCoefficients.Rates)
        {
            Assert.Equal(code, blob[at] | (blob[at + 1] << 8));

            int count = blob[at + 2] | (blob[at + 3] << 8);
            Assert.Equal(3 + (Bands.Length * 10), count);
            at += 4 + (count * 2);
        }

        Assert.Equal(blob.Length, at);
    }

    /// <summary>Matches the 844 bytes the capture shows Audeze HQ sending.</summary>
    [Fact]
    public void Blob_IsTheSizeTheCaptureShows() =>
        Assert.Equal(844, MaxwellEqCoefficients.Blob(Bands, new int[10]).Length);
}
