using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Diagnostics;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Everything the QuadCast says, while somebody works through its controls one at a time.
///
/// The passive listener already existed and it found the mute button. What it could not do
/// is attribute: an unguided window of traffic tells you that something spoke, not which
/// hand made it speak, and the first run of it produced seventy-five seconds of silence
/// because nobody was turning anything. So this one runs a script. Each step is printed,
/// then held open, and whatever arrives lands underneath the step that caused it.
///
/// Four channels are listened to at once because it is not known which one answers for
/// what: the lighting pipe on 0xFF90, the mute pipe on 0xFFFF, and — the interesting one —
/// the sixteen-byte bidirectional channel on the audio device's Consumer collection, which
/// carries vendor usages 0x07FF and is where a gain knob or a pattern selector would most
/// naturally report. The feature report on 0xFF90 is polled alongside them, because a
/// value that only appears when read would never show up on a listener at all.
///
/// Passive. Nothing is written to the device at any point.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class QuadCastProbe
{
    private const ushort HyperX = 0x0951;

    /// <summary>
    /// The script. Long enough per step to do the thing unhurriedly and to see it land,
    /// short enough that the whole run is a couple of minutes.
    /// </summary>
    private static readonly (int Seconds, string Do)[] Steps =
    [
        (8, "Nothing at all — this is the baseline. Keep your hands off the microphone."),
        (14, "Tap the mute panel on top: off, on, off, on. Leave it ON at the end."),
        (18, "Turn the GAIN dial slowly all the way down, then all the way back up."),
        (20, "Turn the POLAR PATTERN selector on the back through all four positions,\n"
             + "     pausing about three seconds on each one."),
        (14, "Plug headphones into the microphone's jack, then unplug them.\n"
             + "     Skip this step and just wait if you have none to hand."),
        (8, "Nothing again — hands off. This is the settle."),
    ];

    /// <summary>
    /// The second script: which icon on the selector is which value.
    ///
    /// By icon rather than by position on purpose. The dial's order is a fact about one
    /// model's silkscreen and it is not the order the marketing pictures show; naming the
    /// drawing and asking for it back removes the dial's layout from the question
    /// entirely. Turning to a position passes through the ones between it, so the value
    /// that counts in each step is the last one printed.
    /// </summary>
    private static readonly (int Seconds, string Do)[] PatternSteps =
    [
        (6, "Hands off — baseline."),
        (8, "Turn the selector to the icon that is TWO FULL SEPARATE RINGS."),
        (8, "Now to the icon that is A RING WITH A NOTCH TAKEN OUT — the Pac-Man one."),
        (8, "Now to the icon that is ONE FULL RING."),
        (8, "Now to the icon that is TWO INTERSECTING RINGS."),
        (5, "Hands off. Leave it wherever you like."),
    ];

    public static int Run(string? logPath, bool patterns = false)
    {
        List<HidInterface> ears =
        [
            .. new WindowsHidEnumerator().EnumerateAll()
                .Where(i => i.VendorId == HyperX && i.InputReportLength > 1)
                .OrderBy(i => i.ProductId)
                .ThenBy(i => i.UsagePage),
        ];

        if (ears.Count == 0)
        {
            Console.WriteLine("No HyperX HID interface found. Is the microphone plugged in?");
            return 1;
        }

        string path = logPath is { Length: > 0 }
            ? logPath
            : Path.Combine(Path.GetTempPath(), "quadcast-dig.txt");

        using var log = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };

        (int Seconds, string Do)[] script = patterns ? PatternSteps : Steps;

        Write(log, $"QuadCast dig — {script.Sum(s => s.Seconds)} seconds, {ears.Count} channels, nothing written.");
        Write(log, string.Empty);

        foreach (HidInterface ear in ears)
        {
            Write(log, $"  listening: {ear.VendorId:X4}:{ear.ProductId:X4} page 0x{ear.UsagePage:X4} " +
                       $"usage 0x{ear.Usage:X2}  in {ear.InputReportLength}");
        }

        Write(log, string.Empty);
        Write(log, $"  the log is being written to {path}");
        Write(log, string.Empty);

        var stop = new CancellationTokenSource();
        var gate = new object();
        var seen = new ConcurrentDictionary<string, string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        List<Task> listeners = [.. ears.Select(ear => Task.Run(() => Listen(ear, log, gate, seen, clock, stop.Token)))];
        Task poller = Task.Run(() => Poll(log, gate, clock, stop.Token));

        try
        {
            foreach ((int seconds, string what) in script)
            {
                lock (gate)
                {
                    Write(log, string.Empty);
                    Write(log, $"===== {clock.Elapsed.TotalSeconds,5:0.0}s  ({seconds}s)  {what}");
                }

                Thread.Sleep(TimeSpan.FromSeconds(seconds));
            }
        }
        finally
        {
            stop.Cancel();
            Task.WaitAll([.. listeners, poller], TimeSpan.FromSeconds(3));
        }

        lock (gate)
        {
            Write(log, string.Empty);
            Write(log, "===== done.");
            Write(log, string.Empty);
            Write(log, $"The log is at {path} — send that file, or its contents.");
        }

        return 0;
    }

    private static void Listen(
        HidInterface ear,
        TextWriter log,
        object gate,
        ConcurrentDictionary<string, string> seen,
        System.Diagnostics.Stopwatch clock,
        CancellationToken stop)
    {
        string tag = $"{ear.ProductId:X4} 0x{ear.UsagePage:X4}";

        try
        {
            using var channel = new WindowsHidChannel(ear.Path, ear.InputReportLength, ear.OutputReportLength);

            while (!stop.IsCancellationRequested)
            {
                if (channel.Listen(TimeSpan.FromMilliseconds(400)) is not { } report) continue;

                string line = string.Join(' ', report.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

                // A channel that repeats one frame is one line of news rather than
                // hundreds. Changes only, after the first of each.
                if (seen.TryGetValue(tag, out string? previous) && previous == line) continue;
                seen[tag] = line;

                lock (gate) Write(log, $"  {clock.Elapsed.TotalSeconds,5:0.0}s  {tag}  {line}");
            }
        }
        catch (IOException ex)
        {
            lock (gate) Write(log, $"  {tag} could not be opened: {ex.Message}");
        }
    }

    /// <summary>
    /// The lighting pipe's feature report, four times a second.
    ///
    /// Separate from the listeners because it is a different kind of question: a listener
    /// hears what a device volunteers, and a device that keeps its state in a register
    /// volunteers nothing. This is the only way that state would ever be seen.
    /// </summary>
    private static void Poll(TextWriter log, object gate, System.Diagnostics.Stopwatch clock, CancellationToken stop)
    {
        string last = string.Empty;

        while (!stop.IsCancellationRequested)
        {
            if (VendorSurvey.ReadFeature(HyperX, 0xFF90) is { } now && now != last)
            {
                lock (gate) Write(log, $"  {clock.Elapsed.TotalSeconds,5:0.0}s  FF90 feature  {now}");
                last = now;
            }

            Thread.Sleep(250);
        }
    }

    private static void Write(TextWriter log, string line)
    {
        Console.WriteLine(line);
        log.WriteLine(line);
    }
}
