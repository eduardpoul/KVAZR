using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// What a Logitech receiver remembers about its pairings, laid beside what a device on its
/// own cable says about itself.
///
/// The question behind it: with the mouse on its charging cable the receiver reports
/// nothing connected, the driver has nothing to say about it, and the system tier's bare
/// "USB Receiver" comes back into the list — next to a mouse that by then has two rows of
/// its own, one per route. A receiver whose pairing table still names the mouse while it is
/// away, and names it by the same unit id the mouse gives over the cable, is what would let
/// one device own both of its routes and keep the receiver under it.
///
/// Two things are not known, and this prints enough to settle both. Whether a Lightspeed
/// receiver keeps the Unifying pairing registers at all. And where the answer arrives: a
/// long register is asked with a short report and answered with a long one, and on Windows
/// those are two device paths — so the question goes out on one and the answer is listened
/// for on the other, and failing that the long report is tried on its own path.
///
/// Read-only throughout: HID++ 1.0 register reads (0x81, 0x83) and the first function of
/// feature 0x0003. The register writes are 0x80 and 0x82, and neither appears here.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ReceiverPairingProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort ShortUsage = 0x01;
    private const ushort LongUsage = 0x02;

    private const byte GetRegister = 0x81;
    private const byte GetLongRegister = 0x83;
    private const byte Refusal = 0x8F;

    private const byte Connections = 0x02;
    private const byte ReceiverInfo = 0xB5;

    // Sub-registers of 0xB5. The per-slot ones are counted from slot one at zero.
    private const byte AboutReceiver = 0x03;
    private const byte Pairing = 0x20;
    private const byte ExtendedPairing = 0x30;
    private const byte DeviceName = 0x40;

    private const ushort DeviceInformation = 0x0003;

    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(600);

    /// <summary>One turn on each path while waiting for an answer that may come on either.</summary>
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(40);

    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(20);

    private enum Route
    {
        None,
        ShortAsked,
        LongAsked,
    }

    /// <summary>A slot as the receiver's table describes it, whether or not anything is on the air.</summary>
    private sealed record Remembered(ushort Receiver, byte Slot, string? Wpid, string? Serial, string? Name);

    /// <summary>A device answering on its own index, and the ids it gives for itself.</summary>
    private sealed record Cabled(ushort Product, string Unit, string? RadioPid);

    public static int Run()
    {
        List<HidInterface> faces =
        [
            .. new WindowsHidEnumerator().EnumerateAll()
                .Where(i => i.VendorId == Logitech && i.UsagePage == HidPlusPlusPage),
        ];

        if (faces.Count == 0)
        {
            Console.WriteLine("No HID++ interface found.");
            return 1;
        }

        Console.WriteLine("[receiver] Таблицы сопряжений ресиверов Logitech и кто на кабеле");
        Console.WriteLine();

        var remembered = new List<Remembered>();
        var cabled = new List<Cabled>();

        foreach (IGrouping<ushort, HidInterface> product in faces.GroupBy(i => i.ProductId))
        {
            HidInterface? shortFace = product.FirstOrDefault(i => i.Usage == ShortUsage);
            HidInterface? longFace = product.FirstOrDefault(i => i.Usage == LongUsage && i.OutputReportLength >= 20);

            Console.WriteLine($"{product.First().ProductName}  {Logitech:X4}:{product.Key:X4}");

            if (shortFace is null || longFace is null)
            {
                Console.WriteLine($"  нет {(shortFace is null ? "короткой" : "длинной")} коллекции — пропускаю");
                Console.WriteLine();
                continue;
            }

            try
            {
                using var shortChannel = new WindowsHidChannel(
                    shortFace.Path, shortFace.InputReportLength, shortFace.OutputReportLength);
                using var longChannel = new WindowsHidChannel(
                    longFace.Path, longFace.InputReportLength, longFace.OutputReportLength);

                byte[]? connections = ReadShort(shortChannel, Connections);

                if (connections is null || HidPlusPlus.IsError(connections))
                {
                    // The same test the driver uses to tell a cable from a receiver.
                    Console.WriteLine(
                        $"  регистр 0x02        {(connections is null ? "молчит" : HidPlusPlus.Explain(connections))}"
                        + " — не ресивер, устройство на своём кабеле");

                    if (Identity(longChannel, product.Key) is { } identity) cabled.Add(identity);
                }
                else
                {
                    Console.WriteLine($"  регистр 0x02        {Hex(connections, 4, 3)}   подключено сейчас: {connections[5]}");
                    remembered.AddRange(Pairings(shortChannel, longChannel, product.Key));
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"  не открылся: {ex.Message}");
            }

            Console.WriteLine();
        }

        Compare(remembered, cabled);

        return 0;
    }

    /// <summary>
    /// The receiver's own description, then three registers per slot: the pairing (radio
    /// pid and kind), the extended pairing (serial), and the name. Asked of every slot
    /// whatever the connection count says — what is on the air is not the question here.
    /// </summary>
    private static List<Remembered> Pairings(WindowsHidChannel shortChannel, WindowsHidChannel longChannel, ushort receiver)
    {
        var found = new List<Remembered>();
        var routes = new Dictionary<Route, int>();

        byte[]? Read(byte sub)
        {
            (byte[]? reply, Route route) = ReadLong(shortChannel, longChannel, sub);
            routes[route] = routes.GetValueOrDefault(route) + 1;
            return reply;
        }

        Console.WriteLine($"  B5 03 о себе        {Said(Read(AboutReceiver))}");

        for (byte slot = 1; slot <= 6; slot++)
        {
            byte n = (byte)(slot - 1);

            byte[]? pairing = Read((byte)(Pairing + n));
            byte[]? extended = Read((byte)(ExtendedPairing + n));
            byte[]? named = Read((byte)(DeviceName + n));

            // Pairing: interval at 6, radio pid at 7–8, kind in the low nibble of 11.
            // Extended: serial at 5–8. Name: length at 5, the characters after it.
            string? wpid = Usable(pairing) ? Convert.ToHexString(pairing!, 7, 2) : null;
            if (wpid == "0000") wpid = null;

            string? serial = Usable(extended) ? Convert.ToHexString(extended!, 5, 4) : null;
            if (serial is "00000000" or "FFFFFFFF") serial = null;

            string? name = Usable(named) ? Name(named!) : null;

            Console.WriteLine($"  slot {slot}");
            Console.WriteLine($"    B5 {Pairing + n:X2}  {Said(pairing)}");
            if (wpid is not null)
            {
                int kind = pairing![11] & 0x0F;
                Console.WriteLine($"           радио-pid {wpid} · вид {kind}{Kind(kind)} · интервал {pairing[6]} мс");
            }

            Console.WriteLine($"    B5 {ExtendedPairing + n:X2}  {Said(extended)}");
            if (serial is not null) Console.WriteLine($"           серийный {serial}");

            Console.WriteLine($"    B5 {DeviceName + n:X2}  {Said(named)}");
            if (name is not null) Console.WriteLine($"           имя «{name}»");

            if (wpid is not null || serial is not null) found.Add(new Remembered(receiver, slot, wpid, serial, name));
        }

        Console.WriteLine(
            $"  ответы: на короткий запрос {routes.GetValueOrDefault(Route.ShortAsked)}"
            + $", на длинный {routes.GetValueOrDefault(Route.LongAsked)}"
            + $", без ответа {routes.GetValueOrDefault(Route.None)}");

        return found;
    }

    /// <summary>
    /// What a device on its own index says it is: feature 0x0003, function zero. The unit
    /// id is the same number <c>PointerReader</c> calls the serial; the product ids after it
    /// are one per transport the device speaks, and the radio one is what a receiver's
    /// pairing table would hold.
    /// </summary>
    private static Cabled? Identity(WindowsHidChannel channel, ushort product)
    {
        // The register question asked a moment ago is answered by a mouse with a HID++ 2.0
        // error — on this, the long path, which was already open to hear it. Errors count
        // as answers, so left in the buffer it would be taken as the reply to the next
        // question and the feature would look absent.
        Drain(channel);

        if (HidPlusPlusReader.FeatureIndex(channel, HidPlusPlus.Receiver, DeviceInformation) is not { } index)
        {
            string why = channel.LastWriteFailed
                ? $"запись отклонена, ошибка {channel.LastWriteError}"
                : channel.LastReply is { } last ? "последнее услышанное: " + Hex(last, 0, 8) : "ответа нет";

            Console.WriteLine($"  0x0003 не найдена — {why}");
            return null;
        }

        byte[]? info = HidPlusPlusReader.Ask(channel, HidPlusPlus.Receiver, index, 0x00);

        Console.WriteLine($"  0x0003 fn0          {Said(info)}");

        if (info is null || HidPlusPlus.IsError(info) || info.Length < 17) return null;

        // Entity count at 4, unit id at 5–8, transports at 9–10, then up to three product
        // ids in a fixed order of transports: Bluetooth, low-energy, the receiver radio, USB.
        string unit = Convert.ToHexString(info, 5, 4);
        int transports = info[10];

        var pids = new List<(string Transport, string Pid)>();
        int offset = 11;

        foreach ((int bit, string transport) in new (int, string)[] { (0x01, "bt"), (0x02, "ble"), (0x04, "радио"), (0x08, "usb") })
        {
            if ((transports & bit) == 0) continue;
            if (offset > 15) break;

            pids.Add((transport, Convert.ToHexString(info, offset, 2)));
            offset += 2;
        }

        Console.WriteLine(
            $"           unit id {unit} · транспорты 0x{transports:X2}: "
            + string.Join(", ", pids.Select(p => $"{p.Transport} {p.Pid}")));

        return new Cabled(product, unit, pids.FirstOrDefault(p => p.Transport == "радио").Pid);
    }

    private static void Compare(List<Remembered> remembered, List<Cabled> cabled)
    {
        Console.WriteLine("Сверка");

        if (remembered.Count == 0)
        {
            Console.WriteLine("  ни один ресивер не назвал сопряжённых — по таблице сопряжений связывать нечего");
            return;
        }

        foreach (Remembered slot in remembered)
        {
            Console.WriteLine(
                $"  {Logitech:X4}:{slot.Receiver:X4} slot {slot.Slot}"
                + $"  радио-pid {slot.Wpid ?? "?"}  серийный {slot.Serial ?? "?"}  {slot.Name}");

            Cabled? bySerial = slot.Serial is null ? null : cabled.FirstOrDefault(c => c.Unit == slot.Serial);
            Cabled? byRadio = slot.Wpid is null ? null : cabled.FirstOrDefault(c => c.RadioPid == slot.Wpid);

            Console.WriteLine(
                bySerial is not null
                    ? $"    = {Logitech:X4}:{bySerial.Product:X4} на кабеле, совпал unit id"
                    : byRadio is not null
                        ? $"    ≈ {Logitech:X4}:{byRadio.Product:X4} на кабеле, совпал только радио-pid (unit id {byRadio.Unit})"
                        : "    на кабеле такого нет");
        }
    }

    /// <summary>A short register, asked and answered on the short path.</summary>
    private static byte[]? ReadShort(WindowsHidChannel channel, byte register)
    {
        Drain(channel);

        return channel.Request(
            Question(channel.OutputLength, GetRegister, register, 0x00),
            r => r.Length >= 6
                && r[1] == HidPlusPlus.Receiver
                && ((r[2] == GetRegister && r[3] == register) || HidPlusPlus.IsError(r)),
            Wait);
    }

    /// <summary>
    /// One long register. The answer, or the receiver's refusal, and which question got it.
    ///
    /// Short question first, because that is how the protocol asks it. Its answer is a long
    /// report and so lands on the long path, while a refusal is a short report and lands on
    /// the short one — so both are listened to in turns until one of them speaks.
    /// </summary>
    private static (byte[]? Reply, Route Route) ReadLong(WindowsHidChannel shortChannel, WindowsHidChannel longChannel, byte sub)
    {
        Drain(shortChannel);
        Drain(longChannel);

        if (shortChannel.Send(Question(shortChannel.OutputLength, GetLongRegister, ReceiverInfo, sub))
            && Either(longChannel, shortChannel, sub) is { } answer)
        {
            return (answer, Route.ShortAsked);
        }

        if (longChannel.Send(Question(longChannel.OutputLength, GetLongRegister, ReceiverInfo, sub))
            && Either(longChannel, shortChannel, sub) is { } other)
        {
            return (other, Route.LongAsked);
        }

        return (null, Route.None);
    }

    private static byte[]? Either(WindowsHidChannel longChannel, WindowsHidChannel shortChannel, byte sub)
    {
        DateTime deadline = DateTime.UtcNow + Wait;

        while (DateTime.UtcNow < deadline)
        {
            if (longChannel.Listen(Slice) is { } heard && Answers(heard, sub)) return heard;
            if (shortChannel.Listen(Slice) is { } said && Refuses(said)) return said;
        }

        return null;
    }

    private static bool Answers(byte[] r, byte sub) =>
        r.Length >= 7
        && r[0] == HidPlusPlus.LongReport
        && r[1] == HidPlusPlus.Receiver
        && r[2] == GetLongRegister
        && r[3] == ReceiverInfo
        && r[4] == sub;

    /// <summary>
    /// A refusal does not repeat the sub-register, so one cannot be told from another —
    /// which is why both paths are drained before every question.
    /// </summary>
    private static bool Refuses(byte[] r) =>
        r.Length >= 6
        && r[1] == HidPlusPlus.Receiver
        && r[2] == Refusal
        && r[3] == GetLongRegister
        && r[4] == ReceiverInfo;

    private static void Drain(WindowsHidChannel channel)
    {
        for (int i = 0; i < 64 && channel.Listen(Brief) is not null; i++)
        {
        }
    }

    private static byte[] Question(int size, byte subId, byte register, byte parameter)
    {
        byte[] report = new byte[size];

        report[0] = size >= 20 ? HidPlusPlus.LongReport : HidPlusPlus.ShortReport;
        report[1] = HidPlusPlus.Receiver;
        report[2] = subId;
        report[3] = register;
        report[4] = parameter;

        return report;
    }

    private static bool Usable(byte[]? reply) => reply is { Length: >= 17 } && !HidPlusPlus.IsError(reply);

    private static string? Name(byte[] reply)
    {
        int length = Math.Min(reply[5], reply.Length - 6);
        if (length <= 0) return null;

        string read = string.Concat(reply.Skip(6).Take(length).Select(b => (char)b));

        return read.All(c => c is >= ' ' and <= '~') ? read : null;
    }

    /// <summary>Named only where there is no doubt; the number is the measurement.</summary>
    private static string Kind(int kind) => kind switch
    {
        0x01 => " (клавиатура)",
        0x02 => " (мышь)",
        _ => string.Empty,
    };

    private static string Said(byte[]? reply) =>
        reply is null ? "нет ответа"
        : HidPlusPlus.IsError(reply) ? $"отказ: {HidPlusPlus.Explain(reply)}"
        : Hex(reply, 4, reply.Length - 4);

    private static string Hex(byte[] bytes, int start, int count) =>
        string.Join(' ', bytes.Skip(start).Take(count).Select(b => b.ToString("X2")));
}
