using Maxwell.Race.Protocol;

namespace Maxwell.Race;

/// <summary>
/// The Maxwell feature map, lifted from the command constants compiled into Audeze HQ
/// (Devices::MaxwellTransport). These supersede the community protocol notes, which
/// name several of these wrongly — 0x24 is noise reduction rather than "audio reset",
/// 0x20 is voice-prompt level rather than a source flag, 0x2C is sidetone level rather
/// than "audio config", and 0x35 is absent from them entirely.
/// </summary>
public static class MaxwellFeatures
{
    // Read via 0x0901, written via 0x0900, unless noted otherwise.

    /// <summary>EQ preset slot.</summary>
    public const byte PeqIndex = 0x00;

    /// <summary>Voice-prompt volume.</summary>
    public const byte VpLevel = 0x20;

    /// <summary>Microphone mute. Read only — no write function exists in Audeze HQ either.</summary>
    public const byte MicMute = 0x22;

    /// <summary>Microphone noise reduction: 0 off, 1 low, 2 high.</summary>
    public const byte NoiseReduction = 0x24;

    /// <summary>L/R balance slider, centred on 0x80.</summary>
    public const byte EarcupBalance = 0x25;

    public const byte AmpGain = 0x28;
    public const byte LeftGain = 0x29;
    public const byte RightGain = 0x2A;

    public const byte SidetoneLevel = 0x2C;

    /// <summary>Read only: the ceiling for <see cref="SidetoneLevel"/>.</summary>
    public const byte MaxSidetoneLevel = 0x2D;

    /// <summary>Head-tracking sensors. Write only.</summary>
    public const byte SensorsEnabled = 0x2E;

    /// <summary>Audeze HQ reads this slot as a first-run flag; the community notes call it the audio source state.</summary>
    public const byte IsNewFilm = 0x2F;

    /// <summary>Microphone level. Absent from the public sub-command table.</summary>
    public const byte MicLevel = 0x35;

    // Read via 0x2C83, written via 0x2C82.

    /// <summary>Sidetone on/off.</summary>
    public const byte SidetoneState = 0x07;

    /// <summary>Auto power-off. Writes use the longer 0x0C-length form.</summary>
    public const byte AutoPowerOff = 0x01;

    public const byte ChatMix = 0x0B;

    /// <summary>True for the three sub-commands that drive amplifier gain and so carry a damage risk.</summary>
    public static bool IsGain(byte sub) => sub is AmpGain or LeftGain or RightGain;

    /// <summary>How a feature may be accessed.</summary>
    public enum Access
    {
        ReadOnly,
        ReadWrite,
        WriteOnly,
    }

    public sealed record Feature(string Name, Access Access, ushort ReadCommand, byte Sub, string Notes);

    /// <summary>Everything readable, in display order.</summary>
    public static readonly Feature[] Readable =
    [
        new("Battery level", Access.ReadOnly, RaceCommands.Battery, 0x00, "no sub-command"),
        new("EQ preset index", Access.ReadWrite, RaceCommands.Read, PeqIndex, "slots 1-10; 0 accepted, unidentified"),
        new("Voice prompt level", Access.ReadWrite, RaceCommands.Read, VpLevel, ""),
        new("Mic mute", Access.ReadOnly, RaceCommands.Read, MicMute, "hardware controlled"),
        new("Mic noise reduction", Access.ReadWrite, RaceCommands.Read, NoiseReduction, "0 off, 1 low, 2 high"),
        new("Earcup balance", Access.ReadWrite, RaceCommands.Read, EarcupBalance, "centred on 0x80"),
        new("Amp gain", Access.ReadWrite, RaceCommands.Read, AmpGain, "max 150"),
        new("Left gain", Access.ReadWrite, RaceCommands.Read, LeftGain, "max 150"),
        new("Right gain", Access.ReadWrite, RaceCommands.Read, RightGain, "max 150"),
        new("Sidetone level", Access.ReadWrite, RaceCommands.Read, SidetoneLevel, ""),
        new("Max sidetone level", Access.ReadOnly, RaceCommands.Read, MaxSidetoneLevel, "ceiling"),
        new("Source / new-film flag", Access.ReadWrite, RaceCommands.Read, IsNewFilm, "0x0A = USB-C"),
        new("Mic level", Access.ReadWrite, RaceCommands.Read, MicLevel, "needs the boom mic attached"),
        new("Sidetone on/off", Access.ReadWrite, RaceCommands.ChatMixRead, SidetoneState, ""),
        new("Auto power off", Access.ReadWrite, RaceCommands.ChatMixRead, AutoPowerOff, "preset index"),
        new("ChatMix", Access.ReadWrite, RaceCommands.ChatMixRead, ChatMix, "0..20, 10 = balanced"),
    ];
}
