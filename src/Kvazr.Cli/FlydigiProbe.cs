using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Asks a Flydigi pad what it is and how charged, over the vendor page.
///
/// The bytes are SDL's, which carries a driver for Flydigi's second protocol generation
/// and reads the Apex 5's battery with it: a command report <c>03 5A A5 cmd …</c>, and an
/// answer that repeats <c>5A A5 cmd</c>. Get-info (0x01) answers with the device id at 5,
/// the link at 6 and the charge at 11 — state in the high nibble, fifths of a full charge
/// in the low one. Get-status (0x10) says at 9 whether a third party may take the pad
/// over.
///
/// Only those two, both questions. The command that takes the pad over (0x1C) changes
/// what it sends and to whom, and nothing here needs it.
///
/// It also overturns a reading from this desk: the dock's "two charges", 57 and 55, sit
/// in a frame that begins <c>5A A5 EF</c>, and 0xEF is the pad's input report — sticks and
/// buttons. Those were axes. So the first thing done here is listen, and print what each
/// interface says unprompted, before anything is asked.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FlydigiProbe
{
    private const ushort Flydigi = 0x37D7;
    private const ushort VendorPage = 0xFFA0;

    private const byte Magic1 = 0x5A;
    private const byte Magic2 = 0xA5;
    private const byte GetInfo = 0x01;
    private const byte GetStatus = 0x10;
    private const byte InputReport = 0xEF;

    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(800);

    public static int Run()
    {
        List<HidInterface> faces =
        [
            .. new WindowsHidEnumerator().EnumerateAll()
                .Where(i => i.VendorId == Flydigi && i.UsagePage == VendorPage && i.OutputReportLength > 0),
        ];

        if (faces.Count == 0)
        {
            Console.WriteLine("No Flydigi vendor interface (page 0xFFA0) found.");
            return 1;
        }

        Console.WriteLine("[flydigi] Вендорская страница 0xFFA0: что говорит сама и что отвечает");
        Console.WriteLine();

        foreach (HidInterface face in faces)
        {
            Console.WriteLine($"{face.ProductName}  {face.VendorId:X4}:{face.ProductId:X4}  in {face.InputReportLength} out {face.OutputReportLength}");

            try
            {
                using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

                Listen(channel);
                Ask(channel, GetInfo, [0x02, 0x00]);
                Ask(channel, GetStatus, []);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"  не открылся: {ex.Message}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>A second of whatever the interface volunteers, counted, with the first few shown raw.</summary>
    private static void Listen(WindowsHidChannel channel)
    {
        var heard = new List<byte[]>();
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);

        while (DateTime.UtcNow < deadline)
        {
            if (channel.Listen(TimeSpan.FromMilliseconds(200)) is { } report) heard.Add(report);
        }

        Console.WriteLine($"  сам за секунду: {heard.Count} отчётов"
            + (heard.Count == 0 ? string.Empty : $", из них входных (EF): {heard.Count(r => Command(r) == InputReport)}"));

        foreach (byte[] report in heard.Take(3)) Console.WriteLine($"    {Hex(report, 0, 24)}");
    }

    /// <summary>
    /// One question, under each report id it could be framed with. SDL sends 0x03; a
    /// collection that declares no report ids takes 0x00 in that place instead, and only
    /// the write itself says which this one is.
    /// </summary>
    private static void Ask(WindowsHidChannel channel, byte command, byte[] parameters)
    {
        string name = command == GetInfo ? "get-info 0x01" : "get-status 0x10";

        foreach (byte reportId in (byte[])[0x03, 0x00])
        {
            byte[] request = new byte[channel.OutputLength];
            request[0] = reportId;
            request[1] = Magic1;
            request[2] = Magic2;
            request[3] = command;
            for (int i = 0; i < parameters.Length && i + 4 < request.Length; i++) request[i + 4] = parameters[i];

            byte[]? reply = channel.Request(request, r => Command(r) == command, Wait);

            if (channel.LastWriteFailed)
            {
                Console.WriteLine($"  {name} [id {reportId:X2}]  запись отклонена, ошибка {channel.LastWriteError}");
                continue;
            }

            if (reply is null)
            {
                Console.WriteLine($"  {name} [id {reportId:X2}]  ответа нет");
                return;
            }

            ReadOnlySpan<byte> data = Body(reply);
            Console.WriteLine($"  {name} [id {reportId:X2}]  {Hex(reply, 0, 32)}");

            if (command == GetInfo) Info(data);
            else Console.WriteLine($"      доступен для сторонних: {(data.Length > 9 ? data[9].ToString() : "?")} (1 — да)");

            return;
        }
    }

    /// <summary>What get-info says, laid out the way SDL reads it, from the first magic byte.</summary>
    private static void Info(ReadOnlySpan<byte> data)
    {
        if (data.Length < 17)
        {
            Console.WriteLine("      короче, чем ждали");
            return;
        }

        int state = (data[11] >> 4) & 0x0F;
        int level = data[11] & 0x0F;

        string charge = state switch
        {
            0 => $"{level * 20}% от батареи",
            1 => $"{level * 20}% заряжается",
            2 => "заряжен",
            _ => $"состояние {state}, уровень {level}",
        };

        Console.WriteLine($"      устройство {data[5]} (Apex 5 — 128/129/133/134) · связь {data[6]} · прошивка {data[16]:X2}{data[15]:X2}");
        Console.WriteLine($"      заряд: байт 11 = 0x{data[11]:X2} → {charge}");
    }

    /// <summary>The command byte, wherever the magic starts: right after a report id, or at the front.</summary>
    private static int Command(byte[] report)
    {
        for (int at = 0; at <= 1 && at + 2 < report.Length; at++)
        {
            if (report[at] == Magic1 && report[at + 1] == Magic2) return report[at + 2];
        }

        return -1;
    }

    private static ReadOnlySpan<byte> Body(byte[] report) =>
        report.Length > 1 && report[0] != Magic1 ? report.AsSpan(1) : report.AsSpan();

    private static string Hex(byte[] bytes, int start, int count) =>
        string.Join(' ', bytes.Skip(start).Take(count).Select(b => b.ToString("X2")));
}
