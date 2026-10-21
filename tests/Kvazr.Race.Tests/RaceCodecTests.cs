using Kvazr.Race.Protocol;
using Xunit;

namespace Kvazr.Race.Tests;

public sealed class RaceCodecFramingTests
{
    private const int ReportLength = 62;

    /// <summary>
    /// The chip parses the RACE packet at a fixed offset: the 05 5A magic must land on
    /// byte 3 of the report. Misalignment is not an error — the device simply never
    /// answers, which is why this invariant gets its own test.
    /// </summary>
    [Fact]
    public void ShortFraming_PutsMagicOnByteThree()
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];

        byte[] report = RaceCodec.BuildShortReport(body, ReportLength);

        Assert.Equal(0x05, report[3]);
        Assert.Equal(0x5A, report[4]);
    }

    [Fact]
    public void LongFraming_PutsMagicOnByteThree()
    {
        byte[] report = RaceCodec.BuildLongReport(RaceCodec.BuildMemoryReadBody(0x0828EA20), ReportLength);

        Assert.Equal(0x05, report[3]);
        Assert.Equal(0x5A, report[4]);
    }

    /// <summary>Documented battery request: 06 07 80 05 5A 03 00 D6 0C.</summary>
    [Fact]
    public void ShortFraming_MatchesDocumentedBatteryRequest()
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];

        byte[] report = RaceCodec.BuildShortReport(body, ReportLength);

        Assert.Equal("060780055A0300D60C", Convert.ToHexString(report.AsSpan(0, 9)));
    }

    /// <summary>Documented right-gain read: 06 08 80 05 5A 04 00 01 09 2A.</summary>
    [Fact]
    public void ShortFraming_MatchesDocumentedGainRead()
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x09, 0x2A];

        byte[] report = RaceCodec.BuildShortReport(body, ReportLength);

        Assert.Equal("060880055A040001092A", Convert.ToHexString(report.AsSpan(0, 10)));
    }

    [Fact]
    public void ShortFraming_WritesBodyLengthIntoByteOne()
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x09, 0x28];

        byte[] report = RaceCodec.BuildShortReport(body, ReportLength);

        Assert.Equal(RaceCodec.OutputReportId, report[0]);
        Assert.Equal(body.Length, report[1]);
    }

    [Fact]
    public void ShortFraming_RejectsBodyTooLargeForTheReport()
    {
        byte[] body = new byte[ReportLength];

        Assert.Throws<ArgumentOutOfRangeException>(() => RaceCodec.BuildShortReport(body, ReportLength));
    }

    [Fact]
    public void MemoryReadBody_EncodesAddressLittleEndian()
    {
        byte[] body = RaceCodec.BuildMemoryReadBody(0x0828EA20);

        Assert.Equal([0x20, 0xEA, 0x28, 0x08], body.AsSpan(8, 4).ToArray());
    }
}

public sealed class RaceCodecFreshnessTests
{
    /// <summary>
    /// Byte 1 is the new-data length and byte 2 is flags. Reading the pair as a 16-bit
    /// little-endian length — which the public protocol notes imply — makes an idle
    /// buffer look like 32 KB of payload and hides that a reply is a replay.
    /// </summary>
    [Fact]
    public void FreshReport_IsRecognised()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.FreshGainReply);

        Assert.True(RaceCodec.IsFresh(report));
    }

    [Fact]
    public void StaleReport_IsRejected()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.StaleGainReply);

        Assert.False(RaceCodec.IsFresh(report));
    }

    [Fact]
    public void IdleReport_IsNotFresh()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.IdleReport);

        Assert.False(RaceCodec.IsFresh(report));
    }

    [Fact]
    public void ShortBuffer_IsNotFresh()
    {
        Assert.False(RaceCodec.IsFresh([0x07, 0x0A]));
    }

    /// <summary>
    /// The dongle sets byte 2 on every report, so a fresh answer over it carries the
    /// "stale" bit. Taking the bit at its word made every dongle reply a replay.
    /// </summary>
    [Fact]
    public void DongleReply_IsFreshDespiteItsFlag()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.DongleBatteryFresh);

        Assert.Equal(RaceCodec.StaleFlag, report[2]);
        Assert.True(RaceCodec.IsFresh(report));
    }

    [Fact]
    public void DongleReplay_IsNotFresh()
    {
        Assert.False(RaceCodec.IsFresh(CapturedTraffic.Bytes(CapturedTraffic.DongleBatteryReplay)));
    }
}

