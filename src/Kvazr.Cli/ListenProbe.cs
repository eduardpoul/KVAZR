using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Listens without asking, to find out whether the headset says anything on its own.
///
/// This decides how the application should get its data. Everything today is polled: the
/// panel asks for a value, waits, and asks again in ten seconds — so a wheel turned on the
/// earcup shows up whenever the next sweep happens to run. If the device pushes its own
/// notifications, that whole arrangement is wrong and the fix is to subscribe rather than
/// to poll faster.
///
/// Sends nothing at all: any packet seen here arrived unprompted.
/// </summary>
internal static class ListenProbe
{
    private static string Name(MaxwellInterface face) => face.UsagePage switch
    {
        0x000C => "consumer controls — volume, the wheel",
        0x000B => "telephony — mute, hook",
        0xFF13 => "RACE — settings",
        _ => "unknown",
    };

    public static async Task<int> RunAsync(int seconds, CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var factory = new WindowsHidTransportFactory();

        // Every interface the headset offers, not only the vendor one.
        //
        // RACE is where settings are read and written, and it answers only when asked. The
        // wheel and the mute button are ordinary HID controls and belong to the consumer
        // and telephony pages instead — so listening on RACE alone proves nothing about
        // whether the device announces anything, which is the question that decides
        // whether this application should subscribe or keep polling.
        List<MaxwellInterface> found = [.. enumerator.Enumerate()];
        if (found.Count == 0)
        {
            Console.WriteLine("No Audeze interface found.");
            return 1;
        }

        Console.WriteLine($"listening on {found.Count} interfaces for {seconds}s — nothing is being sent.");
        foreach (MaxwellInterface each in found)
        {
            Console.WriteLine($"   PID 0x{each.ProductId:X4}  usage {each.UsagePage:X4}:{each.Usage:X2}"
                              + $"  in {each.InputReportLength,3}  {Name(each)}");
        }

        Console.WriteLine();
        Console.WriteLine("Turn the wheel on the earcup, lift the boom, press mute.");
        Console.WriteLine();

        var seen = new Dictionary<string, int>();
        var channels = new List<(MaxwellInterface Face, WindowsHidChannel Channel)>();

        foreach (MaxwellInterface each in found)
        {
            try
            {
                channels.Add((each, new WindowsHidChannel(each.Path, each.InputReportLength, each.OutputReportLength)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   could not open {each.UsagePage:X4}: {ex.Message}");
            }
        }

        long deadline = Environment.TickCount64 + (seconds * 1000);
        var watchers = channels.Select(pair => Task.Run(() =>
        {
            while (Environment.TickCount64 < deadline)
            {
                byte[]? report = pair.Channel.Listen(TimeSpan.FromMilliseconds(400));
                if (report is null) continue;

                string key = $"{pair.Face.ProductId:X4} {pair.Face.UsagePage:X4}:{pair.Face.Usage:X2}";
                lock (seen) seen[key] = seen.GetValueOrDefault(key) + 1;

                    Console.WriteLine($"  +{Environment.TickCount64 % 100000,6} ms  {key}  "
                                  + Convert.ToHexString(report.AsSpan(0, Math.Min(16, report.Length))));
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(watchers).ConfigureAwait(false);
        foreach ((_, WindowsHidChannel channel) in channels) channel.Dispose();

        Console.WriteLine();
        Console.WriteLine(seen.Count == 0
            ? "Nothing arrived unprompted: the device only answers when asked, and polling is the only option."
            : "Unprompted traffic, by command:");

        foreach ((string face, int count) in seen.OrderByDescending(p => p.Value))
        {
            Console.WriteLine($"   usage {face}  ×{count}");
        }

        return 0;
    }
}
