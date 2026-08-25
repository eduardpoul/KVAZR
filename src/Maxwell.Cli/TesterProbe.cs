using System.Diagnostics;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Diagnostics;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Walks one mouse through its controls, one at a time, and says what answered.
///
/// It used to watch everything at once for half a minute and print which flags had been
/// seen. That counts, and counting is the weaker half of the question: five flags out of
/// five tells nobody whether their side button works, only that something did. Asked one
/// control at a time, the same traffic answers what people actually open a mouse tester
/// for — press this; did it arrive?
///
/// The ceiling is unchanged and no procedure raises it. Raw Input's mouse record carries
/// five buttons and a wheel, full stop; a sixth button is not under-reported here, it is
/// absent from the vocabulary and arrives on a different collection entirely. What the
/// walk changes is that the five are confirmed one by one instead of summed.
/// </summary>
internal static class TesterProbe
{
    private const ushort LeftDown = 0x0001;
    private const ushort RightDown = 0x0004;
    private const ushort MiddleDown = 0x0010;
    private const ushort FourthDown = 0x0040;
    private const ushort FifthDown = 0x0100;
    private const ushort Wheel = 0x0400;

    /// <summary>How long one step waits before giving up on it and moving on.</summary>
    private static readonly TimeSpan StepPatience = TimeSpan.FromSeconds(20);

    private sealed record Step(string Ask, ushort Flag, int Direction = 0);

    private static readonly Step[] Walk =
    [
        new("Left click", LeftDown),
        new("Right click", RightDown),
        new("Wheel click", MiddleDown),
        new("Side button — back", FourthDown),
        new("Side button — forward", FifthDown),
        new("Wheel up", Wheel, 1),
        new("Wheel down", Wheel, -1),
    ];

    public static int Run(int seconds)
    {
        using var watcher = new WindowsRawInputWatcher();

        if (!watcher.IsWatching)
        {
            Console.WriteLine("Raw Input refused the registration — nothing will arrive.");
            return 1;
        }

        var state = new Progress();
        watcher.Reported += state.Saw;

        Console.WriteLine("[tester] One control at a time. Nothing is opened and nothing is written.");
        Console.WriteLine();
        Console.WriteLine("  Use one mouse throughout — the first thing that answers is the one being");
        Console.WriteLine("  tested, and everything else is ignored from then on.");
        Console.WriteLine("  Press Enter to skip a control you have not got.");
        Console.WriteLine();

        var answered = new List<Step>();
        var silent = new List<Step>();

        for (int i = 0; i < Walk.Length; i++)
        {
            Step step = Walk[i];
            Console.Write($"  {i + 1}/{Walk.Length + 1}  {step.Ask,-24} ");

            if (Await(state, step))
            {
                answered.Add(step);
                Console.WriteLine("ok");
            }
            else
            {
                silent.Add(step);
                Console.WriteLine("— skipped");
            }
        }

        Console.Write($"  {Walk.Length + 1}/{Walk.Length + 1}  {"Move it, fast",-24} ");
        Sweep(state, seconds);

        Report(state, answered, silent);

        return 0;
    }

    /// <summary>
    /// Waits for one control, or for the person to say they have not got it.
    ///
    /// Polling rather than a wait handle, because there are two things to wait for and
    /// one of them is a keypress. Twenty milliseconds is well below noticing and well
    /// above spinning.
    /// </summary>
    private static bool Await(Progress state, Step step)
    {
        state.Expect(step);

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < StepPatience)
        {
            if (state.Satisfied) return true;

            // Guarded, because asking a redirected stdin whether a key is waiting throws
            // rather than answering no — and a harness that pipes into this would take
            // the whole walk down on its first step.
            if (!Console.IsInputRedirected
                && Console.KeyAvailable
                && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
            {
                return false;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    /// <summary>
    /// The one step that measures rather than asks: how fast reports come when somebody
    /// is really moving it.
    /// </summary>
    private static void Sweep(Progress state, int seconds)
    {
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"        Whip it about for {seconds}s — as fast as you can, and keep going.");
        Console.WriteLine("        A mouse reports when it has movement to report, so a gentle hand");
        Console.WriteLine("        measures the hand.");
        Console.WriteLine();

        state.Measure();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
    }

    private static void Report(Progress state, List<Step> answered, List<Step> silent)
    {
        Console.WriteLine();
        Console.WriteLine($"  Mouse      {state.Name ?? "nothing answered"}");
        Console.WriteLine(
            $"  Answered   {(answered.Count > 0 ? string.Join(", ", answered.Select(s => s.Ask)) : "nothing")}");

        if (silent.Count > 0)
        {
            Console.WriteLine($"  Silent     {string.Join(", ", silent.Select(s => s.Ask))}");
        }

        if (state.Steps.Count > 0)
        {
            Console.WriteLine(
                $"  Wheel      steps of {string.Join(", ", state.Steps.Order())}"
                + (state.Steps.All(s => Math.Abs(s) % 120 == 0)
                    ? " — stepped, one detent is 120"
                    : " — finer than a detent, so high resolution"));
        }

        Console.WriteLine($"  Rate       at least {state.Peak} per second");
        Console.WriteLine();
        Console.WriteLine("  \"At least\" twice over. A control nobody pressed is silent here rather than");
        Console.WriteLine("  missing from the mouse; and the rate is what the hand achieved, which is a");
        Console.WriteLine("  floor under what the mouse can do and never a ceiling.");
    }

    /// <summary>
    /// What has been asked for and what has arrived. Shared with the watcher's thread,
    /// which is why everything it touches sits behind one lock.
    /// </summary>
    private sealed class Progress
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<long, int> _tenths = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private Step? _wanted;
        private bool _measuring;
        private IntPtr _device;

        public bool Satisfied { get; private set; }

        public string? Name { get; private set; }

        public HashSet<int> Steps { get; } = [];

        /// <summary>Reports in the busiest tenth of a second, scaled to the second.</summary>
        public int Peak { get; private set; }

        public void Expect(Step step)
        {
            lock (_sync)
            {
                _wanted = step;
                Satisfied = false;
            }
        }

        public void Measure()
        {
            lock (_sync)
            {
                _wanted = null;
                _measuring = true;
            }
        }

        public void Saw(RawMouseReport report)
        {
            lock (_sync)
            {
                // The first mouse to answer claims the walk. Two mice on one desk both
                // report, and a walk that accepted either would pass a step with the
                // wrong one and never say which.
                if (_device != IntPtr.Zero && report.Device != _device) return;

                if (_measuring) Count();

                if (_wanted is not { } step) return;
                if ((report.Buttons & step.Flag) == 0) return;

                if (step.Direction != 0)
                {
                    if (report.Wheel == 0 || Math.Sign(report.Wheel) != step.Direction) return;

                    Steps.Add(report.Wheel);
                }

                if (_device == IntPtr.Zero)
                {
                    _device = report.Device;
                    Name = RawInputPointers.NameOf(report.Device);
                }

                Satisfied = true;
            }
        }

        private void Count()
        {
            long tenth = (long)(_clock.Elapsed.TotalSeconds * 10);
            int inThisTenth = _tenths[tenth] = _tenths.GetValueOrDefault(tenth) + 1;

            if (inThisTenth * 10 > Peak) Peak = inThisTenth * 10;
        }
    }
}
