using System.Diagnostics;
using System.Runtime.Versioning;

using Kvazr.Race;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// The switch on the earcup and the sound Windows receives, side by side.
///
/// The meter says a muted Maxwell keeps streaming ordinary room noise, which reads as a
/// broken switch and is not one: the headset has a mute register of its own, read-only,
/// which Audeze HQ polls and which nothing here has ever asked for. Two claims, and no
/// way to compare them while they are printed by two different programs minutes apart.
///
/// So both go on one line, half a second at a time: what the headset says about its
/// microphone, and what arrived in the capture stream during the same half second. Flip
/// the switch during the run and the answer is on the screen — either the register moves
/// and the audio does not, which puts the mute inside the headset's own opinion, or
/// neither moves, and the switch is not the one we think it is.
///
/// Read only. The mute register has no write function, in this harness or in Audeze HQ.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MicSwitchProbe
{
    /// <summary>How often the capture buffer is drained. Packets arrive about every 10 ms.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    /// <summary>One printed line: short enough to catch a flip, long enough to read.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    public static async Task<int> RunAsync(int seconds, CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var factory = new WindowsHidTransportFactory();

        IReadOnlyList<MaxwellInterface> found = enumerator.EnumerateRaceInterfaces();

        if (found.Count == 0)
        {
            Console.WriteLine("Интерфейс Audeze RACE не найден — наушники выключены или не на связи.");
            return 1;
        }

        MaxwellInterface target = found[0];
        using var session = new RaceSession(factory.Open(target));

        Console.WriteLine($"наушники:  PID 0x{target.ProductId:X4}  {target.Transport}");

        var endpoints = new WindowsAudioEndpoints();

        AudioEndpoint? mic = endpoints.Enumerate()
            .FirstOrDefault(e => e.Flow == AudioFlow.Capture
                && !e.IsContainerless
                && e.Name.Contains("Maxwell", StringComparison.OrdinalIgnoreCase));

        IAudioLevelMeter? meter = null;

        if (mic is null)
        {
            Console.WriteLine("микрофон:  входа со словом Maxwell в имени нет — смотрим только регистр");
        }
        else
        {
            try
            {
                meter = endpoints.Listen(mic.Id);
                Console.WriteLine($"микрофон:  {mic.Name} — {meter.Format}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"микрофон:  {mic.Name} — поток не открылся: {ex.Message}");
            }
        }

        // The neighbours, once, with full patience. 0xFF out of the mute register is only
        // worth reading as an answer if the registers either side of it answer sensibly:
        // otherwise it is this transport shrugging, and the column below means nothing.
        foreach ((byte sub, string name) in new (byte, string)[]
        {
            (MaxwellFeatures.MicMute, "0x22 заглушка"),
            (MaxwellFeatures.NoiseReduction, "0x24 шумодав"),
            (MaxwellFeatures.MicLevel, "0x35 уровень"),
            (MaxwellFeatures.SidetoneLevel, "0x2C сайдтон"),
        })
        {
            int value = await session.ReadValueAsync(sub, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"  {name,-16} {(value < 0 ? "не ответил" : $"{value,3} (0x{value:X2})")}");
        }

        Console.WriteLine();
        Console.WriteLine($"Щёлкайте переключателем микрофона на наушниках. {seconds} с.");
        Console.WriteLine();
        Console.WriteLine("   время   регистр 0x22        что пришло в Windows");
        Console.WriteLine("   -----   -----------------   --------------------------------");

        using (meter)
        {
            var clock = Stopwatch.StartNew();
            TimeSpan span = TimeSpan.FromSeconds(seconds);

            (long packets, long silent) before = meter?.Tally ?? (0, 0);

            while (clock.Elapsed < span)
            {
                double loudest = 0;
                TimeSpan edge = clock.Elapsed + Window;

                // Drained often, printed rarely: a peak read once every half second is a
                // peak of whatever the last buffer happened to hold, not of the window.
                while (clock.Elapsed < edge && clock.Elapsed < span)
                {
                    if (meter?.Read() is { } peak) loudest = Math.Max(loudest, peak);
                    await Task.Delay(Tick, cancellationToken).ConfigureAwait(false);
                }

                // One attempt, not four: a dropped reply should leave a gap in the column
                // rather than stall the line it belongs on.
                int raw = await session
                    .ReadValueAsync(RaceCommands.Read, MaxwellFeatures.MicMute, 1, 200, cancellationToken)
                    .ConfigureAwait(false);

                (long packets, long silent) now = meter?.Tally ?? (0, 0);
                long arrived = now.packets - before.packets;
                long empty = now.silent - before.silent;
                before = now;

                Console.WriteLine(
                    $"   {clock.Elapsed.TotalSeconds,4:0.0}s   {Register(raw),-17}   {Stream(meter, loudest, arrived, empty)}");
            }
        }

        return 0;
    }

    private static string Register(int raw) => raw switch
    {
        < 0 => "— не ответил",
        0 => "0x00 включён",
        1 => "0x01 ЗАГЛУШЁН",
        0xFF => "0xFF нет штанги",
        _ => $"0x{raw:X2} ?",
    };

    private static string Stream(IAudioLevelMeter? meter, double loudest, long packets, long empty)
    {
        if (meter is null) return "—";
        if (packets == 0) return "тишина в эфире: пакеты не идут";

        string level = loudest <= 0 ? "  −∞ dB" : $"{20 * Math.Log10(loudest),5:0.0} dB";

        return $"{level}   пакетов {packets,3}, пустых {empty,3}"
            + (empty == packets ? "   ← цифровая тишина" : string.Empty);
    }
}
