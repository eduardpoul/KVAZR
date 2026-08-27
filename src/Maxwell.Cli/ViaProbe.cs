using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// What a VIA keyboard answers, asked only the questions that read.
///
/// VIA is an open protocol with its command ids in QMK's own header, which is what makes
/// this a probe rather than a lottery: every byte sent here is a documented request for a
/// value, and the ids that destroy something are not in the file. In particular
/// <c>id_bootloader_jump</c> is 0x0B and <c>id_eeprom_reset</c> is 0x0A — one command
/// away from the ones below, and the reason this list is a whitelist rather than a sweep.
///
/// The transport is raw HID: usage page 0xFF60, usage 0x61, report id 0, and a payload of
/// 32 bytes each way. The command id leads the payload and the device echoes it back,
/// which is how a reply is told from a keypress arriving on the same pipe.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ViaProbe
{
    private const ushort ViaPage = 0xFF60;
    private const byte ViaUsage = 0x61;

    // Reads only. Nothing here changes anything on the keyboard.
    private const byte GetProtocolVersion = 0x01;
    private const byte GetKeyboardValue = 0x02;
    private const byte GetKeycode = 0x04;
    private const byte MacroGetCount = 0x0C;
    private const byte MacroGetBufferSize = 0x0D;
    private const byte GetLayerCount = 0x11;

    private const byte ValueUptime = 0x01;
    private const byte ValueLayoutOptions = 0x02;
    private const byte ValueFirmwareVersion = 0x04;

    private const byte Unhandled = 0xFF;

    public static int Run()
    {
        HidInterface? via = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.UsagePage == ViaPage && i.Usage == ViaUsage);

        if (via is null)
        {
            Console.WriteLine("Коллекции VIA (0xFF60:0x61) нет ни на одном устройстве.");
            Console.WriteLine("Клавиатура в беспроводном режиме или её прошивка без VIA.");
            return 1;
        }

        Console.WriteLine($"{via.VendorId:X4}:{via.ProductId:X4}  {via.ProductName}");
        Console.WriteLine($"  вход {via.InputReportLength}  выход {via.OutputReportLength}");
        Console.WriteLine();

        using var channel = new WindowsHidChannel(via.Path, via.InputReportLength, via.OutputReportLength);

        byte[]? version = Ask(channel, via, GetProtocolVersion);
        Console.WriteLine($"  версия протокола     {Number(version, 2, 2)}");

        byte[]? layers = Ask(channel, via, GetLayerCount);
        Console.WriteLine($"  слоёв                {Number(layers, 2, 1)}");

        byte[]? firmware = Ask(channel, via, GetKeyboardValue, ValueFirmwareVersion);
        Console.WriteLine($"  версия прошивки      {Number(firmware, 3, 4)}");

        byte[]? uptime = Ask(channel, via, GetKeyboardValue, ValueUptime);
        Console.WriteLine($"  аптайм, мс           {Number(uptime, 3, 4)}");

        byte[]? layout = Ask(channel, via, GetKeyboardValue, ValueLayoutOptions);
        Console.WriteLine($"  параметры раскладки  {Number(layout, 3, 4)}");

        byte[]? macros = Ask(channel, via, MacroGetCount);
        Console.WriteLine($"  макросов             {Number(macros, 2, 1)}");

        byte[]? buffer = Ask(channel, via, MacroGetBufferSize);
        Console.WriteLine($"  буфер макросов, б    {Number(buffer, 2, 2)}");

        Console.WriteLine();

        // The raw frame once, because the layout of a reply is a thing to read rather
        // than to remember: VIA answers a keycode read with the command, the layer, the
        // row and the column echoed back before the two bytes that were asked for.
        byte[]? sample = Ask(channel, via, GetKeycode, 0x00, 0x00, 0x00);
        Console.WriteLine(sample is null
            ? "  клавиша: нет ответа"
            : $"  сырой ответ на чтение клавиши (0,0): {Convert.ToHexString(sample, 0, Math.Min(10, sample.Length))}");

        Console.WriteLine();
        Console.WriteLine("  первые клавиши слоя 0, как их держит клавиатура:");

        for (byte row = 0; row < 2; row++)
        {
            var line = new List<string>();

            for (byte col = 0; col < 6; col++)
            {
                byte[]? key = Ask(channel, via, GetKeycode, 0x00, row, col);
                line.Add(key is { Length: > 6 } ? $"{key[5]:X2}{key[6]:X2}" : "----");
            }

            Console.WriteLine($"    ряд {row}:  {string.Join("  ", line)}");
        }

        return 0;
    }

    /// <summary>
    /// One documented read. The reply is the one whose first payload byte echoes the
    /// command — a keypress arriving on the same pipe would not.
    /// </summary>
    private static byte[]? Ask(WindowsHidChannel channel, HidInterface via, byte command, params byte[] args)
    {
        byte[] report = new byte[via.OutputReportLength];
        report[0] = 0x00;
        report[1] = command;

        for (int i = 0; i < args.Length && i + 2 < report.Length; i++) report[i + 2] = args[i];

        // The echoed arguments are part of the match, not decoration.
        //
        // A reply carries no sequence number, and every keycode read answers with the
        // same command id — so a predicate that checks the id alone will accept the
        // answer to the previous question. Measured: reading a row of six keys that way
        // returned two of them holding another key's code, one row shifted. The device
        // echoes the layer, row and column it was asked about, and matching those makes
        // each answer identifiable.
        return channel.Request(
            report,
            reply => reply.Length > 1
                && (reply[1] == Unhandled
                    || (reply[1] == command && Echoes(reply, args))),
            TimeSpan.FromMilliseconds(600));
    }

    /// <summary>Whether a reply repeats the arguments it was asked with.</summary>
    private static bool Echoes(byte[] reply, byte[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (reply.Length <= i + 2 || reply[i + 2] != args[i]) return false;
        }

        return true;
    }

    /// <summary>A big-endian number out of the reply, or a dash where nothing came back.</summary>
    private static string Number(byte[]? reply, int at, int length)
    {
        if (reply is null) return "нет ответа";
        if (reply.Length > 1 && reply[1] == Unhandled) return "команда не поддержана";
        if (reply.Length < at + length) return "ответ короче ожидаемого";

        long value = 0;
        for (int i = 0; i < length; i++) value = (value << 8) | reply[at + i];

        return $"{value}  (" + Convert.ToHexString(reply, at, length) + ")";
    }
}
