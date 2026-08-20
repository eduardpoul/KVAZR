namespace Maxwell.Race.Tests;

/// <summary>
/// Real HID reports captured from an Audeze Maxwell on firmware v1.0.1.74 over USB-C.
/// These are the ground truth for the codec: every framing rule in the protocol was
/// derived from these bytes, so a change that breaks them is a regression, not a
/// difference of opinion.
/// </summary>
internal static class CapturedTraffic
{
    /// <summary>
    /// Reply to a master-gain read (0x0901 sub 0x28). One 0x5B packet carrying the value.
    /// Report byte 1 is 0x0A (ten bytes of new data) and byte 2 is 0x00 (fresh).
    /// The tail past the packet is the stale remains of an earlier battery reply.
    /// </summary>
    public const string FreshGainReply =
        "070A00055B060001092800009300D60C0000470000802C1C" +
        "0000000000000000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The same content replayed with byte 1 = 0x00 and byte 2 = 0x80: no new data.</summary>
    public const string StaleGainReply =
        "070080055B060001092800009300D60C0000470000802C1C" +
        "0000000000000000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Reply to a battery read (0x0CD6). Two packets: a short 0x5B acknowledgement
    /// followed by the 0x5D event whose last payload byte is the level (0x47 = 71%).
    /// </summary>
    public const string BatteryReply =
        "070080055B0300D60C00055D0500D60C0000470000802C1C" +
        "0000000000000000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>An idle report from a device with nothing to say — what the dongle returns to everything.</summary>
    public const string IdleReport =
        "070080000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000000000000000";

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex);
}
