namespace Kvazr.Race.Protocol;

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

    /// <summary>
    /// Reads one user equaliser slot back as its parametric bands. The payload carries a
    /// non-volatile key rather than a sub-command, so it is framed unlike the register
    /// namespaces: <c>0C 0A &lt;slot&gt; EF E8 03</c> — slot, key, then a sixteen-bit cap
    /// on how many bytes may come back.
    /// </summary>
    public const ushort EqBandsRead = 0x0A0C;

    /// <summary>
    /// Writes the band list of a user slot. Persists the editor's own view of the curve
    /// and not what the headset plays from; see <see cref="EqCoefficientsApply"/>.
    /// </summary>
    public const ushort EqBandsWrite = 0x0A0D;

    /// <summary>Frees the non-volatile key before <see cref="EqBandsWrite"/> refills it.</summary>
    public const ushort EqNvKeyReclaim = 0x0A03;

    /// <summary>
    /// Loads biquad coefficients into the running filter. What the headset actually plays
    /// from — the band list alone changes nothing audible.
    /// </summary>
    public const ushort EqCoefficientsApply = 0x0E03;

    /// <summary>The non-volatile key the equaliser slots live under.</summary>
    public const byte EqNvKey = 0xEF;

    /// <summary>
    /// Slots holding a user curve, numbered as the wire numbers them: <b>0 to 3</b>.
    ///
    /// Not the preset index. The firmware calls the four user presets 7 to 10 and the
    /// equaliser commands call the same four 0 to 3 — Audeze HQ subtracts seven on the way
    /// down, and the non-volatile key is 0xEF00 plus this number. Asking with the preset
    /// index instead names keys that do not exist, and the headset answers every one of
    /// them with the same "never written" status an empty slot gives, which reads exactly
    /// like a headset with nothing saved on it.
    /// </summary>
    public static bool IsUserEqSlot(int slot) => slot is >= 0 and <= 3;

    /// <summary>Preset index 7..10 as the equaliser commands number it, or -1.</summary>
    public static int EqSlotOfPreset(int preset) => preset is >= 7 and <= 10 ? preset - 7 : -1;

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
