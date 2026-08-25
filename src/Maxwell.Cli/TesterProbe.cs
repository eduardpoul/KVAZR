using System.Diagnostics;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Diagnostics;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// What a mouse actually does, counted rather than read off its declaration.
///
/// The last universal source at the baseline. Everything else about a mouse is something
/// it says about itself, and the descriptor has been caught saying sixteen buttons for a
/// five-button one; Windows repeats the same sixteen. Only the traffic settles it, and
/// the traffic needs somebody to move the mouse — which is why this is a tester and not
/// another line in an inventory.
/// </summary>
internal static class TesterProbe
{
    private const ushort LeftDown = 0x0001;
    private const ushort RightDown = 0x0004;
    private const ushort MiddleDown = 0x0010;
    private const ushort FourthDown = 0x0040;
    private const ushort FifthDown = 0x0100;
    private const ushort Wheel = 0x0400;
    private const ushort HorizontalWheel = 0x0800;

    private static readonly (ushort Flag, string Name)[] Buttons =
    [
        (LeftDown, "left"),
        (RightDown, "right"),
        (MiddleDown, "middle"),
        (FourthDown, "fourth (back)"),
        (FifthDown, "fifth (forward)"),
    ];

    public static int Run(int seconds)
    {
        var seen = new Dictionary<IntPtr, Tally>();
        var clock = Stopwatch.StartNew();

        using var watcher = new WindowsRawInputWatcher();

        if (!watcher.IsWatching)
        {
            Console.WriteLine("Raw Input refused the registration — nothing will arrive.");
            return 1;
        }

        watcher.Reported += report =>
        {
            lock (seen)
            {
                if (!seen.TryGetValue(report.Device, out Tally? tally))
                {
                    seen[report.Device] = tally = new Tally();
                }

                tally.Count(report, clock.Elapsed);
            }
        };

        Console.WriteLine($"[tester] Counting for {seconds}s. Nothing is opened and nothing is written.");
        Console.WriteLine();
        Console.WriteLine("  Move each mouse, press every button including the side ones,");
        Console.WriteLine("  turn the wheel, and tilt it if it tilts.");
        Console.WriteLine();
        Console.WriteLine("  For the rate: whip it about as fast as you can for a second or two.");
        Console.WriteLine("  A mouse reports when it has movement to report, so a slow hand");
        Console.WriteLine("  measures the hand rather than the mouse.");
        Console.WriteLine();

        Thread.Sleep(TimeSpan.FromSeconds(seconds));

        lock (seen)
        {
            if (seen.Count == 0)
            {
                Console.WriteLine("  Nothing moved.");
                return 1;
            }

            foreach ((IntPtr device, Tally tally) in seen)
            {
                Report(device, tally);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Counted, not declared. A button that was never pressed is missing from this");
        Console.WriteLine("list rather than absent from the mouse, and Raw Input carries five buttons and");
        Console.WriteLine("a wheel — a sixth arrives elsewhere. So the count is a floor, and a floor that");
        Console.WriteLine("was observed beats a ceiling that was claimed.");

        return 0;
    }

    private static void Report(IntPtr device, Tally tally)
    {
        string name = RawInputPointers.NameOf(device) ?? $"device {device.ToInt64():X}";

        Console.WriteLine($"  {name}");
        Console.WriteLine($"      reports   {tally.Reports}");
        Console.WriteLine($"      rate      {tally.Peak} per second at its busiest");

        string pressed = string.Join(
            ", ",
            Buttons.Where(b => (tally.Flags & b.Flag) != 0).Select(b => b.Name));

        Console.WriteLine($"      buttons   {(pressed.Length > 0 ? pressed : "none pressed")}");
        Console.WriteLine($"      wheel     {(tally.Flags & Wheel) != 0} · "
                          + $"horizontal {((tally.Flags & HorizontalWheel) != 0 ? "yes" : "not seen")}");

        if (tally.Steps.Count > 0)
        {
            Console.WriteLine($"      steps     {string.Join(", ", tally.Steps.Order())}");
        }

        Console.WriteLine();
    }

    private sealed class Tally
    {
        /// <summary>
        /// Counted in tenths of a second rather than whole ones.
        ///
        /// Because of what is being asked of the person holding the mouse: nobody moves
        /// one flat out for a whole second, so a whole-second bucket averages the fast
        /// part of a flick together with the slowing down at the end of it. A tenth is
        /// short enough to sit inside one swipe.
        /// </summary>
        private readonly Dictionary<long, int> _tenths = [];

        public int Reports { get; private set; }

        /// <summary>
        /// The rate at its busiest, which is a floor on what the mouse can do and never a
        /// ceiling.
        ///
        /// It replaced an average across the whole span, and that average was wrong in a
        /// way that read as a plausible number: somebody moves a mouse in bursts, the span
        /// counts the pauses, and any mouse comes out at forty-something.
        ///
        /// Even this is a floor, and for a reason no arithmetic fixes: a mouse sends a
        /// report when it has movement to report. Polled a thousand times a second and
        /// pushed gently, it answers a fraction of those polls — so what is counted here
        /// is how fast the hand moved, until the hand moves fast enough to stop being the
        /// limit.
        /// </summary>
        public int Peak { get; private set; }

        public ushort Flags { get; private set; }

        /// <summary>
        /// The distinct wheel deltas seen. A stepped wheel reports 120 every time; a
        /// high-resolution one reports fractions of it, and that difference cannot be
        /// found in any declaration.
        /// </summary>
        public HashSet<int> Steps { get; } = [];

        public void Count(RawMouseReport report, TimeSpan at)
        {
            Reports++;
            Flags |= report.Buttons;

            long tenth = (long)(at.TotalSeconds * 10);
            int inThisTenth = _tenths[tenth] = _tenths.GetValueOrDefault(tenth) + 1;

            if (inThisTenth * 10 > Peak) Peak = inThisTenth * 10;

            if ((report.Buttons & (Wheel | HorizontalWheel)) != 0 && report.Wheel != 0)
            {
                Steps.Add(report.Wheel);
            }
        }

        // No "shortest gap between two reports" any more. It read 0.0 ms, and it was
        // reading the wrong clock: the timestamp is taken when this thread pulls the
        // message off its queue, not when the report arrived, so several delivered
        // together are stamped at the same instant. It described the message pump and
        // was offered as a fact about the mouse.
    }
}
