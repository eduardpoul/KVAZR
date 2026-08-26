using System.Diagnostics;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Whether the recording level slider does anything to the sound that arrives.
///
/// Windows offers a capture volume on every endpoint, and offering it is not the same as
/// applying it: on some devices it is a hardware gain, on some a software scale, and on
/// some it is a number the endpoint keeps and nothing reads. A slider of the third kind
/// belongs on no page, and the difference is not visible from the interface.
///
/// So this sets the level to a few values and measures the floor at each. Doubling the
/// level should lift the measured floor by about six decibels; a floor that does not move
/// means the slider is decoration.
///
/// Writes the level and puts it back. Nothing else is touched.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MicGainProbe
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(string? only, CancellationToken cancellationToken = default)
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

        int? original = channel.Volume;
        Console.WriteLine($"  {meter.Format}   было {original?.ToString() ?? "—"}%");
        Console.WriteLine();
        Console.WriteLine("  Сидите тихо: меряю собственный шум микрофона на каждом уровне.");
        Console.WriteLine();
        Console.WriteLine($"   {"уровень",-9} {"пик",-10} {"средний",-10}");
        Console.WriteLine($"   {new string('-', 9)} {new string('-', 10)} {new string('-', 10)}");

        try
        {
            foreach (int percent in (int[])[0, 20, 40, 60, 80, 100])
            {
                if (!channel.SetVolume(percent))
                {
                    Console.WriteLine($"   {percent,3}%      запись не принята");
                    continue;
                }

                // The engine applies a level change on a buffer boundary, and the first
                // packets after it are still the old gain. Half a second is thrown away
                // before anything is counted.
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                meter.Read();

                var seen = new List<double>();
                var clock = Stopwatch.StartNew();

                while (clock.Elapsed < Settle)
                {
                    if (meter.Read() is { } peak) seen.Add(peak);
                    await Task.Delay(Tick, cancellationToken).ConfigureAwait(false);
                }

                Console.WriteLine(
                    $"   {percent,3}%      {Decibels(seen.Count == 0 ? 0 : seen.Max()),-10} "
                    + $"{Decibels(seen.Count == 0 ? 0 : seen.Average()),-10} (замер {channel.Volume?.ToString() ?? "—"}%)");
            }
        }
        finally
        {
            if (original is { } back)
            {
                channel.SetVolume(back);
                Console.WriteLine();
                Console.WriteLine($"  вернул {channel.Volume?.ToString() ?? "—"}%");
            }
        }

        return 0;
    }

    private static string Decibels(double fraction) =>
        fraction <= 0 ? "  −∞ dB" : $"{20 * Math.Log10(fraction),5:0.0} dB";
}
