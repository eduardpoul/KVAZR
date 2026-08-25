using Maxwell.Race;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Whether switching a headset on announces itself, on any of the three channels the
/// machine could possibly hear it on.
///
/// This decides an architecture, not a detail. Everything the panel knows about a device
/// that has gone quiet currently comes from asking it — and asking has a cadence, so
/// switching a headset on is followed by a wait somewhere between nothing and the length
/// of that cadence. If any of these three fires, the wait goes away entirely and the
/// polling becomes a safety net instead of the mechanism.
///
/// The three, and why all of them at once: the dongle stays plugged in when the headset
/// sleeps, so Windows sees no arrival or removal of the HID device — but the audio
/// endpoints it presents may well come and go with the link, and that is an event Core
/// Audio raises. Separately the headset itself might volunteer a report: it does for the
/// volume wheel, which is why the panel follows the wheel without polling for it. One
/// timeline, so whichever of them fires can be read against the moment the switch moved.
/// </summary>
internal static class WakeProbe
{
    public static async Task<int> RunAsync(int seconds, CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var endpoints = new WindowsAudioEndpoints();

        List<MaxwellInterface> faces = [.. enumerator.Enumerate()];

        Console.WriteLine($"[wake] Watching three sources for {seconds}s.");
        Console.WriteLine();
        Console.WriteLine("  hid    — unprompted reports on the headset's own interfaces");
        Console.WriteLine("  system — Windows announcing a device arriving or leaving");
        Console.WriteLine("  audio  — an audio endpoint appearing or disappearing");
        Console.WriteLine();

        if (faces.Count == 0)
        {
            Console.WriteLine("  No Audeze interface found — plug the dongle in first.");
            return 1;
        }

        Console.WriteLine("  Switch the headset OFF, wait ten seconds, switch it ON, wait ten seconds.");
        Console.WriteLine("  Do it twice if there is time. Every line below is stamped.");
        Console.WriteLine();

        var hits = new Dictionary<string, int>(StringComparer.Ordinal);

        void Note(string source, string what)
        {
            lock (hits)
            {
                hits[source] = hits.GetValueOrDefault(source) + 1;
                Console.WriteLine($"  {DateTime.Now:HH:mm:ss.fff}  {source,-7} {what}");
            }
        }

        // ---------- Windows device arrivals ----------

        using var system = new WindowsDeviceChangeWatcher();
        system.Changed += (_, _) => Note("system", "a device arrived or left");
        system.Start();

        if (!system.IsWatching)
        {
            Console.WriteLine($"  (the system watcher was refused, CM result {system.Result} — its line will stay empty)");
            Console.WriteLine();
        }

        // ---------- the headset's own interfaces ----------

        var channels = new List<(MaxwellInterface Face, WindowsHidChannel Channel)>();

        foreach (MaxwellInterface face in faces)
        {
            try
            {
                channels.Add((face, new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength)));
            }
            catch (IOException ex)
            {
                Console.WriteLine($"  (could not open usage {face.UsagePage:X4}: {ex.Message})");
            }
        }

        long deadline = Environment.TickCount64 + (seconds * 1000);

        var listening = channels.Select(pair => Task.Run(
            () =>
            {
                while (Environment.TickCount64 < deadline)
                {
                    byte[]? report = pair.Channel.Listen(TimeSpan.FromMilliseconds(300));
                    if (report is null) continue;

                    Note(
                        "hid",
                        $"usage {pair.Face.UsagePage:X4}:{pair.Face.Usage:X2}  "
                        + Convert.ToHexString(report.AsSpan(0, Math.Min(12, report.Length))));
                }
            },
            cancellationToken)).ToList();

        // ---------- the audio endpoints ----------
        //
        // Sampled rather than subscribed, because the question here is only whether the
        // set changes with the switch. If it does, subscribing is the next piece of work
        // and Core Audio has a notification for exactly this.
        listening.Add(Task.Run(
            () =>
            {
                Dictionary<string, string> before = Endpoints(endpoints);

                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(250);

                    Dictionary<string, string> now = Endpoints(endpoints);

                    foreach ((string id, string name) in now)
                    {
                        if (!before.ContainsKey(id)) Note("audio", $"+ {name}");
                    }

                    foreach ((string id, string name) in before)
                    {
                        if (!now.ContainsKey(id)) Note("audio", $"- {name}");
                    }

                    before = now;
                }
            },
            cancellationToken));

        await Task.WhenAll(listening).ConfigureAwait(false);

        foreach ((_, WindowsHidChannel channel) in channels) channel.Dispose();

        Console.WriteLine();

        if (hits.Count == 0)
        {
            Console.WriteLine("RESULT: nothing announced anything. Asking on a timer is the only way to know,");
            Console.WriteLine("        and the only question left is how often it is worth asking.");
            return 0;
        }

        Console.WriteLine("RESULT: something did announce itself —");

        foreach ((string source, int count) in hits.OrderByDescending(p => p.Value))
        {
            Console.WriteLine($"   {source,-7} x{count}");
        }

        Console.WriteLine();
        Console.WriteLine("        Read the stamps against when the switch actually moved: an event that");
        Console.WriteLine("        fires for both directions, promptly, is one the panel can subscribe to.");

        return 0;
    }

    private static Dictionary<string, string> Endpoints(WindowsAudioEndpoints source)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (AudioEndpoint endpoint in source.Enumerate())
        {
            found[endpoint.Id] = $"{endpoint.Name}  [{endpoint.Flow}]";
        }

        return found;
    }
}
