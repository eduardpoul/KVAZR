using System.Diagnostics;
using System.Runtime.Versioning;

using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.HyperX;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// The polar selector, read through the contributor that the application uses.
///
/// A check of the new code on real hardware rather than of the hardware: everything below
/// goes through <see cref="HyperXContributor"/> and the connection it opens, so a pass
/// here means the page will show the same thing. The device inventory cannot do this job:
/// it prints one snapshot and then exits, which says nothing about whether the row keeps
/// up with a hand on the dial.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PolarProbe
{
    private static readonly string[] Named = ["Stereo", "Cardioid", "Omnidirectional", "Bidirectional"];

    public static async Task<int> RunAsync(int seconds)
    {
        var contributor = new HyperXContributor(new WindowsHidListenerFactory());

        var quadcast = new DeviceInfo
        {
            Key = "probe:quadcast",
            DriverId = "probe",
            Vendor = "HyperX",
            Model = "QuadCast S",
            Class = DeviceClass.Microphone,
            HardwareIds = new HashSet<string>(StringComparer.Ordinal) { "usb:0951:171d" },
        };

        IDeviceConnection? connection = await contributor.AttachAsync(quadcast).ConfigureAwait(false);

        if (connection is null)
        {
            Console.WriteLine("The contributor did not attach. Is the microphone plugged in?");
            return 1;
        }

        using (connection)
        {
            Console.WriteLine($"Watching the polar selector for {seconds}s, through the application's own code.");
            Console.WriteLine("It should be known at once. Turn the dial to watch it follow.");
            Console.WriteLine();

            var clock = Stopwatch.StartNew();
            string last = string.Empty;

            while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                CapabilityValue value = await connection
                    .ReadAsync(CapabilityIds.MicPolarPattern)
                    .ConfigureAwait(false);

                // A choice, matching what the page is handed. The harness printing a bare
                // number was how a type mismatch went unnoticed: it printed the reading
                // correctly while the page beside it showed nothing at all.
                string now = value is ChoiceValue choice && choice.Index >= 0 && choice.Index < Named.Length
                    ? $"{choice.Index} — {Named[choice.Index]}"
                    : "not known yet: the channel says nothing until the dial moves";

                if (now != last)
                {
                    Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0}s   {now}");
                    last = now;
                }

                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Done.");
        return 0;
    }
}
