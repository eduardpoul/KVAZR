using System.Globalization;
using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Digs into the devices on this desk: every HID interface they expose, the full HID++
/// feature table of whatever is paired to a Logitech receiver, and a passive listen on
/// each vendor-defined interface to see who broadcasts without being asked.
///
/// Read-only by construction. The listen never writes a byte, and the HID++ part calls
/// only the documented read functions — a wrong write to an unknown vendor interface is
/// how devices end up in states their own software cannot explain.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DeskProbe
{
    /// <summary>The HID++ features worth recognising by name when they turn up.</summary>
    private static readonly Dictionary<ushort, string> KnownFeatures = new()
    {
        [0x0000] = "Root",
        [0x0001] = "FeatureSet",
        [0x0003] = "DeviceInfo (FW версии)",
        [0x0005] = "DeviceName",
        [0x0020] = "ConfigChange",
        [0x0021] = "UniqueId (32 байта)",
        [0x1000] = "Battery (уровни)",
        [0x1004] = "UnifiedBattery (%)",
        [0x1814] = "ChangeHost (переключение хоста)",
        [0x1815] = "HostsInfo (список хостов)",
        [0x1830] = "PowerModes (сон)",
        [0x1861] = "BatteryCalibration",
        [0x1b04] = "SpecialKeys / кнопки (ремап)",
        [0x1bc0] = "ReportHidUsage",
        [0x1d4b] = "WirelessDeviceStatus",
        [0x1e00] = "EnableHiddenFeatures",
        [0x1eb0] = "TdeAccess",
        [0x2110] = "SmartShift (колесо)",
        [0x2121] = "HiRes Wheel",
        [0x2201] = "AdjustableDPI",
        [0x2202] = "ExtendedAdjustableDPI",
        [0x2250] = "AnalyticsMove",
        [0x2251] = "LiftDetection",
        [0x8060] = "ReportRate (частота опроса)",
        [0x8061] = "ExtendedReportRate (до 8 кГц)",
        [0x8090] = "ModeStatus (perf/endurance)",
        [0x8100] = "OnboardProfiles (память мыши)",
        [0x8110] = "MouseButtonSpy",
        [0x1990] = "IlluminationLight",
        [0x8070] = "ColorLedEffects (подсветка)",
        [0x8071] = "RgbEffects",
        [0x00c2] = "DfuControl (прошивка)",
        [0x00d0] = "Dfu",
    };

    public static int Run(string[] args)
    {
        var enumerator = new WindowsHidEnumerator();
        List<HidInterface> all = [.. enumerator.EnumerateAll()];

        // The desk map: everything, grouped, with usage pages spelled out. This is the
        // list of doors; the rest of the probe walks through the interesting ones.
        Console.WriteLine("=== HID-интерфейсы стола ===");

        foreach (IGrouping<(ushort Vid, ushort Pid), HidInterface> device in
                 all.GroupBy(i => (i.VendorId, i.ProductId)).OrderBy(g => g.Key))
        {
            HidInterface first = device.First();
            Console.WriteLine($"\n{device.Key.Vid:X4}:{device.Key.Pid:X4}  {first.ProductName}");

            foreach (HidInterface iface in device.OrderBy(i => i.UsagePage).ThenBy(i => i.Usage))
            {
                string kind = iface.UsagePage switch
                {
                    0x01 => iface.Usage switch
                    {
                        0x02 => "мышь",
                        0x04 => "джойстик",
                        0x05 => "геймпад",
                        0x06 => "клавиатура",
                        0x80 => "системные кнопки",
                        _ => "generic",
                    },
                    0x0C => "consumer (медиа-клавиши)",
                    0x0D => "digitizer",
                    0xFF00 => "HID++",
                    >= 0xFF00 => "vendor-defined",
                    _ => "",
                };

                Console.WriteLine(
                    $"    page {iface.UsagePage:X4} usage {iface.Usage:X2}  in {iface.InputReportLength,3}  " +
                    $"out {iface.OutputReportLength,3}  {kind}");
            }
        }

        // The Logitech feature table: everything the paired device admits to having.
        Console.WriteLine("\n=== HID++: полная таблица фич ===");

        foreach (HidInterface iface in all.Where(i =>
                     i.VendorId == 0x046D && i.UsagePage == 0xFF00 && i.Usage == 0x02 && i.OutputReportLength >= 20))
        {
            Console.WriteLine($"\n{iface.ProductName}  {iface.VendorId:X4}:{iface.ProductId:X4}");

            try
            {
                using var channel = new WindowsHidChannel(iface.Path, iface.InputReportLength, iface.OutputReportLength);
                DumpFeatures(channel);
            }
            catch (IOException e)
            {
                Console.WriteLine($"  канал не открылся: {e.Message}");
            }
        }

        // Passive listens on the vendor-defined interfaces: who talks unprompted.
        Console.WriteLine("\n=== Вендорские интерфейсы: пассивное прослушивание (5 с) ===");

        foreach (HidInterface iface in all.Where(i =>
                     i.UsagePage >= 0xFF00 && i.UsagePage != 0xFF00 && i.InputReportLength > 1))
        {
            Console.WriteLine($"\n{iface.VendorId:X4}:{iface.ProductId:X4}  {iface.ProductName}  " +
                              $"page {iface.UsagePage:X4} in {iface.InputReportLength}");

            try
            {
                using var channel = new WindowsHidChannel(iface.Path, iface.InputReportLength, iface.OutputReportLength);

                int heard = 0;
                DateTime end = DateTime.UtcNow.AddSeconds(5);

                while (DateTime.UtcNow < end && heard < 6)
                {
                    if (channel.Listen(TimeSpan.FromMilliseconds(1200)) is not { } report) continue;

                    heard++;
                    Console.WriteLine("    " + string.Join(' ', report.Take(24).Select(b => b.ToString("X2", CultureInfo.InvariantCulture))));
                }

                if (heard == 0) Console.WriteLine("    тишина — сам ничего не шлёт");
            }
            catch (IOException e)
            {
                Console.WriteLine($"    не открылся: {e.Message}");
            }
        }

        return 0;
    }

    private static void DumpFeatures(WindowsHidChannel channel)
    {
        byte device = FindDevice(channel);
        if (device == 0)
        {
            Console.WriteLine("  никто не ответил на пинг");
            return;
        }

        Console.WriteLine($"  device index 0x{device:X2}");

        // Feature 0x0001 is the table of contents: count, then ids by position.
        byte[]? setIndex = Ask(channel, device, 0x00, 0x00, 0x00, 0x01);
        if (setIndex is null || HidPlusPlus.IsError(setIndex) || setIndex[4] == 0)
        {
            Console.WriteLine("  FeatureSet недоступен");
            return;
        }

        byte set = setIndex[4];
        byte[]? countReply = Ask(channel, device, set, 0x00);
        if (countReply is null || HidPlusPlus.IsError(countReply)) return;

        int count = countReply[4];
        Console.WriteLine($"  фич: {count + 1}");

        for (byte i = 0; i <= count; i++)
        {
            byte[]? entry = Ask(channel, device, set, 0x01, i);
            if (entry is null || HidPlusPlus.IsError(entry)) continue;

            ushort id = (ushort)((entry[4] << 8) | entry[5]);
            byte flags = entry[6];

            string name = KnownFeatures.TryGetValue(id, out string? known) ? known : "";
            string hidden = (flags & 0x40) != 0 ? " [hidden]" : "";
            string engineering = (flags & 0x20) != 0 ? " [eng]" : "";

            Console.WriteLine($"    [{i,2}] 0x{id:X4}  {name}{hidden}{engineering}");
        }

        // Safe reads on the designable features. Raw bytes are printed alongside the
        // parse wherever the layout is community-documented rather than official: a
        // wrong guess should be checkable against the dump, not hidden behind it.
        if (Feature(channel, device, 0x0003) is { } info)
        {
            byte[]? fw = Ask(channel, device, info, 0x01, 0x00);
            if (fw is not null && !HidPlusPlus.IsError(fw))
            {
                string ascii = new([.. fw.Skip(5).Take(3).Select(b => (char)b)]);
                Console.WriteLine($"  Прошивка (0x0003): {ascii}{fw[8]:X2}.{fw[9]:X2}_{(fw[10] << 8) | fw[11]:X4}");
            }
        }

        if (Feature(channel, device, 0x2201) is { } dpi)
        {
            byte[]? current = Ask(channel, device, dpi, 0x02, 0x00);
            if (current is not null && !HidPlusPlus.IsError(current))
            {
                Console.WriteLine($"  DPI сейчас (0x2201): {(current[5] << 8) | current[6]}");
            }
        }

        if (Feature(channel, device, 0x2202) is { } xdpi)
        {
            byte[]? current = Ask(channel, device, xdpi, 0x03, 0x00, 0x00);
            if (current is not null && !HidPlusPlus.IsError(current))
            {
                int x = (current[5] << 8) | current[6];
                int y = (current[9] << 8) | current[10];
                Console.WriteLine($"  DPI сейчас (0x2202, X/Y): {x} / {y}");
                Console.WriteLine($"    raw: {Hex(current)}");
            }
        }

        if (Feature(channel, device, 0x8060) is { } rate)
        {
            byte[]? current = Ask(channel, device, rate, 0x01);
            if (current is not null && !HidPlusPlus.IsError(current))
            {
                Console.WriteLine($"  Report rate (0x8060): {1000 / Math.Max(1, (int)current[4])} Гц");
            }
        }

        if (Feature(channel, device, 0x8061) is { } xrate)
        {
            byte[]? supported = Ask(channel, device, xrate, 0x00, 0x00);
            byte[]? current = Ask(channel, device, xrate, 0x02);

            if (supported is not null && !HidPlusPlus.IsError(supported))
            {
                Console.WriteLine($"  Report rates (0x8061) поддержка: {Rates(supported[4])} (raw {supported[4]:X2})");
            }

            if (current is not null && !HidPlusPlus.IsError(current))
            {
                Console.WriteLine($"  Report rate (0x8061) сейчас: {RateOf(current[4])} (raw {Hex(current)})");
            }
        }
    }

    /// <summary>0x8061 encodes rates as an enum: 0=125Гц … 5=4кГц, 6=8кГц.</summary>
    private static string RateOf(byte value) => value switch
    {
        0 => "125 Гц", 1 => "250 Гц", 2 => "500 Гц", 3 => "1 кГц",
        4 => "2 кГц", 5 => "4 кГц", 6 => "8 кГц",
        _ => $"?{value}",
    };

    private static string Rates(byte bits) =>
        string.Join(", ", Enumerable.Range(0, 7).Where(i => (bits & (1 << i)) != 0).Select(i => RateOf((byte)i)));

    private static string Hex(byte[] bytes) =>
        string.Join(' ', bytes.Take(14).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>The direct index first — the receiver register says which world this is.</summary>
    private static byte FindDevice(WindowsHidChannel channel)
    {
        foreach (byte candidate in (byte[])[0xFF, 1, 2])
        {
            byte[]? pong = channel.Request(
                HidPlusPlus.Ping(channel.OutputLength, candidate),
                r => HidPlusPlus.Answers(r, candidate, HidPlusPlus.RootIndex, 0x01),
                HidPlusPlusReader.WakeWait);

            if (pong is not null && HidPlusPlus.Pong(pong)) return candidate;
        }

        return 0;
    }

    private static byte? Feature(WindowsHidChannel channel, byte device, ushort id)
    {
        byte[]? reply = Ask(channel, device, 0x00, 0x00, (byte)(id >> 8), (byte)(id & 0xFF));
        return reply is null || HidPlusPlus.IsError(reply) || reply[4] == 0 ? null : reply[4];
    }

    private static byte[]? Ask(
        WindowsHidChannel channel, byte device, byte feature, byte function, params byte[] parameters) =>
        channel.Request(
            HidPlusPlus.Request(channel.OutputLength, device, feature, function, parameters),
            reply => HidPlusPlus.Answers(reply, device, feature, function),
            HidPlusPlusReader.Wait);
}