public sealed class RaceCodecParsingTests
{
    /// <summary>
    /// A 0x0901 read answers with a single 0x5B packet whose payload is
    /// sub, 00, 00, value — the value last.
    /// </summary>
    [Fact]
    public void GainReply_YieldsOnePacketWithTheValueLast()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.FreshGainReply);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        RacePacket packet = Assert.Single(packets);
        Assert.Equal(RaceCommands.Read, packet.Command);
        Assert.Equal(MaxwellFeatures.AmpGain, packet.Sub);
        Assert.Equal(147, packet.Value);
        Assert.True(packet.IsReply);
    }

    /// <summary>
    /// A battery read answers with a short 0x5B acknowledgement followed by a 0x5D
    /// event holding the level. Neither kind can be assumed to carry the payload,
    /// which is why lookups match on the command and a minimum payload length.
    /// </summary>
    [Fact]
    public void BatteryReply_YieldsAcknowledgementThenEventWithTheLevel()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.BatteryReply);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        Assert.Equal(2, packets.Count);

        Assert.True(packets[0].IsReply);
        Assert.Equal(RaceCommands.Battery, packets[0].Command);
        Assert.Single(packets[0].Payload);

        Assert.True(packets[1].IsEvent);
        Assert.Equal(RaceCommands.Battery, packets[1].Command);
        Assert.Equal(71, packets[1].Value);
    }

    /// <summary>The documented "battery sits at report byte 12" rule is wrong for this firmware.</summary>
    [Fact]
    public void BatteryReply_ByteTwelveIsNotTheLevel()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.BatteryReply);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        Assert.NotEqual(report[12], packets[1].Value);
    }

    [Fact]
    public void IdleReport_YieldsNoPackets()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.IdleReport);

        Assert.Empty(RaceCodec.ParsePackets(report));
    }

    [Fact]
    public void UndersizedReport_YieldsNoPackets()
    {
        Assert.Empty(RaceCodec.ParsePackets([0x07, 0x00, 0x80]));
    }

    /// <summary>
    /// A stale buffer still contains the previous answer, so parsing must succeed —
    /// staleness is the caller's decision, not the parser's.
    /// </summary>
    [Fact]
    public void StaleReport_StillParses()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.StaleGainReply);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        Assert.NotEmpty(packets);
        Assert.Equal(147, packets[0].Value);
    }

    /// <summary>A fresh report is read only as far as its count: the old 86% behind it is not news.</summary>
    [Fact]
    public void DongleBatteryReply_YieldsTodaysLevelOnly()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.DongleBatteryFresh);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        Assert.Equal(2, packets.Count);
        Assert.Equal(83, packets[1].Value);
    }

    /// <summary>
    /// A replay is read only as far as its packets run on from the head. Past that is
    /// whatever older answers left behind, and on this desk it was a battery reply
    /// hours old, read as the current level whenever the previous question was another.
    /// </summary>
    [Fact]
    public void Replay_StopsWhereItsHeadEnds()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.DongleStatusReplay);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        RacePacket status = Assert.Single(packets);
        Assert.Equal(RaceCommands.Status, status.Command);
    }

    [Fact]
    public void Replay_KeepsTheAnswerAtItsHead()
    {
        byte[] report = CapturedTraffic.Bytes(CapturedTraffic.DongleBatteryReplay);

        List<RacePacket> packets = RaceCodec.ParsePackets(report);

        Assert.Equal(2, packets.Count);
        Assert.Equal(83, packets[1].Value);
    }

    /// <summary>The 0x2C namespace puts the sub-command second rather than first.</summary>
    [Fact]
    public void ChatMixLayout_ExposesSubCommandFromTheSecondByte()
    {
        var packet = new RacePacket(RacePacket.Reply, RaceCommands.ChatMixRead, [0x00, 0x0B, 0x00, 0x0A], 3);

        Assert.Equal(MaxwellFeatures.ChatMix, packet.Sub2C);
        Assert.Equal(10, packet.Value);
        Assert.NotEqual(MaxwellFeatures.ChatMix, packet.Sub);
    }
}

public sealed class RaceCodecMemoryTests
{
    [Fact]
    public void TryExtractWord_FindsDataAfterTheEchoedAddress()
    {
        const uint address = 0x0828EA20;
        byte[] stream =
        [
            0x05, 0x5B, 0x08, 0x00, 0x80, 0x16, 0x00, 0x00,
            0x20, 0xEA, 0x28, 0x08,
            0x76, 0x31, 0x2E, 0x30,
        ];

        bool found = RaceCodec.TryExtractWord(stream, address, out byte[]? word);

        Assert.True(found);
        Assert.Equal([0x76, 0x31, 0x2E, 0x30], word);
    }

    /// <summary>Without the reply marker ahead of it, a matching address is coincidence, not data.</summary>
    [Fact]
    public void TryExtractWord_IgnoresAddressWithoutReplyMarker()
    {
        const uint address = 0x0828EA20;
        byte[] stream = [0x20, 0xEA, 0x28, 0x08, 0x76, 0x31, 0x2E, 0x30];

        Assert.False(RaceCodec.TryExtractWord(stream, address, out byte[]? word));
        Assert.Null(word);
    }

