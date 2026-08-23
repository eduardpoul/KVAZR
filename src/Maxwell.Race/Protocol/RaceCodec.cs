namespace Maxwell.Race.Protocol;

/// <summary>
/// Pure RACE framing: bytes in, bytes out. No device access, no state, no platform
/// dependency — everything here is unit-testable against captured traffic, which is
/// the point. A framing mistake in this class is silent on the wire: the device
/// simply never answers, which is indistinguishable from a hardware fault.
/// </summary>
public static class RaceCodec
{
    /// <summary>HID output report ID carrying RACE commands.</summary>
    public const byte OutputReportId = 0x06;

    /// <summary>HID input report ID carrying RACE replies.</summary>
    public const byte InputReportId = 0x07;

    /// <summary>Start-of-packet byte for both requests (05 5A) and replies (05 5B / 05 5D).</summary>
    public const byte Marker = 0x05;

    /// <summary>Byte 2 of an input report: set when the buffer holds no new data.</summary>
    public const byte StaleFlag = 0x80;

    /// <summary>
    /// 1-byte length framing, used by the 0x09xx and 0x2Cxx families:
    /// <c>06 LEN 80 05 5A ...</c>. The 05 5A magic must land on byte 3 of the report —
    /// the chip parses at a fixed offset and silently drops anything misaligned.
    /// </summary>
    public static byte[] BuildShortReport(ReadOnlySpan<byte> body, int reportLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reportLength, body.Length + 2);

