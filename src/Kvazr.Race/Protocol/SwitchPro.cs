namespace Kvazr.Race.Protocol;

/// <summary>
/// Nintendo Switch Pro Controller, the read-mostly half of its HID protocol.
///
/// Written ahead of the hardware on purpose: the protocol is community-documented to
/// the byte (dekuNukem's Nintendo_Switch_Reverse_Engineering is the canonical source,
/// and every open driver — Linux hid-nintendo, joycond, BetterJoy — agrees with it).
/// Everything here is pure frame building and parsing, unit-tested against the
/// documented examples, so the day the pad is plugged in the only question left is
/// whether it answers — not whether the bytes are right.
///
/// Writes exist in the protocol (rumble, LEDs, input mode) but this class builds only
/// the ones a read path needs: the USB handshake, input-mode select, and queries. The
/// capability system's Untested marking covers the first live run.
/// </summary>
public static class SwitchPro
{
    public const ushort Vendor = 0x057E;

    public const ushort ProController = 0x2009;

    // ---- report ids -----------------------------------------------------------------

    /// <summary>Host → pad: rumble + optional subcommand.</summary>
    public const byte OutputReport = 0x01;

    /// <summary>Pad → host: subcommand reply.</summary>
    public const byte SubcommandReply = 0x21;

    /// <summary>Pad → host: full input at 60 Hz (buttons, sticks, IMU).</summary>
    public const byte FullInput = 0x30;

    /// <summary>Pad → host: simple HID input, the mode USB pads wake up in.</summary>
    public const byte SimpleInput = 0x3F;

    /// <summary>Host → pad over USB only: the 0x80-prefixed link commands.</summary>
    public const byte UsbCommand = 0x80;

    // ---- subcommands ----------------------------------------------------------------

    public const byte SubDeviceInfo = 0x02;
    public const byte SubSetInputMode = 0x03;
    public const byte SubSetShipmentState = 0x08;
    public const byte SubSpiFlashRead = 0x10;
    public const byte SubSetPlayerLights = 0x30;
    public const byte SubEnableImu = 0x40;
    public const byte SubEnableVibration = 0x48;

    // ---- SPI flash addresses (calibration and colours live in the pad) --------------

    /// <summary>Serial number, 16 bytes ASCII (0xFF-padded when absent).</summary>
    public const uint SpiSerial = 0x6000;

    /// <summary>Body colour RGB, then buttons RGB — the pad knows what colour it is.</summary>
    public const uint SpiColours = 0x6050;

    /// <summary>Factory stick calibration, 18 bytes (left 9, right 9).</summary>
    public const uint SpiStickCalibration = 0x603D;

    /// <summary>
    /// The neutral rumble frame. Every output report carries rumble data whether or not
    /// it means to vibrate, and this is the documented "do nothing" encoding.
    /// </summary>
    public static ReadOnlySpan<byte> NeutralRumble => [0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40];

    /// <summary>
    /// Builds the output report carrying a subcommand: id, a 4-bit rolling counter the
    /// pad uses to tell retries from new commands, the mandatory rumble block, then the
    /// subcommand and its arguments.
    /// </summary>
    public static byte[] Subcommand(int size, byte counter, byte subcommand, params byte[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 12 + arguments.Length);

        byte[] report = new byte[size];
        report[0] = OutputReport;
        report[1] = (byte)(counter & 0x0F);
        NeutralRumble.CopyTo(report.AsSpan(2));
        report[10] = subcommand;
        arguments.CopyTo(report, 11);
        return report;
    }

    /// <summary>Switches the pad into the full 0x30 input mode, where battery and IMU live.</summary>
    public static byte[] SetFullInputMode(int size, byte counter) =>
        Subcommand(size, counter, SubSetInputMode, FullInput);

    /// <summary>Reads a run of SPI flash: little-endian address, then a length up to 0x1D.</summary>
    public static byte[] SpiRead(int size, byte counter, uint address, byte length) =>
        Subcommand(
            size, counter, SubSpiFlashRead,
            (byte)(address & 0xFF), (byte)((address >> 8) & 0xFF),
            (byte)((address >> 16) & 0xFF), (byte)((address >> 24) & 0xFF),
            length);

    /// <summary>
    /// The USB link commands. Over USB the pad sits silent behind its Broadcom chip
    /// until 0x02 (handshake) is sent; 0x04 then keeps it on USB HID instead of timing
    /// out back to Bluetooth. Over Bluetooth neither exists nor is needed.
    /// </summary>
    public static byte[] Usb(int size, byte command)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);

        byte[] report = new byte[size];
        report[0] = UsbCommand;
        report[1] = command;
        return report;
    }

    public const byte UsbHandshake = 0x02;
    public const byte UsbForceHid = 0x04;

    // ---- parsing --------------------------------------------------------------------

    /// <summary>True when this report answers the given subcommand and acknowledges it.</summary>
    public static bool Answers(byte[] reply, byte subcommand)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return reply.Length >= 15
            && reply[0] == SubcommandReply
            && reply[14] == subcommand
            && (reply[13] & 0x80) != 0;
    }

    /// <summary>Where the subcommand's own data starts inside a 0x21 reply.</summary>
    public const int ReplyDataOffset = 15;

    /// <summary>
    /// The battery nibble, present in every 0x21 and 0x30 report at byte 2. The pad
    /// reports four steps, not a percentage — 8 full, 6 medium, 4 low, 2 critical —
    /// with the low bit of the nibble set while charging. The mapping to percent is
    /// honest about the resolution: 100/70/40/15, never a number the pad did not say.
    /// </summary>
    public static (int Percent, bool IsCharging)? Battery(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Length < 3 || report[0] is not (SubcommandReply or FullInput)) return null;

        int nibble = report[2] >> 4;
        bool charging = (nibble & 0x1) != 0;

        int? percent = (nibble & 0xE) switch
        {
            8 => 100,
            6 => 70,
            4 => 40,
            2 => 15,
            0 => 5,
            _ => null,
        };

        return percent is { } p ? (p, charging) : null;
    }

    /// <summary>Body and button colours out of the SPI colour block: the pad knows
    /// what colour it is, and the row can match it.</summary>
    public static (int Body, int Buttons)? Colours(byte[] spiReply)
    {
        ArgumentNullException.ThrowIfNull(spiReply);

        // An SPI reply echoes the address and length before the data.
        int at = ReplyDataOffset + 5;
        if (spiReply.Length < at + 6) return null;

        int body = (spiReply[at] << 16) | (spiReply[at + 1] << 8) | spiReply[at + 2];
        int buttons = (spiReply[at + 3] << 16) | (spiReply[at + 4] << 8) | spiReply[at + 5];
        return (body, buttons);
    }

    /// <summary>
    /// One analog stick from the packed twelve-bit pair the pad uses everywhere:
    /// three bytes carry X and Y, calibration turns them into a signed range later.
    /// </summary>
    public static (int X, int Y) Stick(byte[] report, int offset)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + 2, report.Length - 1);

        int x = report[offset] | ((report[offset + 1] & 0x0F) << 8);
        int y = (report[offset + 1] >> 4) | (report[offset + 2] << 4);
        return (x, y);
    }

    /// <summary>Left stick bytes start here in 0x30/0x21 reports; the right one follows.</summary>
    public const int LeftStickOffset = 6;

    public const int RightStickOffset = 9;
}
