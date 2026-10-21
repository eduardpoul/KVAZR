using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Proves the device-arrival notification actually fires.
///
/// Native registration is the kind of thing that fails quietly: a wrong structure size
/// is refused, nothing throws, and the application simply never hears about a device
/// again — indistinguishable from a machine where nothing was plugged in. So it gets
/// exercised by hand once, with something to unplug.
/// </summary>
internal static class DeviceChangeProbe
{
    public static int Run(TimeSpan window)
    {
        using var watcher = new WindowsDeviceChangeWatcher();

        int seen = 0;
        watcher.Changed += (_, _) =>
        {
            int n = Interlocked.Increment(ref seen);
            Console.WriteLine($"    [{DateTime.Now:HH:mm:ss.fff}] device change #{n}");
        };

        watcher.Start();

        Console.WriteLine($"registration: {(watcher.IsWatching ? "accepted" : "REFUSED")} (CM result {watcher.Result})");

        if (!watcher.IsWatching)
        {
            Console.WriteLine("RESULT: the registration was refused — no notifications will ever arrive.");
            return 1;
        }

        Console.WriteLine($"Listening for {window.TotalSeconds:0} s — plug or unplug something.");
        Console.WriteLine();

        Thread.Sleep(window);

        Console.WriteLine();
        Console.WriteLine(seen > 0
            ? $"RESULT: {seen} notification(s). The watcher works."
            : "RESULT: nothing heard. Either nothing changed, or the registration was refused.");

        return seen > 0 ? 0 : 1;
    }
}
