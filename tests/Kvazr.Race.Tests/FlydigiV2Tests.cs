using Kvazr.Race.Protocol;
using Xunit;

namespace Kvazr.Race.Tests;

/// <summary>
/// The Flydigi get-info exchange, against the answer an Apex 5 gave through its dongle on
/// this desk, and against the states SDL's driver reads out of the same byte.
/// </summary>
public sealed class FlydigiV2Tests
{
    /// <summary>Captured from the pad: wireless, on battery, top fifth.</summary>
    private static readonly byte[] Captured = Convert.FromHexString(
        "045AA5010100850200000000054501007045213145290000012800001131" + "1F00");

    private static byte[] WithCharge(byte charge)
    {
        byte[] report = (byte[])Captured.Clone();
        report[12] = charge;
        return report;
    }

    [Fact]
    public void TheQuestion_IsACommandReportWithTheMagic()
    {
        byte[] question = FlydigiV2.GetInfo(32);

        Assert.Equal(32, question.Length);
        Assert.Equal(new byte[] { 0x03, 0x5A, 0xA5, 0x01, 0x02, 0x00 }, question[..6]);
    }

    [Fact]
    public void TheCapturedAnswer_IsAnApex5OnItsRadio_AtTheTopFifth()
    {
        FlydigiV2.Info? info = FlydigiV2.ReadInfo(Captured);

        Assert.NotNull(info);
        Assert.Equal(133, info.Device);
        Assert.Equal(2, info.Link);
        Assert.Equal(new FlydigiV2.Charge(100, IsCharging: false), info.Battery);
    }

    [Theory]
    [InlineData(0x03, 60, false)]
    [InlineData(0x13, 60, true)]
    [InlineData(0x21, 100, true)]
    public void TheChargeByte_CarriesStateAndLevel(byte charge, int percent, bool charging)
    {
        Assert.Equal(
            new FlydigiV2.Charge(percent, charging),
            FlydigiV2.ReadInfo(WithCharge(charge))?.Battery);
    }

    /// <summary>A state nobody has described is no reading, not a guess.</summary>
    [Fact]
    public void AnUnknownState_IsNoCharge()
    {
        Assert.Null(FlydigiV2.ReadInfo(WithCharge(0x35))?.Battery);
    }

    /// <summary>A level past five would read past a hundred; it is held at the top.</summary>
    [Fact]
    public void ALevelPastTheTop_IsHeldAtAHundred()
    {
        Assert.Equal(100, FlydigiV2.ReadInfo(WithCharge(0x07))?.Battery?.Percent);
    }

    [Fact]
    public void InputReports_AreNotAnswers()
    {
        byte[] input = Convert.FromHexString("005AA5EF0801003901060038000000000000");

        Assert.False(FlydigiV2.Answers(input, FlydigiV2.GetInfoCommand));
        Assert.Null(FlydigiV2.ReadInfo(input));
    }
}
