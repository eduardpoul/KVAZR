namespace Kvazr.Race.Protocol;

/// <summary>
/// Flydigi's second protocol generation, the one question this application asks of it:
/// what the pad is, how it is linked, and how charged.
///
/// Not reverse-engineered here. SDL carries a driver for these pads and reads the Apex 5's
/// battery with exactly this exchange, and its bytes were then checked against the pad on
/// this desk: asked through its dongle, it answered as device 133 — one of the four ids
/// SDL files under Apex 5 — on its wireless link, with a charge byte of 0x05.
///
/// The question is a command report: <c>03 5A A5 01 02 00</c>, report id first. The answer
/// arrives under report id 0x04 and repeats <c>5A A5 01</c>; everything below is counted
/// from that first magic byte, as SDL counts it.
///
/// Only this. The same channel takes the pad over (0x1C), sets its state and drives its
/// motors; none of that is built, because a charge reading needs none of it and taking
/// the pad over changes what it sends and to whom.
/// </summary>
public static class FlydigiV2
{
    public const ushort Vendor = 0x37D7;

    /// <summary>The vendor collection the commands go to: 32 bytes each way on the Apex 5 dongle.</summary>
    public const ushort VendorPage = 0xFFA0;

    public const byte CommandReport = 0x03;

    public const byte Magic1 = 0x5A;

    public const byte Magic2 = 0xA5;

    public const byte GetInfoCommand = 0x01;

    /// <summary>A charge reading: how full, and whether power is coming in.</summary>
    public sealed record Charge(int Percent, bool IsCharging);

    /// <summary>What get-info says about the pad.</summary>
    public sealed record Info(int Device, int Link, Charge? Battery);

    /// <summary>The get-info question, at the length the collection declares.</summary>
    public static byte[] GetInfo(int size)
    {
        byte[] report = new byte[Math.Max(size, 6)];

        report[0] = CommandReport;
        report[1] = Magic1;
        report[2] = Magic2;
        report[3] = GetInfoCommand;
        report[4] = 0x02;

        return report;
    }

    /// <summary>The body of a report from its first magic byte, with or without a report id before it.</summary>
    public static ReadOnlySpan<byte> Body(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);

        for (int at = 0; at <= 1 && at + 2 < report.Length; at++)
        {
            if (report[at] == Magic1 && report[at + 1] == Magic2) return report.AsSpan(at);
        }

        return [];
    }

    /// <summary>Whether a report answers the given command, rather than being input or anything else.</summary>
    public static bool Answers(byte[] report, byte command)
    {
        ReadOnlySpan<byte> body = Body(report);

        return body.Length > 2 && body[2] == command;
    }

    /// <summary>
    /// Reads a get-info answer. Null for anything that is not one.
    ///
    /// The charge is one byte: the state in its high nibble — 0 on battery, 1 charging,
    /// 2 charged — and the level in its low one, in fifths. Coarse, and that is the pad's
    /// resolution rather than a loss here: a reading of 100 means the top fifth.
    /// </summary>
    public static Info? ReadInfo(byte[] report)
    {
        ReadOnlySpan<byte> body = Body(report);
        if (body.Length < 12 || body[2] != GetInfoCommand) return null;

        int state = (body[11] >> 4) & 0x0F;
        int level = Math.Min(body[11] & 0x0F, 5) * 20;

        // "Charged" means on the cable and full, which is still power coming in: the same
        // reading HID++ gives for a charge complete, and the one the page shows as such.
        Charge? battery = state switch
        {
            0 => new Charge(level, IsCharging: false),
            1 => new Charge(level, IsCharging: true),
            2 => new Charge(100, IsCharging: true),
            _ => null,
        };

        return new Info(body[5], body[6], battery);
    }
}
