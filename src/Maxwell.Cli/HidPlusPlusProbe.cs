using System.Text;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Asks every pairing slot on every Logitech receiver what is behind it.
///
/// This runs before any driver exists, for the same reason the tier-zero survey did: a
/// protocol built against hardware that turns out not to answer is the most expensive way
/// to discover it does not. Read-only throughout — a wrong write to a receiver can unpair
/// the device driving the cursor.
/// </summary>
internal static class HidPlusPlusProbe
{
    private const ushort Logitech = 0x046D;

    /// <summary>Where HID++ lives. Usage 2 is the long report, which is the one that matters.</summary>
    private const ushort HidPlusPlusPage = 0xFF00;

    private const ushort LongUsage = 0x02;

    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// The first ping to a sleeping device is also the thing that wakes it, and the
    /// answer can take over a second to swim back — long enough that it used to land in
    /// the next slot's window and be dismissed as "не подошёл". The wake gets patience;
    /// everything after it is against an awake device and stays brisk.
    /// </summary>
    private static readonly TimeSpan WakeWait = TimeSpan.FromMilliseconds(1800);

    private static readonly string[] Types =
        ["клавиатура", "пульт", "числовой блок", "мышь", "тачпад", "трекбол", "презентер", "приёмник"];

    public static int Run()
    {
        List<HidInterface> receivers =
        [
            .. new WindowsHidEnumerator().EnumerateAll()
                .Where(i => i.VendorId == Logitech
                    && i.UsagePage == HidPlusPlusPage
                    && i.OutputReportLength >= 7),
        ];

        if (receivers.Count == 0)
        {
            Console.WriteLine("No HID++ long-report interface found.");
            return 1;
        }

        foreach (HidInterface iface in receivers)
        {
            Console.WriteLine($"{iface.ProductName}  {iface.VendorId:X4}:{iface.ProductId:X4}");
            Console.WriteLine();

            Console.WriteLine($"  usage 0x{iface.Usage:X2}  in {iface.InputReportLength} out {iface.OutputReportLength}");

            using var channel = new WindowsHidChannel(iface.Path, iface.InputReportLength, iface.OutputReportLength);

            // The receiver first. It always answers, so if this is silent the channel is
            // wrong rather than the slots being empty — and those look identical from
            // outside, which is the whole reason to ask something guaranteed to reply.
            Receiver(channel, iface);

            for (byte device = 1; device <= 6; device++) Interrogate(channel, device);
            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Asks the receiver itself, on whichever report size this interface carries. A
    /// Unifying receiver answers HID++ 1.0 register reads on the short report even when
    /// nothing at all is paired to it, which makes it the one guaranteed reply on the bus.
    /// </summary>
    private static void Receiver(WindowsHidChannel channel, HidInterface iface)
    {
        byte[] request = new byte[iface.OutputReportLength];

        // 0x10 short: read register 0x00 on the receiver. The classic "are you there".
        request[0] = iface.OutputReportLength >= 20 ? HidPlusPlus.LongReport : HidPlusPlus.ShortReport;
        request[1] = HidPlusPlus.Receiver;
        request[2] = 0x81;
        request[3] = 0x00;

        byte[]? reply = channel.Request(request, r => r.Length > 1 && r[1] == HidPlusPlus.Receiver, Wait);

        string what = reply is null
            ? channel.LastWriteFailed ? $"запись отклонена ({channel.LastWriteError})" : "молчит"
            : string.Join(' ', reply.Take(8).Select(b => b.ToString("X2")));

        Console.WriteLine($"    приёмник 0xFF: {what}");
    }

    private static void Interrogate(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.Interrogate(channel, device) is { } paired)
        {
            Console.WriteLine($"  slot {device}  {paired.Name}");
            Console.WriteLine(
                $"            {paired.Type?.ToString() ?? "?"} · {(paired.BatteryPercent is { } b ? $"{b}%" : "заряд не отдаёт")}");
            return;
        }

        byte[]? pong = channel.Request(
            HidPlusPlus.Ping(channel.OutputLength, device),
            reply => HidPlusPlus.Answers(reply, device, HidPlusPlus.RootIndex, 0x01),
            HidPlusPlusReader.Wait);

        if (pong is null || !HidPlusPlus.Pong(pong))
        {
            string why = channel.LastWriteFailed
                ? $"запись отклонена, ошибка {channel.LastWriteError}"
                : pong is not null && HidPlusPlus.IsError(pong)
                    ? HidPlusPlus.Explain(pong)
                    : channel.LastReply is { } last
                        ? "ответ не подошёл: " + string.Join(' ', last.Take(8).Select(b => b.ToString("X2")))
                        : "ответа нет";

            Console.WriteLine($"  slot {device}  —   {why}");
            return;
        }

        string version = $"HID++ {pong[4]}.{pong[5]}";
        string name = Name(channel, device) ?? "(без имени)";
        string type = Type(channel, device);
        string battery = Battery(channel, device) ?? "заряд не отдаёт";

        Console.WriteLine($"  slot {device}  {name}");
        Console.WriteLine($"            {type} · {version} · {battery}");
    }

    private static string? Name(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, HidPlusPlus.FeatureDeviceName) is not { } feature) return null;