    [Fact]
    public void ParseVersionString_ExtractsFourPartVersion()
    {
        byte[] bytes = "v1.0.1.74\0"u8.ToArray();

        Assert.Equal("1.0.1.74", RaceCodec.ParseVersionString(bytes));
    }

    [Fact]
    public void ParseVersionString_RejectsNonPrintableRun()
    {
        Assert.Null(RaceCodec.ParseVersionString([0xFF, 0xFE, 0x00]));
    }
}

public sealed class GainSafetyTests
{
    /// <summary>Above 150 the coil can be damaged, so the limit is a hard rail rather than advice.</summary>
    [Fact]
    public void MaxGain_IsOneHundredAndFifty()
    {
        Assert.Equal(150, RaceCommands.MaxGain);
    }

    [Theory]
    [InlineData(0x88)]
    [InlineData(0x8E)]
    public void ReservedGainCodes_AreRejected(int value)
    {
        Assert.True(RaceCommands.IsReservedGain(value));
    }

    [Theory]
    [InlineData(0x93)]
    [InlineData(0x80)]
    [InlineData(150)]
    public void OrdinaryGainValues_AreAllowed(int value)
    {
        Assert.False(RaceCommands.IsReservedGain(value));
    }

    /// <summary>
    /// Factory reset and re-pairing are issued in the READ-shaped form, so a sweep of
    /// the 0x1101 namespace that looks read-only would wipe the device.
    /// </summary>
    [Theory]
    [InlineData((ushort)0x1101, (byte)0x95)]
    [InlineData((ushort)0x1101, (byte)0xAA)]
    public void DestructiveCommands_AreFlagged(ushort command, byte sub)
    {
        Assert.True(RaceCommands.IsDestructive(command, sub));
    }

    [Fact]
    public void OrdinaryReadCommand_IsNotFlaggedDestructive()
    {
        Assert.False(RaceCommands.IsDestructive(RaceCommands.Read, MaxwellFeatures.AmpGain));
    }
}

/// <summary>
/// Multi-report framing, used by anything too long for one report — the equaliser
/// coefficients above all.
///
/// The chip reassembles by concatenation alone: no sequence number, no continuation bit,
/// no total length in the first report. That makes the framing simple and the ordering
/// absolute, and it means a chunk sent with the wrong count silently shifts everything
/// after it rather than being rejected.
/// </summary>
public sealed class RaceCodecChunkingTests
{
    private const int ReportLength = 62;

    private static byte[] Body(int length)
    {
        byte[] body = new byte[length];
        for (int i = 0; i < length; i++) body[i] = (byte)(i & 0xFF);
        return body;
    }

    [Fact]
    public void ShortBody_FitsOneReport()
    {
        List<byte[]> reports = RaceCodec.BuildChunkedReports(0x80, Body(10), ReportLength);

        Assert.Single(reports);
        Assert.Equal(RaceCodec.OutputReportId, reports[0][0]);
        Assert.Equal(10, reports[0][1]);
        Assert.Equal(0x80, reports[0][2]);
    }

    /// <summary>The real case: a ten-band coefficient blob is 219 bytes of body.</summary>
    [Fact]
    public void CoefficientBody_SplitsIntoFour()
    {
        List<byte[]> reports = RaceCodec.BuildChunkedReports(0x80, Body(219), ReportLength);

        Assert.Equal(4, reports.Count);
        Assert.Equal([59, 59, 59, 42], reports.Select(r => (int)r[1]));
    }

    /// <summary>Every chunk carries the flags byte, not just the first.</summary>
    [Fact]
    public void EveryChunk_CarriesTheFlags() =>
        Assert.All(RaceCodec.BuildChunkedReports(0x80, Body(219), ReportLength),
            report => Assert.Equal(0x80, report[2]));

    /// <summary>
    /// Concatenating the chunks must give the body back exactly. This is the invariant
    /// the device relies on and the one a chunking bug breaks invisibly.
    /// </summary>
    [Fact]
    public void Chunks_ReassembleToTheBody()
    {
        byte[] body = Body(219);

        byte[] rejoined = [.. RaceCodec.BuildChunkedReports(0x80, body, ReportLength)
            .SelectMany(report => report.Skip(3).Take(report[1]))];

        Assert.Equal(body, rejoined);
    }

    [Fact]
    public void ExactMultipleOfChunk_DoesNotEmitAnEmptyReport()
    {
        List<byte[]> reports = RaceCodec.BuildChunkedReports(0x80, Body(RaceCodec.MaxChunk * 2), ReportLength);

        Assert.Equal(2, reports.Count);
        Assert.All(reports, r => Assert.Equal(RaceCodec.MaxChunk, r[1]));
    }

    [Fact]
    public void ReportTooShortForAChunk_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RaceCodec.BuildChunkedReports(0x80, Body(10), RaceCodec.MaxChunk + 2));
}
