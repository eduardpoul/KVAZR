namespace Maxwell.Race.Protocol;

/// <summary>
/// One RACE packet inside a HID input report; a report can carry several.
/// 0x5B is the reply to a command and usually carries the value itself. 0x5D is an
/// event the command triggers — a battery read answers with a short 0x5B followed by
/// a 0x5D holding the level, while a 0x0901 read answers with a single 0x5B.
/// So neither kind can be assumed to hold the payload: match on the command instead.
/// </summary>
public sealed record RacePacket(byte Kind, ushort Command, byte[] Payload, int Offset)
{
    public const byte Reply = 0x5B;
    public const byte Event = 0x5D;

    public bool IsReply => Kind == Reply;

    public bool IsEvent => Kind == Event;

    /// <summary>Sub-command for the 0x09xx family, which leads its payload with it.</summary>
    public byte Sub => Payload.Length > 0 ? Payload[0] : (byte)0;

    /// <summary>
    /// Sub-command for the 0x2C namespace, which lays its payload out as
    /// 00 &lt;sub&gt; 00 &lt;value&gt; — the sub-command second rather than first.
    /// </summary>
    public byte Sub2C => Payload.Length > 1 ? Payload[1] : (byte)0;

    /// <summary>Reads put the value last: battery percent, gain, or source state.</summary>
    public byte Value => Payload.Length > 0 ? Payload[^1] : (byte)0;

    public string Hex => Convert.ToHexString(Payload);
}
