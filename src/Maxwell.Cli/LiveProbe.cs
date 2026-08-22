using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Listens on every interesting input interface at once and prints whatever arrives,
/// timestamped and tagged. This is the half of the dig that needs hands: tap the
/// microphone's mute, turn its knob, lift the pad off the dock, press a key — the
/// device that reacts identifies its own protocol.
///
/// Purely passive, same guarantee as the rest of the probes: nothing is written.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LiveProbe
{
    /// <summary>Interfaces worth a live ear: vendor pages, telephony, and consumer
    /// controls — the pages where state changes announce themselves.</summary>
    private static bool Interesting(HidInterface i) =>
        i.InputReportLength > 1
        && (i.UsagePage >= 0xFF00 && i.UsagePage != 0xFF00   // vendor, HID++ has its own probe
            || i.UsagePage == 0x000B                          // telephony: the mute of headsets
            || i.UsagePage == 0x000C);                        // consumer: media keys, knobs

    public static int Run()
    {
        List<HidInterface> targets = [.. new WindowsHidEnumerator().EnumerateAll().Where(Interesting)];

        Console.WriteLine("Слушаю 30 секунд. Действуйте:");
        Console.WriteLine("  · тапните mute на QuadCast, покрутите ручку gain");
        Console.WriteLine("  · снимите геймпад с дока (или поставьте)");
        Console.WriteLine("  · понажимайте клавиши на Air96, покрутите её колесо");
        Console.WriteLine();

        foreach (HidInterface i in targets)
        {
            Console.WriteLine($"  ухо: {i.VendorId:X4}:{i.ProductId:X4} page {i.UsagePage:X4}/{i.Usage:X2} in {i.InputReportLength}  {i.ProductName}");
        }

        Console.WriteLine();

        var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var console = new object();
        var last = new ConcurrentDictionary<string, string>();

        List<Task> ears = [.. targets.Select(iface => Task.Run(() =>
        {
            string tag = $"{iface.VendorId:X4}:{iface.ProductId:X4} {iface.UsagePage:X4}";

            try
            {
                using var channel = new WindowsHidChannel(iface.Path, iface.InputReportLength, iface.OutputReportLength);

                while (!stop.IsCancellationRequested)
                {
                    if (channel.Listen(TimeSpan.FromMilliseconds(700)) is not { } report) continue;

                    string line = string.Join(' ', report.Take(20).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

                    // A broadcaster repeating one frame a second is one line of news,
                    // not thirty: only changes get printed after the first.
                    if (last.TryGetValue(tag, out string? previous) && previous == line) continue;
                    last[tag] = line;

                    lock (console)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.f}] {tag}  {line}");
                    }
                }
            }
            catch (IOException e)
            {
                lock (console)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.f}] {tag}  не открылся: {e.Message}");
                }
            }
        }, stop.Token))];

        try
        {
            Task.WaitAll([.. ears], TimeSpan.FromSeconds(35));
        }
        catch (AggregateException)
        {
            // Cancellation surfacing as an aggregate is the expected end of the watch.
        }

        Console.WriteLine();
        Console.WriteLine("Готово. Повторяющиеся кадры печатались один раз — тишина после");
        Console.WriteLine("первой строки значит «шлёт то же самое», а не «замолчал».");

        return 0;
    }
}
