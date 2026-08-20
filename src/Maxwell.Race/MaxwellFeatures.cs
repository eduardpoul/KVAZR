namespace Maxwell.Race;

/// <summary>
/// The Maxwell feature map, lifted from the command constants compiled into
/// Audeze HQ itself (Devices::MaxwellTransport). These supersede the community
/// PROTOCOL.md guesses, several of which name the wrong feature.
/// </summary>
public static class MaxwellFeatures
{
    /// <summary>Read via 0x0901, written via 0x0900, unless noted otherwise.</summary>
    public const byte PeqIndex = 0x00;          // EQ preset slot
    public const byte VpLevel = 0x20;           // voice-prompt volume
    public const byte MicMute = 0x22;           // read only, no writer exists
    public const byte NoiseReduction = 0x24;    // microphone AI noise reduction
    public const byte EarcupBalance = 0x25;     // L/R balance slider around 0x80
    public const byte AmpGain = 0x28;
    public const byte LeftGain = 0x29;
    public const byte RightGain = 0x2A;
    public const byte SidetoneLevel = 0x2C;
    public const byte MaxSidetoneLevel = 0x2D;  // read only, the ceiling for SidetoneLevel
    public const byte SensorsEnabled = 0x2E;    // head-tracking sensors, write only
    public const byte IsNewFilm = 0x2F;         // Audeze HQ reads this slot as a first-run flag
    public const byte MicLevel = 0x35;          // absent from the public sub-command table

    /// <summary>Lives in the 0x2C82 write / 0x2C83 read namespace.</summary>
    public const byte SidetoneState = 0x07;
    public const byte AutoPowerOff = 0x01;      // writes use the longer 0x0C-length form
    public const byte ChatMix = 0x0B;

    /// <summary>
    /// Destructive commands, listed so they are never sent by accident. Both are
    /// issued in the READ-shaped form, so blind sweeps of the 0x1101 namespace will
    /// trigger them: cmd 0x1101 sub 0x95 wipes the headset, sub 0xAA re-pairs it.
    /// </summary>
    public static readonly (ushort Command, byte Sub)[] Forbidden =
    {
        (0x1101, 0x95),   // factoryReset
        (0x1101, 0xAA),   // pairing
    };

    public static bool IsForbidden(ushort command, byte sub) =>
        Forbidden.Any(f => f.Command == command && f.Sub == sub);

    public sealed record Feature(string Name, string Access, ushort ReadCommand, byte Sub, string Notes);

    /// <summary>Everything that can be read, in display order.</summary>
    public static readonly Feature[] Readable =
    {
        new("Battery level", "read", RaceSession.CmdBattery, 0x00, "cmd 0x0CD6, no sub-command"),
        new("EQ preset index", "read/write", RaceSession.CmdRead, PeqIndex, ""),
        new("Voice prompt level", "read/write", RaceSession.CmdRead, VpLevel, ""),
        new("Mic mute", "read only", RaceSession.CmdRead, MicMute, "no write function exists"),
        new("Mic noise reduction", "read/write", RaceSession.CmdRead, NoiseReduction, ""),
        new("Earcup balance", "read/write", RaceSession.CmdRead, EarcupBalance, "centred on 0x80"),
        new("Amp gain", "read/write", RaceSession.CmdRead, AmpGain, "max 150"),
        new("Left gain", "read/write", RaceSession.CmdRead, LeftGain, "max 150"),
        new("Right gain", "read/write", RaceSession.CmdRead, RightGain, "max 150"),
        new("Sidetone level", "read/write", RaceSession.CmdRead, SidetoneLevel, ""),
        new("Max sidetone level", "read only", RaceSession.CmdRead, MaxSidetoneLevel, "ceiling"),
        new("Source / new-film flag", "read/write", RaceSession.CmdRead, IsNewFilm, "0x0A = USB-C"),
        new("Mic level", "read/write", RaceSession.CmdRead, MicLevel, "undocumented upstream"),
        new("Sidetone on/off", "read/write", RaceSession.CmdChatMixRead, SidetoneState, ""),
        new("Auto power off", "read/write", RaceSession.CmdChatMixRead, AutoPowerOff, ""),
        new("ChatMix", "read/write", RaceSession.CmdChatMixRead, ChatMix, "0..20, 10 = balanced"),
    };
}