        // Length first: the name comes back sixteen bytes at a time and the last chunk is
        // padded, so reading without knowing where to stop appends whatever was in the
        // buffer.
        byte[]? count = Ask(channel, HidPlusPlus.Request(channel.OutputLength, device, feature, 0x00), device, feature, 0x00);
        if (count is null || HidPlusPlus.IsError(count)) return null;

        int length = count[4];
        var name = new StringBuilder();

        for (byte offset = 0; offset < length; offset += 16)
        {
            byte[]? chunk = Ask(
                channel, HidPlusPlus.Request(channel.OutputLength, device, feature, 0x01, offset), device, feature, 0x01);

            if (chunk is null || HidPlusPlus.IsError(chunk)) break;

            for (int i = 4; i < chunk.Length && name.Length < length; i++)
            {
                if (chunk[i] == 0) break;
                name.Append((char)chunk[i]);
            }
        }

        return name.Length > 0 ? name.ToString() : null;
    }

    private static string Type(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, HidPlusPlus.FeatureDeviceName) is not { } feature) return "?";

        byte[]? reply = Ask(channel, HidPlusPlus.Request(channel.OutputLength, device, feature, 0x02), device, feature, 0x02);

        if (reply is null || HidPlusPlus.IsError(reply)) return "?";

        return reply[4] < Types.Length ? Types[reply[4]] : $"тип {reply[4]}";
    }

    private static string? Battery(WindowsHidChannel channel, byte device)
    {
        // Two features mean the same thing to a person and neither is universal: the
        // newer devices carry 0x1004 and the older ones 0x1000, so both are asked.
        if (Index(channel, device, HidPlusPlus.FeatureUnifiedBattery) is { } unified)
        {
            byte[]? reply = Ask(channel, HidPlusPlus.Request(channel.OutputLength, device, unified, 0x01), device, unified, 0x01);
            if (reply is not null && !HidPlusPlus.IsError(reply)) return $"{reply[4]}%";
        }

        if (Index(channel, device, HidPlusPlus.FeatureBatteryLevel) is { } level)
        {
            byte[]? reply = Ask(channel, HidPlusPlus.Request(channel.OutputLength, device, level, 0x00), device, level, 0x00);
            if (reply is not null && !HidPlusPlus.IsError(reply)) return $"{reply[4]}%";
        }

        return null;
    }

    /// <summary>
    /// Where a feature sits on this device, or null when the device does not have it.
    /// Index zero back from the root means "no such feature", which is the answer for
    /// most features on most devices and is not an error.
    /// </summary>
    private static byte? Index(WindowsHidChannel channel, byte device, ushort feature)
    {
        byte[]? reply = Ask(
            channel, HidPlusPlus.GetFeature(channel.OutputLength, device, feature), device, HidPlusPlus.RootIndex, 0x00);

        if (reply is null || HidPlusPlus.IsError(reply) || reply[4] == 0) return null;

        return reply[4];
    }

    private static byte[]? Ask(WindowsHidChannel channel, byte[] request, byte device, byte feature, byte function) =>
        channel.Request(request, reply => HidPlusPlus.Answers(reply, device, feature, function), Wait);
}
