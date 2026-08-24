using System.Diagnostics;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// What Windows' own mute does to the stream we listen to.
///
/// Run on a QuadCast it prints, in order: 503 packets and none of them empty, then 503
/// with 496 empty, then 501 with one. Which looks like an answer and is not one, and the
/// difference cost an afternoon.
///
/// The stream emptying does not mean Windows zeroed it. On this device the flag reaches
/// the hardware — muting the endpoint puts the light on the microphone's body out — so
/// what the middle phase measured was the capsule going quiet, not the engine muting the
/// samples. The two are indistinguishable from here, and reading it the wrong way led to
/// "the application cannot see the capsule while it has muted the endpoint", which is
/// false: press the button on the device afterwards and the sound comes back with the
/// flag still set.
///
/// So what this probe actually establishes is narrower than its name, and worth keeping
/// for it: that the flag and the microphone can disagree, which is why the page reads the
/// stream first and why a write pulses the flag rather than repeating it.
///
/// The seven non-empty packets after the mute are the buffer draining: about seventy
/// milliseconds of audio arrives after the write, which is half the reason a click has to
/// outrank the stream for a moment.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BlindSpotProbe
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(25);

    public static async Task<int> RunAsync(int seconds, string? name)
    {
        var endpoints = new WindowsAudioEndpoints();

        AudioEndpoint? chosen = endpoints.Enumerate()
            .Where(e => e.Flow == AudioFlow.Capture && !e.IsContainerless)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .FirstOrDefault(e => name is not { Length: > 0 } || e.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (chosen is null)
        {
            Console.WriteLine($"No capture endpoint matches \"{name}\".");
            return 1;
        }

        Console.WriteLine(chosen.Name);

        using IAudioEndpointChannel channel = endpoints.Open(chosen.Id);
        using IAudioLevelMeter meter = endpoints.Listen(chosen.Id);

        bool? was = channel.Mute;
        int phase = Math.Max(2, seconds / 3);

        Console.WriteLine($"  {meter.Format}   muted in Windows: {(was is { } m ? m ? "yes" : "no" : "-")}");
        Console.WriteLine();
        Console.WriteLine("  Leave the microphone's own button ALONE for this one — the probe");
        Console.WriteLine("  drives Windows' mute and puts it back. Ordinary room noise is enough.");
        Console.WriteLine();

        try
        {
            _ = channel.SetMute(false);

            // Nothing to compare against until the capsule is passing sound. Waiting for
            // it rather than refusing, because the person who has to press the button is
            // reading about it in another window.
            if (!await WaitForSoundAsync(meter).ConfigureAwait(false))
            {
                Console.WriteLine("  Nothing ever arrived. The capsule stayed muted, so this measures nothing.");
                return 2;
            }

            Reading before = await CountAsync(meter, phase).ConfigureAwait(false);
            Say("Windows not muting", before);

            if (before.Empty == before.Packets)
            {
                Console.WriteLine();
                Console.WriteLine("  The stream went empty again before the test began. Retry without");
                Console.WriteLine("  touching the button.");
                return 2;
            }

            _ = channel.SetMute(true);
            Reading muted = await CountAsync(meter, phase).ConfigureAwait(false);
            Say("Windows muting", muted);

            _ = channel.SetMute(false);
            Reading after = await CountAsync(meter, phase).ConfigureAwait(false);
            Say("Windows not muting", after);

            Console.WriteLine();

            // A fraction, not an equality. The first run of this asked for every packet
            // to be empty, got 496 of 503, and printed the opposite of what it had just
            // measured: muting does not take effect between two packets, so the audio
            // already in the buffer arrives after it — about seventy milliseconds of it.
            bool blinded = muted.Packets > 0 && muted.Empty >= muted.Packets * 0.9;

            Console.WriteLine(blinded
                ? "  The stream went empty while the flag was set. On a device that answers the\n" +
                  "  flag in hardware that is the capsule going quiet, not the engine muting the\n" +
                  "  samples, and from here the two look the same. Do not read it as either."
                : "  The stream kept its sound while the flag was set, so on this device the flag\n" +
                  "  is a request the capsule did not follow. The two can disagree, which is why\n" +
                  "  the page believes the stream and a write pulses the flag."); 

            return 0;
        }
        finally
        {
            if (was is { } restore) _ = channel.SetMute(restore);
            Console.WriteLine($"  Windows' mute put back to {(was == true ? "muted" : "not muted")}.");
        }
    }

    /// <summary>
    /// Holds until the capsule is passing sound, or gives up after half a minute. The
    /// microphone's own button is the only thing that can start it, and nothing on this
    /// side can press it.
    /// </summary>
    private static async Task<bool> WaitForSoundAsync(IAudioLevelMeter meter)
    {
        Console.WriteLine("  Waiting for the capsule: take the microphone off its own mute (the light comes on).");

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            Reading heard = await CountAsync(meter, 1).ConfigureAwait(false);

            if (heard.Packets > heard.Empty)
            {
                Console.WriteLine($"  Sound at {clock.Elapsed.TotalSeconds:0.0}s. Measuring.");
                Console.WriteLine();
                return true;
            }
        }

        return false;
    }

    private static async Task<Reading> CountAsync(IAudioLevelMeter meter, int seconds)
    {
        (long packets, long empty) = meter.Tally;
        double loudest = 0;

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await Task.Delay(Tick).ConfigureAwait(false);

            if (meter.Read() is { } peak) loudest = Math.Max(loudest, peak);
        }

        (long now, long blank) = meter.Tally;

        return new Reading(now - packets, blank - empty, loudest);
    }

    private static void Say(string phase, Reading reading) =>
        Console.WriteLine(
            $"    {phase,-22} packets {reading.Packets,5}   empty {reading.Empty,5}   " +
            $"loudest {(reading.Loudest <= 0 ? "-inf dB" : $"{20 * Math.Log10(reading.Loudest):0.0} dB")}");

    private readonly record struct Reading(long Packets, long Empty, double Loudest);
}
