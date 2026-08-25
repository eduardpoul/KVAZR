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
        Console.WriteLine($"      rate      {tally.Rate():0} per second while moving");

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
        private TimeSpan _first = TimeSpan.MinValue;
        private TimeSpan _last;

        public int Reports { get; private set; }

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

            if (_first == TimeSpan.MinValue) _first = at;
            _last = at;

            if ((report.Buttons & (Wheel | HorizontalWheel)) != 0 && report.Wheel != 0)
            {
                Steps.Add(report.Wheel);
            }
        }

        /// <summary>
        /// Reports per second across the span they arrived in, which is the rate while the
        /// mouse was being used — the only rate worth having. Counting against the whole
        /// window would divide by the time it spent sitting still.
        /// </summary>
        public double Rate()
        {
            double span = (_last - _first).TotalSeconds;

            return span > 0.2 ? Reports / span : 0;
        }
    }
}
