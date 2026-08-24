using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Whether a microphone's own mute button can be seen without a vendor protocol.
///
/// The QuadCast's touch button does not change the Windows mute state — measured, twice —
/// so the application shows a microphone as live while the capsule is off. It does say so
/// on a vendor HID page, but that means a vendor driver for one device.
///
/// This probe tests the cheaper claim: that the capture stream itself is the witness. A
/// muted capsule streams exact zeroes, and a live capsule in a quiet room streams its own
/// noise floor, which is small but never nothing. If those two are separable then the
/// level meter the page already opens is enough to follow the button, with no vendor byte
/// anywhere.
///
/// So it prints half a second at a time: how many packets arrived, how many of them were
/// digital silence, and the loudest sample in the window. Tap the button during the run
/// and the transition is on the screen — along with how long it takes to be sure of.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WitnessProbe
{
    /// <summary>How often the meter is drained. Packets arrive about every 10 ms.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(25);

    /// <summary>One printed line. Short enough to see a transition, long enough to read.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    public static async Task<int> RunAsync(int seconds, string? name)
    {
        var endpoints = new WindowsAudioEndpoints();

        List<AudioEndpoint> capture =
        [
            .. endpoints.Enumerate()
                .Where(e => e.Flow == AudioFlow.Capture && !e.IsContainerless)
                .OrderBy(e => e.Name, StringComparer.Ordinal),
        ];

        if (capture.Count == 0)
        {
            Console.WriteLine("No microphones found.");
            return 1;
        }

        AudioEndpoint? chosen = name is { Length: > 0 }
            ? capture.FirstOrDefault(e => e.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            : capture[0];

        if (chosen is null)
        {
            Console.WriteLine($"No capture endpoint matches \"{name}\". Attached:");
            foreach (AudioEndpoint e in capture) Console.WriteLine($"  {e.Name}");
            return 1;
        }

        Console.WriteLine(chosen.Name);

        using IAudioEndpointChannel channel = endpoints.Open(chosen.Id);
        IAudioLevelMeter meter;

        try
        {
            meter = endpoints.Listen(chosen.Id);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  the stream would not open: {ex.Message}");
            return 1;
        }

        bool? was = channel.Mute;

        using (meter)
        {
            Console.WriteLine($"  {meter.Format}   volume {channel.Volume?.ToString(CultureInfo.InvariantCulture) ?? "-"}%   " +
                              $"muted in Windows: {(was is { } m ? m ? "yes" : "no" : "-")}");

            // Windows' own mute zeroes the capture stream, so with it on there is nothing
            // to measure: every packet is silence and the capsule cannot be seen through
            // it. The first run of this probe spent 75 seconds proving exactly that.
            if (was == true)
            {
                Console.WriteLine("  Windows had it muted, which would hide the answer — lifting that for the run.");
                _ = channel.SetMute(false);
            }

            try
            {
                Console.WriteLine();
                Console.WriteLine($"  Watching for {seconds}s. Tap the mute button a few times, and speak in between.");
                Console.WriteLine();
                Console.WriteLine("     time   packets  silent   loudest      reading");
                Console.WriteLine("     -----  -------  ------  ---------  -------------");

                await WatchAsync(meter, seconds).ConfigureAwait(false);
            }
            finally
            {
                if (was == true)
                {
                    _ = channel.SetMute(true);
                    Console.WriteLine("  Windows' mute put back as it was.");
                }
            }
        }

        return 0;
    }

    private static async Task WatchAsync(IAudioLevelMeter meter, int seconds)
    {
        var clock = Stopwatch.StartNew();
        var run = TimeSpan.Zero;

        (long packets, long silent) = meter.Tally;
        double loudest = 0;

        int live = 0;
        int quiet = 0;
        int mixed = 0;
        double floor = double.MaxValue;

        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await Task.Delay(Tick).ConfigureAwait(false);

            if (meter.Read() is not { } peak)
            {
                Console.WriteLine("  the stream stopped answering.");
                break;
            }

            loudest = Math.Max(loudest, peak);

            if (clock.Elapsed - run < Window) continue;
            run = clock.Elapsed;

            (long nowPackets, long nowSilent) = meter.Tally;
            long arrived = nowPackets - packets;
            long empty = nowSilent - silent;
            (packets, silent) = (nowPackets, nowSilent);

            string reading = arrived == 0 ? "no packets"
                : empty == arrived ? "capsule silent"
                : empty == 0 ? "capsule live"
                : "changing";

            if (arrived > 0)
            {
                if (empty == arrived) quiet++;
                else if (empty == 0) { live++; floor = Math.Min(floor, loudest); }
                else mixed++;
            }

            Console.WriteLine(
                $"    {clock.Elapsed.TotalSeconds,5:0.0}s  {arrived,7}  {empty,6}  {Decibels(loudest),9}  {reading}");

            loudest = 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {live} live windows, {quiet} silent, {mixed} straddling a change.");

        if (live > 0)
        {
            Console.WriteLine(
                $"  quietest live window: {Decibels(floor)} — this is the distance between " +
                "\"a quiet room\" and \"a muted capsule\".");
        }
    }

    private static string Decibels(double fraction) =>
        fraction <= 0 ? "-inf dB" : $"{20 * Math.Log10(fraction),5:0.0} dB";
}
