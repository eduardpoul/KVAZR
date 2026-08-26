using System.Diagnostics;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// What the switch on the page actually does to Windows' mute, and when.
///
/// The switch was reported flicking back on for about a second after being turned off,
/// and that has two quite different causes which look identical from the page: either
/// the application writes the flag twice — a stale reading flipping the control, and the
/// control writing what it was flipped to — or it writes once and merely draws the wrong
/// thing for a moment. The first is a bug in the write path, the second in the read
/// path, and guessing between them is how an afternoon goes.
///
/// So this watches the flag itself, twenty times a second, and prints only the moments
/// it changes — alongside what the stream is doing, since the stream is what the page
/// believes. Click the switch while it runs.
///
/// Read only. The flag is watched, never written.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MuteWatchProbe
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    /// <summary>Empty packets in a row before the stream is called silent — the same quarter-second the page uses.</summary>
    private const int MutedAfter = 25;

    public static async Task<int> RunAsync(int seconds, string? only, CancellationToken cancellationToken = default)
    {
        var endpoints = new WindowsAudioEndpoints();

        AudioEndpoint? mic = endpoints.Enumerate()
            .Where(e => e.Flow == AudioFlow.Capture && !e.IsContainerless)
            .FirstOrDefault(e => only is not { Length: > 0 }
                || e.Name.Contains(only, StringComparison.OrdinalIgnoreCase));

        if (mic is null)
        {
            Console.WriteLine($"Микрофона со словом «{only}» в имени не нашлось.");
            return 1;
        }

        Console.WriteLine(mic.Name);

        using IAudioEndpointChannel channel = endpoints.Open(mic.Id);
        using IAudioLevelMeter meter = endpoints.Listen(mic.Id);

        Console.WriteLine($"  {meter.Format}");
        Console.WriteLine();
        Console.WriteLine($"Щёлкайте переключателем микрофона в приложении. {seconds} с.");
        Console.WriteLine("Печатаю только моменты, когда что-то меняется.");
        Console.WriteLine();
        Console.WriteLine("   время    галка Windows   поток");
        Console.WriteLine("   -------  -------------   -----");

        bool? flag = channel.Mute;
        bool silent = false;
        long run = 0;
        (long packets, long empty) seen = meter.Tally;

        Console.WriteLine($"   {0.0,6:0.0}s   {Say(flag),-13}   {(silent ? "тишина" : "звук")}   (начало)");

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await Task.Delay(Tick, cancellationToken).ConfigureAwait(false);

            meter.Read();

            (long packets, long empty) now = meter.Tally;
            long arrived = now.packets - seen.packets;
            long blank = now.empty - seen.empty;
            seen = now;

            if (arrived > 0)
            {
                if (blank < arrived) run = 0;
                else run += blank;

                bool quiet = run >= MutedAfter;

                if (quiet != silent)
                {
                    silent = quiet;
                    Console.WriteLine($"   {clock.Elapsed.TotalSeconds,6:0.0}s   {Say(channel.Mute),-13}   поток → {(quiet ? "ТИШИНА" : "ЗВУК")}");
                }
            }

            bool? current = channel.Mute;

            if (current != flag)
            {
                flag = current;
                Console.WriteLine($"   {clock.Elapsed.TotalSeconds,6:0.0}s   галка → {Say(current),-6}   {(silent ? "тишина" : "звук")}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Готово. Одна смена галки на щелчок — приложение пишет один раз.");
        Console.WriteLine("Две подряд — значит вторая пришла от самого приложения, и чинить надо запись.");

        return 0;
    }

    private static string Say(bool? value) => value switch
    {
        true => "заглушён",
        false => "открыт",
        _ => "—",
    };
}
