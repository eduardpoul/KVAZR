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
            bool direct = Receiver(channel, iface);

            if (direct)
            {
                // One device, wired, answering wherever it is asked. Its own index.
                Interrogate(channel, HidPlusPlus.Receiver);
            }
            else
            {
                for (byte device = 1; device <= 6; device++) Interrogate(channel, device);
            }
            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Asks the receiver itself, on whichever report size this interface carries. A
    /// Unifying receiver answers HID++ 1.0 register reads on the short report even when
    /// nothing at all is paired to it, which makes it the one guaranteed reply on the bus.
    /// </summary>
    private static bool Receiver(WindowsHidChannel channel, HidInterface iface)
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

        // Whether anything at all lives on index 0xFF, which is how a directly attached
        // device is told from a receiver. Measured both ways on this desk: a receiver's
        // long report says nothing on 0xFF, while a mouse on a cable answers there —
        // and answers on every other index too, being the only device on the bus. Asking
        // it six times printed the same mouse six times.
        return reply is not null;
    }

    private static void Interrogate(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.Interrogate(channel, device) is { } paired)
        {
            Console.WriteLine($"  slot {device}  {paired.Name}");
            Console.WriteLine(
                $"            {paired.Type?.ToString() ?? "?"} · {(paired.Battery is { } b ? $"{b.Percent}%{(b.IsCharging ? " ⚡" : "")}" : "заряд не отдаёт")}");

            Features(channel, device);
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
        string battery = HidPlusPlusReader.Battery(channel, device) is { } charge
            ? $"{charge.Percent}%{(charge.IsCharging ? " (заряжается)" : "")}"
            : "заряд не отдаёт";

        Console.WriteLine($"  slot {device}  {name}");
        Console.WriteLine($"            {type} · {version} · {battery}");

        Features(channel, device);
    }

    /// <summary>
    /// Everything this device can be asked, from its own feature table.
    ///
    /// HID++ 2.0 devices carry a list of the features they implement, and the list is
    /// itself a feature — so a device can be asked what it is capable of rather than
    /// probed for it one guess at a time. This is the answer to what a mouse page could
    /// hold beyond a name and a charge: whatever is in here has a documented shape.
    ///
    /// Printed as raw identifiers with names only where they are certain. The number is
    /// the measurement; the name is a convenience, and a wrong name would be worse than
    /// none.
    /// </summary>
    private static void Features(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, FeatureSet) is not { } set) return;

        byte[]? counted = Ask(
            channel, HidPlusPlus.Request(channel.OutputLength, device, set, 0x00), device, set, 0x00);
        if (counted is null || counted.Length < 5) return;

        int count = counted[4];
        if (count is 0 or > 80) return;

        var listed = new List<string>();

        for (byte i = 1; i <= count; i++)
        {
            byte[]? entry = Ask(
                channel, HidPlusPlus.Request(channel.OutputLength, device, set, 0x01, i), device, set, 0x01);
            if (entry is null || entry.Length < 6) continue;

            ushort id = (ushort)((entry[4] << 8) | entry[5]);
            listed.Add(Named(id) is { } known ? $"0x{id:X4} {known}" : $"0x{id:X4}");
        }

        if (listed.Count == 0) return;

        Console.WriteLine($"            {listed.Count} features:");

        foreach (string line in listed) Console.WriteLine($"              {line}");

        Resolution(channel, device);
    }

    /// <summary>
    /// What the resolution feature answers to its first three questions.
    ///
    /// Functions 0, 1 and 2 and no further, and that bound is the point rather than
    /// caution for its own sake. There are two resolution features in HID++ and this
    /// prints raw bytes precisely because it is not yet known which layout this device
    /// speaks — but in both of them the first three functions are questions, and the
    /// setter is further down the list. Guessing one function too far on a mouse means
    /// changing somebody's resolution to find out what the number meant.
    /// </summary>
    private static void Resolution(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, ExtendedResolution) is not { } feature) return;

        Console.WriteLine($"            0x2202 at index {feature}, asked its first three questions:");

        for (byte function = 0; function <= 2; function++)
        {
            byte[]? reply = Ask(
                channel,
                HidPlusPlus.Request(channel.OutputLength, device, feature, function),
                device,
                feature,
                function);

            string said = reply is null
                ? "нет ответа"
                : HidPlusPlus.IsError(reply)
                    ? HidPlusPlus.Explain(reply)
                    : Convert.ToHexString(reply.AsSpan(4, Math.Min(12, reply.Length - 4)));

            Console.WriteLine($"              fn {function}  {said}");
        }
    }

    /// <summary>Adjustable resolution, the extended flavour — what this mouse declares.</summary>
    private const ushort ExtendedResolution = 0x2202;

    /// <summary>The feature that lists the features.</summary>
    private const ushort FeatureSet = 0x0001;

    /// <summary>
    /// The identifiers worth naming, and only the ones there is no doubt about. Everything
    /// else prints as a number, which is what it is.
    /// </summary>
    private static string? Named(ushort id) => id switch
    {
        0x0000 => "root",
        0x0001 => "feature set",
        0x0003 => "device information",
        0x0005 => "device name and kind",
        0x1000 => "battery level",
        0x1001 => "battery voltage",
        0x1004 => "unified battery",
        0x1814 => "change host",
        0x1815 => "hosts information",
        0x1b04 => "reprogrammable controls",
        0x2201 => "adjustable resolution",
        0x2202 => "extended adjustable resolution",
        0x8060 => "report rate",
        0x8061 => "extended report rate",
        0x8100 => "onboard profiles",
        _ => null,
    };



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
