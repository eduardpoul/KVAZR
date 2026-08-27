namespace Maxwell.Race.Protocol;

/// <summary>
/// The VIA protocol, as QMK's own <c>quantum/via.h</c> declares it.
///
/// Raw HID underneath: usage page 0xFF60, usage 0x61, report id 0, and a payload of 32
/// bytes each way. The command id leads the payload and the keyboard echoes it back
/// along with whatever arguments were sent, which is the only thing that makes a reply
/// identifiable — there is no sequence number, and every keycode read answers with the
/// same command id. Measured: matching on the id alone returned another key's code.
///
/// Only the reads are named here, and that is deliberate rather than tidy. The
/// destructive ids sit among them — <c>id_bootloader_jump</c> is 0x0B and
/// <c>id_eeprom_reset</c> is 0x0A — so this file carries a whitelist, and a command that
/// changes something has to be added on purpose by somebody who meant to.
/// </summary>
public static class Via
{
    /// <summary>The usage page a VIA keyboard answers on. Not configurable in practice: VIA's own client looks for exactly this.</summary>
    public const ushort UsagePage = 0xFF60;

    public const byte Usage = 0x61;

    /// <summary>Always answers 0x01 in the first payload byte, whatever it is asked.</summary>
    public const byte GetProtocolVersion = 0x01;

    public const byte GetKeyboardValue = 0x02;

    /// <summary>Arguments: layer, row, column. Answers with all three echoed and the keycode.</summary>
    public const byte GetKeycode = 0x04;

    public const byte MacroGetCount = 0x0C;
    public const byte MacroGetBufferSize = 0x0D;
    public const byte GetLayerCount = 0x11;

    /// <summary>What the keyboard says when it does not know the command.</summary>
    public const byte Unhandled = 0xFF;

    // Sub-commands of GetKeyboardValue.
    public const byte ValueUptime = 0x01;
    public const byte ValueLayoutOptions = 0x02;
    public const byte ValueFirmwareVersion = 0x04;

    /// <summary>
    /// A request, laid out for the wire: the report id, the command, then the arguments.
    /// </summary>
    public static byte[] Request(int reportLength, byte command, params byte[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThan(reportLength, 2);

        byte[] report = new byte[reportLength];
        report[1] = command;

        for (int i = 0; i < arguments.Length && i + 2 < reportLength; i++)
        {
            report[i + 2] = arguments[i];
        }

        return report;
    }

    /// <summary>
    /// Whether a reply belongs to this request: the command echoed, and every argument
    /// with it. An unhandled command answers 0xFF and that is an answer too — the
    /// keyboard heard and declined, which is worth telling from silence.
    /// </summary>
    public static bool Answers(byte[] reply, byte command, params byte[] arguments)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(arguments);

        if (reply.Length < 2) return false;
        if (reply[1] == Unhandled) return true;
        if (reply[1] != command) return false;

        for (int i = 0; i < arguments.Length; i++)
        {
            if (reply.Length <= i + 2 || reply[i + 2] != arguments[i]) return false;
        }

        return true;
    }

    /// <summary>True where the keyboard answered that it does not know the command.</summary>
    public static bool Declined(byte[]? reply) => reply is { Length: > 1 } && reply[1] == Unhandled;

    /// <summary>
    /// A big-endian number out of a reply, or null where the reply is missing, declined
    /// or shorter than the field asked for. Null rather than zero: a keyboard that said
    /// nothing and a keyboard that said zero are different facts.
    /// </summary>
    public static long? Number(byte[]? reply, int at, int length)
    {
        if (reply is null || Declined(reply)) return null;
        if (at < 0 || length <= 0 || reply.Length < at + length) return null;

        long value = 0;
        for (int i = 0; i < length; i++) value = (value << 8) | reply[at + i];

        return value;
    }

    /// <summary>The protocol version, which VIA's own client uses to decide what it may ask.</summary>
    public static int? ProtocolVersion(byte[]? reply) => (int?)Number(reply, 2, 2);

    /// <summary>How many layers the keyboard stores.</summary>
    public static int? LayerCount(byte[]? reply) => (int?)Number(reply, 2, 1);

    /// <summary>
    /// The keycode from a keymap read, which sits after the three echoed arguments.
    /// </summary>
    public static int? Keycode(byte[]? reply) => (int?)Number(reply, 5, 2);
}
