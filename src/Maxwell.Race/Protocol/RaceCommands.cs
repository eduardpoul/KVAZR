namespace Maxwell.Race.Protocol;

/// <summary>
/// RACE command identifiers. Verified against the constants compiled into Audeze HQ,
/// which is the authority here — the community protocol notes name several of these
/// wrongly.
/// </summary>
public static class RaceCommands
{
    /// <summary>Battery level. Takes no sub-command.</summary>
    public const ushort Battery = 0x0CD6;

    /// <summary>Register and NVDM read namespace.</summary>
    public const ushort Read = 0x0901;

    /// <summary>Register and NVDM write namespace.</summary>
    public const ushort Write = 0x0900;

    /// <summary>Undocumented bulk status struct — one round trip for most of the state.</summary>
    public const ushort Status = 0x0910;

    /// <summary>ChatMix, sidetone state and auto power-off read namespace.</summary>
    public const ushort ChatMixRead = 0x2C83;

    /// <summary>Matching write namespace for <see cref="ChatMixRead"/>.</summary>
    public const ushort ChatMixWrite = 0x2C82;

    /// <summary>Four-byte flash and SRAM read. Answered locally by a dongle.</summary>
    public const ushort MemoryRead = 0x1680;

    /// <summary>Highest gain the driver tolerates. Above this the coil can be damaged.</summary>
    public const int MaxGain = 150;

    /// <summary>0 = all game, 10 = balanced, 20 = all chat.</summary>
    public const byte ChatMixBalanced = 10;

    public const byte ChatMixMax = 20;

    /// <summary>
    /// Firmware-reserved gain codes. 0x88 is the boot default and 0x8E is silently
    /// rejected, so neither can be written nor restored.
    /// </summary>
    public static bool IsReservedGain(int value) => value is 0x88 or 0x8E;

    /// <summary>
    /// Commands that destroy user state. Both are issued in the READ-shaped form, so a
    /// blind sweep of the 0x1101 namespace — which the public notes label "battery/sync"
    /// — wipes the headset. Never send these without explicit, confirmed intent.
    /// </summary>
    public static readonly (ushort Command, byte Sub)[] Destructive =
    [
        (0x1101, 0x95),   // factory reset
        (0x1101, 0xAA),   // re-pairing
    ];

    public static bool IsDestructive(ushort command, byte sub) =>
        Destructive.Any(d => d.Command == command && d.Sub == sub);
}