        byte[] report = new byte[reportLength];
        report[0] = OutputReportId;
        report[1] = (byte)body.Length;
        body.CopyTo(report.AsSpan(2));
        return report;
    }

    /// <summary>
    /// Split a body too long for one report into the chunks the chip reassembles.
    ///
    /// There is no sequence number and no continuation bit: the device simply
    /// concatenates what arrives, so the chunks must go out in order and nothing may be
    /// interleaved between them. Each report is <c>06 &lt;count&gt; &lt;flags&gt;</c> and
    /// then up to <see cref="MaxChunk"/> bytes, with the flags byte repeated on every
    /// chunk rather than only the first.
    ///
    /// Note the length byte counts the chunk alone, without the flags byte — unlike
    /// <see cref="BuildShortReport"/>, whose caller folds the flag into the body and so
    /// declares one more than it sends. That works only because the report is zero-filled
    /// and the extra byte is a trailing zero the device ignores.
    /// </summary>
    /// <param name="flags">Transport byte, 0x80 for the paths we speak to.</param>
    /// <param name="body">The RACE frame itself, starting at its 05 5A magic.</param>
    public static List<byte[]> BuildChunkedReports(byte flags, ReadOnlySpan<byte> body, int reportLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reportLength, MaxChunk + 3);

        var reports = new List<byte[]>();
        for (int sent = 0; sent < body.Length; sent += MaxChunk)
        {
            int take = Math.Min(MaxChunk, body.Length - sent);

            byte[] report = new byte[reportLength];
            report[0] = OutputReportId;
            report[1] = (byte)take;
            report[2] = flags;
            body.Slice(sent, take).CopyTo(report.AsSpan(3));
            reports.Add(report);
        }

        return reports;
    }

    /// <summary>
    /// Payload bytes one report carries: the report is 62 long and three of those are
    /// the id, the count and the flags.
    /// </summary>
    public const int MaxChunk = 59;

    /// <summary>
    /// 2-byte length framing, used by 0x1680 whose body already starts with 05 5A.
    /// The magic still lands on byte 3, just via a wider length field.
    /// </summary>
    public static byte[] BuildLongReport(ReadOnlySpan<byte> body, int reportLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reportLength, body.Length + 3);

        byte[] report = new byte[reportLength];
        report[0] = OutputReportId;
        report[1] = (byte)(body.Length & 0xFF);
        report[2] = (byte)(body.Length >> 8);
        body.CopyTo(report.AsSpan(3));
        return report;
    }

    /// <summary>
    /// True when the report carries data the device has not returned before.
    /// Byte 1 is the length of new data and byte 2 is flags — this is NOT a 16-bit
    /// little-endian length, and reading it as one makes idle buffers look like
    /// 32 KB of payload while hiding the staleness of a repeated reply.
    /// </summary>
    public static bool IsFresh(ReadOnlySpan<byte> report) =>
        report.Length >= 3 && report[1] > 0 && (report[2] & StaleFlag) == 0;

    /// <summary>
    /// Split a raw input report into RACE packets. Layout per packet:
    /// <c>05 &lt;kind&gt; &lt;len:u16&gt; &lt;cmd:u16&gt; &lt;payload&gt;</c>, where len counts the command.
    /// When byte 1 is zero the device is replaying its previous buffer, so the whole
    /// report is scanned instead of trusting the length.
    /// </summary>
    public static List<RacePacket> ParsePackets(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var packets = new List<RacePacket>();
        if (report.Length < 9) return packets;

        int declared = report[1];
        int end = declared > 0 && 3 + declared <= report.Length ? 3 + declared : report.Length;

        for (int i = 3; i + 6 <= end; i++)
        {
            if (report[i] != Marker) continue;

            byte kind = report[i + 1];
            if (kind != RacePacket.Reply && kind != RacePacket.Event) continue;

            int length = report[i + 2] | (report[i + 3] << 8);
            if (length < 2 || i + 4 + length > end) continue;

            ushort command = (ushort)(report[i + 4] | (report[i + 5] << 8));
            byte[] payload = report[(i + 6)..(i + 4 + length)];

            packets.Add(new RacePacket(kind, command, payload, i));
            i += 3 + length;
        }

        return packets;
    }

    /// <summary>Little-endian 32-bit address, as the 0x1680 memory read expects it.</summary>
    public static byte[] AddressBytes(uint address) =>
    [
        (byte)address,
        (byte)(address >> 8),
        (byte)(address >> 16),
        (byte)(address >> 24),
    ];

    /// <summary>Body of a 0x1680 four-byte memory read.</summary>
    public static byte[] BuildMemoryReadBody(uint address)
    {
        byte[] address_ = AddressBytes(address);
        return
        [
            0x05, 0x5A, 0x08, 0x00, 0x80, 0x16, 0x00, 0x00,
            address_[0], address_[1], address_[2], address_[3],
        ];
    }

    /// <summary>
    /// Scan a 0x1680 response stream for the reply marker followed by the echoed
    /// address and its four data bytes. The reply is not framed like other packets,
    /// so it is located by matching the address we asked for.
    /// </summary>
    public static bool TryExtractWord(IReadOnlyList<byte> stream, uint address, out byte[]? word)
    {
        ArgumentNullException.ThrowIfNull(stream);

        word = null;
        byte[] wanted = AddressBytes(address);

        for (int i = 0; i + 8 <= stream.Count; i++)
        {
            if (stream[i] != wanted[0] || stream[i + 1] != wanted[1]
                || stream[i + 2] != wanted[2] || stream[i + 3] != wanted[3]) continue;

            bool marked = false;
            for (int j = Math.Max(0, i - 16); j < i; j++)
            {
                if (stream[j] == Marker && j + 1 < i && stream[j + 1] == RacePacket.Reply)
                {
                    marked = true;
                    break;
                }
            }

            if (!marked) continue;

            word = [stream[i + 4], stream[i + 5], stream[i + 6], stream[i + 7]];
            return true;
        }

        return false;
    }

    /// <summary>Parse a NUL-terminated printable ASCII version string out of a flash byte run.</summary>
    public static string? ParseVersionString(IReadOnlyList<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var text = new System.Text.StringBuilder();
        foreach (byte c in bytes)
        {
            if (c == 0) break;
            if (c is < 0x20 or > 0x7E) return null;
            text.Append((char)c);
        }

        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(text.ToString(), @"\d+\.\d+\.\d+\.\d+");

        return match.Success ? match.Value : null;
    }
}
