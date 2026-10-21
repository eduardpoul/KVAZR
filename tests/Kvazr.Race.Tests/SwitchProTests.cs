using Kvazr.Race.Protocol;

using Xunit;

namespace Kvazr.Race.Tests;

/// <summary>
/// Pinned against the community documentation (dekuNukem et al.) before the pad has
/// ever been plugged in. That is the point: when it arrives, a failure means the pad
/// disagrees with the documentation — not that the bytes were typed wrong that day.
/// </summary>
public sealed class SwitchProTests
{
    [Fact]
    public void ASubcommandFrame_CarriesCounterRumbleAndCommand()
    {
        byte[] frame = SwitchPro.Subcommand(49, counter: 0x13, SwitchPro.SubSetPlayerLights, 0x01);

        Assert.Equal(0x01, frame[0]);

        // The counter is four bits; 0x13 rolls to 0x3.
        Assert.Equal(0x03, frame[1]);

        // The mandatory neutral rumble block sits at 2..9.
        Assert.Equal([0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40], frame[2..10]);

        Assert.Equal(SwitchPro.SubSetPlayerLights, frame[10]);
        Assert.Equal(0x01, frame[11]);
        Assert.Equal(49, frame.Length);
    }

    [Fact]
    public void TheSpiReadRequest_EncodesAddressLittleEndian()
    {
        byte[] frame = SwitchPro.SpiRead(49, 0, SwitchPro.SpiColours, 6);

        Assert.Equal(SwitchPro.SubSpiFlashRead, frame[10]);
        Assert.Equal([0x50, 0x60, 0x00, 0x00, 0x06], frame[11..16]);
    }

    [Theory]
    [InlineData(0x80, 100, false)]
    [InlineData(0x90, 100, true)]
    [InlineData(0x60, 70, false)]
    [InlineData(0x40, 40, false)]
    [InlineData(0x20, 15, false)]
    [InlineData(0x30, 15, true)]
    public void TheBatteryNibble_MapsToTheFourHonestSteps(byte value, int percent, bool charging)
    {
        byte[] report = new byte[13];
        report[0] = SwitchPro.FullInput;
        report[2] = value;

        (int Percent, bool IsCharging)? battery = SwitchPro.Battery(report);

        Assert.NotNull(battery);
        Assert.Equal(percent, battery.Value.Percent);
        Assert.Equal(charging, battery.Value.IsCharging);
    }

    [Fact]
    public void AReplyAnswers_OnlyWithTheAckBitAndMatchingSubcommand()
    {
        byte[] reply = new byte[20];
        reply[0] = SwitchPro.SubcommandReply;
        reply[13] = 0x90;
        reply[14] = SwitchPro.SubDeviceInfo;

        Assert.True(SwitchPro.Answers(reply, SwitchPro.SubDeviceInfo));
        Assert.False(SwitchPro.Answers(reply, SwitchPro.SubSpiFlashRead));

        reply[13] = 0x00;
        Assert.False(SwitchPro.Answers(reply, SwitchPro.SubDeviceInfo));
    }

    [Fact]
    public void TheStickBytes_UnpackAsTwelveBitPairs()
    {
        byte[] report = new byte[12];
        report[0] = SwitchPro.FullInput;

        // Documented packing: X = b0 | (b1 & 0xF) << 8, Y = b1 >> 4 | b2 << 4.
        report[SwitchPro.LeftStickOffset] = 0x34;
        report[SwitchPro.LeftStickOffset + 1] = 0xA2;
        report[SwitchPro.LeftStickOffset + 2] = 0x87;

        (int x, int y) = SwitchPro.Stick(report, SwitchPro.LeftStickOffset);

        Assert.Equal(0x234, x);
        Assert.Equal(0x87A, y);
    }

    [Fact]
    public void TheColourBlock_YieldsBodyAndButtons()
    {
        byte[] reply = new byte[SwitchPro.ReplyDataOffset + 5 + 6];
        reply[0] = SwitchPro.SubcommandReply;

        int at = SwitchPro.ReplyDataOffset + 5;
        reply[at] = 0x32; reply[at + 1] = 0x32; reply[at + 2] = 0x32;
        reply[at + 3] = 0xFF; reply[at + 4] = 0xFF; reply[at + 5] = 0xFF;

        (int body, int buttons) = SwitchPro.Colours(reply)!.Value;

        Assert.Equal(0x323232, body);
        Assert.Equal(0xFFFFFF, buttons);
    }
}
