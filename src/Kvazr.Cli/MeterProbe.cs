using System.Diagnostics;
using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Watches the input level of every microphone on the machine, through the same code the
/// application uses.
///
/// One question, and it decides the shape of the whole microphone page: does the Core
/// Audio meter move on its own?
///
/// The meter belongs to the audio engine, and the engine only runs on an endpoint while
/// something is streaming through it. If that is so, a level meter costs nothing while
/// somebody is in a call and reads a flat zero the rest of the time — and a flat zero
/// beside a microphone that plainly works is worse than no meter. The alternative is to
/// open a capture stream ourselves, which is honest but lights the microphone-in-use
/// indicator in the taskbar for as long as the page is open.
///
/// So this prints a trace rather than a number: several seconds of samples per endpoint,
/// with nothing else recording, and then the answer is a fact rather than an opinion.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MeterProbe
{
    private static readonly TimeSpan Sample = TimeSpan.FromMilliseconds(100);

    public static async Task<int> RunAsync(int seconds, string? only)
    {
        var endpoints = new WindowsAudioEndpoints();

        List<AudioEndpoint> capture =
        [
            .. endpoints.Enumerate()
                .Where(e => e.Flow == AudioFlow.Capture && !e.IsContainerless)
                .Where(e => only is not { Length: > 0 }
                    || e.Name.Contains(only, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name, StringComparer.Ordinal),
        ];

        if (capture.Count == 0)
        {
            Console.WriteLine(only is { Length: > 0 }
                ? $"Микрофона со словом «{only}» в имени не нашлось."
                : "Микрофонов не найдено.");

            return 1;
        }

        Console.WriteLine($"Слушаем {capture.Count} вход(а) по {seconds} с, шаг {Sample.TotalMilliseconds:0} мс.");
        Console.WriteLine("Говорите в микрофон. Первое число — сейчас, второе — самое громкое за всё время.");
        Console.WriteLine();

        foreach (AudioEndpoint endpoint in capture)
        {
            await WatchAsync(endpoints, endpoint, seconds).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task WatchAsync(
        WindowsAudioEndpoints endpoints, AudioEndpoint endpoint, int seconds)
    {
        Console.WriteLine(endpoint.Name);

        IAudioEndpointChannel channel;

        try
        {
            channel = endpoints.Open(endpoint.Id);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  не открылся: {ex.Message}");
            Console.WriteLine();
            return;
        }

        IAudioLevelMeter? listening = null;

        try
        {
            listening = endpoints.Listen(endpoint.Id);
            Console.WriteLine($"  слушаем поток: {listening.Format} — индикатор микрофона горит");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  поток не открылся: {ex.Message} — читаем метр движка");
        }

        using (channel)
        using (listening)
        {
            Console.WriteLine($"  громкость {channel.Volume?.ToString() ?? "—"}%   mute {Say(channel.Mute)}");

            var seen = new List<double>();
            var clock = Stopwatch.StartNew();

            while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                double? peak = listening is null ? channel.Peak / 100.0 : listening.Read();

                if (peak is null)
                {
                    Console.WriteLine("  метр не отвечает");
                    break;
                }

                seen.Add(peak.Value);

                // The loudest so far, kept on the line. A meter you have to be looking at
                // at the right instant is a meter that misses the one shout you gave it.
                double loudest = seen.Max();

                Console.Write(
                    $"\r  {Bar(peak.Value)} {peak.Value * 100,6:0.00}%  {Decibels(peak.Value),8}"
                    + $"   пик {loudest * 100,6:0.00}%  {Decibels(loudest),8}   ");

                await Task.Delay(Sample).ConfigureAwait(false);
            }

            Console.WriteLine();

            if (seen.Count > 0)
            {
                int moved = seen.Count(v => v > 0);
                Console.WriteLine(
                    $"  за {seen.Count} проб: максимум {seen.Max() * 100:0.000}% ({Decibels(seen.Max())}), " +
                    $"ненулевых {moved}");

                if (listening is { } meter)
                {
                    (long packets, long silent) = meter.Tally;

                    Console.WriteLine(
                        $"  пакетов {packets}, из них пустых {silent} — " +
                        (packets == 0
                            ? "поток открыт, но ничего не приходит"
                            : silent == packets
                                ? "звук идёт, и в нём цифровая тишина: так выглядит микрофон, заглушённый своей кнопкой"
                                : "в потоке есть звук"));
                }
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Drawn on a decibel scale, because an amplitude scale is unreadable: ordinary speech
    /// peaks at about a tenth of full scale and would sit in the first four characters of
    /// the bar, indistinguishable from a quiet room.
    /// </summary>
    private static string Bar(double fraction)
    {
        const int Width = 40;
        const double Floor = -60;

        double db = fraction <= 0 ? Floor : 20 * Math.Log10(fraction);
        int filled = (int)Math.Round(Math.Clamp((db - Floor) / -Floor, 0, 1) * Width);

        return "[" + new string('#', filled) + new string('·', Width - filled) + "]";
    }

    private static string Decibels(double fraction) =>
        fraction <= 0 ? "  −∞ dB" : $"{20 * Math.Log10(fraction),5:0.0} dB";

    private static string Say(bool? value) => value switch
    {
        true => "да",
        false => "нет",
        _ => "—",
    };
}
