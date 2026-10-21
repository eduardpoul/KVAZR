using Kvazr.Race.Protocol;
using Xunit;

namespace Kvazr.Race.Tests;

/// <summary>
/// The VIA wire format, against frames read off a NuPhy Air96 V2 on a cable.
///
/// The one that matters is the last pair. A VIA reply carries no sequence number and
/// every keycode read answers with the same command id, so a predicate that checks the
/// id alone will accept the answer to the previous question — measured, on this
/// keyboard, as a row of keys where two held another row's codes. The echoed arguments
/// are what make an answer identifiable, and they are part of the match.
/// </summary>
public sealed class ViaTests
{
    /// <summary>The real frame: report id, command, layer, row, column, then the keycode.</summary>
    private static byte[] KeycodeReply(byte layer, byte row, byte column, ushort keycode) =>
    [
        0x00, Via.GetKeycode, layer, row, column, (byte)(keycode >> 8), (byte)keycode,
        .. new byte[26],
    ];

    [Fact]
    public void ARequest_LeadsWithTheCommandAfterTheReportId()
    {
        byte[] report = Via.Request(33, Via.GetKeycode, 0x00, 0x01, 0x02);

        Assert.Equal(33, report.Length);
        Assert.Equal(0x00, report[0]);
        Assert.Equal(Via.GetKeycode, report[1]);
        Assert.Equal([0x00, 0x01, 0x02], report[2..5]);
    }

    [Fact]
    public void TheEscapeKeyReadsBackAsTheSpecificationSaysItShould()
    {
        // Row zero, column zero of the Air96 V2, read on the wire: 00 04 00 00 00 00 29.
        Assert.Equal(0x0029, Via.Keycode(KeycodeReply(0, 0, 0, 0x0029)));
    }

    [Theory]
    [InlineData(0x001E)]
    [InlineData(0x7E06)]
    public void AKeycodeIsBigEndian(ushort keycode)
    {
        Assert.Equal(keycode, Via.Keycode(KeycodeReply(0, 1, 1, keycode)));
    }

    [Fact]
    public void AProtocolVersionIsTwoBytes()
    {
        // What the Air96 V2 answered: twelve.
        Assert.Equal(12, Via.ProtocolVersion([0x00, Via.GetProtocolVersion, 0x00, 0x0C, .. new byte[29]]));
    }

    [Fact]
    public void ALayerCountIsOne()
    {
        Assert.Equal(8, Via.LayerCount([0x00, Via.GetLayerCount, 0x08, .. new byte[30]]));
    }

    [Fact]
    public void AReplyEchoingTheCommandAndItsArguments_IsTheAnswer()
    {
        Assert.True(Via.Answers(KeycodeReply(0, 1, 2, 0x001F), Via.GetKeycode, 0x00, 0x01, 0x02));
    }

    /// <summary>
    /// And the previous question's answer is not. This is the whole reason the arguments
    /// are matched: both frames are a keycode read, both echo 0x04, and only one of them
    /// was asked for.
    /// </summary>
    [Fact]
    public void ThePreviousQuestionsAnswer_IsNotAcceptedForThisOne()
    {
        Assert.False(Via.Answers(KeycodeReply(0, 1, 1, 0x001E), Via.GetKeycode, 0x00, 0x01, 0x02));
    }

    [Fact]
    public void AKeyboardThatDeclines_HasStillAnswered()
    {
        byte[] declined = [0x00, Via.Unhandled, .. new byte[31]];

        Assert.True(Via.Answers(declined, Via.GetLayerCount));
        Assert.True(Via.Declined(declined));

        // And a declined command is not a number, however the bytes fall.
        Assert.Null(Via.LayerCount(declined));
    }

    [Fact]
    public void SilenceIsNotAZero()
    {
        Assert.Null(Via.LayerCount(null));
        Assert.Null(Via.Keycode([0x00, Via.GetKeycode]));
    }
}

/// <summary>
/// Colour, between the keyboard's hue-and-saturation and the picker's packed RGB.
///
/// QMK puts hue on 0..255 rather than the 0..360 everybody writes about, which is the
/// mistake this file exists to prevent: a conversion written from the usual formula
/// lands red where green belongs and is wrong by a third of the wheel.
/// </summary>
public sealed class ViaColourTests
{
    /// <summary>
    /// Which channel wins, rather than an exact triple. QMK divides the wheel into six
    /// regions of 43, which overshoots 255 slightly, so the thirds do not land on exact
    /// primaries — and pinning them to exact values would be testing the arithmetic's
    /// rounding rather than the thing that matters, which is that a third of the wheel
    /// is a third of the wheel.
    /// </summary>
    [Theory]
    [InlineData(0, 16, 8)]      // red:   red channel wins
    [InlineData(85, 8, 0)]      // green
    [InlineData(170, 0, 16)]    // blue
    public void ThePrimaries_LandWhereTheyShould(byte hue, int winner, int loser)
    {
        int rgb = Via.Rgb(hue, 255);

        int high = (rgb >> winner) & 0xFF;
        int low = (rgb >> loser) & 0xFF;

        Assert.True(high == 255, $"тон {hue} дал {high} вместо 255 в старшем канале");
        Assert.True(low < 8, $"тон {hue} дал {low} в канале, который должен молчать");
    }

    [Fact]
    public void FullRed_IsExactlyRed()
    {
        Assert.Equal(0xFF0000, Via.Rgb(0, 255));
    }

    /// <summary>
    /// Unsaturated is white, not black — black would mean the swatch had swallowed the
    /// brightness row.
    ///
    /// White here is 0xFFFEFE rather than 0xFFFFFF, and that is not a defect to correct:
    /// 255 × 255 ÷ 256 is 254, and the keyboard's own lights run the same integer
    /// arithmetic. Rounding it up here would make the swatch and the keys disagree by a
    /// step, which is worse than a step of white nobody can see.
    /// </summary>
    [Fact]
    public void NoSaturation_IsWhiteRatherThanBlack()
    {
        int white = Via.Rgb(0, 0);

        Assert.True(((white >> 16) & 0xFF) >= 254, $"красный канал {(white >> 16) & 0xFF}");
        Assert.True(((white >> 8) & 0xFF) >= 254, $"зелёный канал {(white >> 8) & 0xFF}");
        Assert.True((white & 0xFF) >= 254, $"синий канал {white & 0xFF}");
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(85, 255)]
    [InlineData(170, 255)]
    [InlineData(200, 128)]
    public void AColourSurvivesTheRoundTrip(byte hue, byte saturation)
    {
        (byte back, byte sat) = Via.Hsv(Via.Rgb(hue, saturation));

        Assert.True(Math.Abs(back - hue) <= 2, $"тон {hue} вернулся как {back}");
        Assert.True(Math.Abs(sat - saturation) <= 2, $"насыщенность {saturation} вернулась как {sat}");
    }

    [Fact]
    public void WhiteHasNoHueToSpeakOf()
    {
        Assert.Equal((0, 0), Via.Hsv(0xFFFFFF));
    }
}
