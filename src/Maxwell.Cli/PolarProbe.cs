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
/// here means the page will show the same thing. The device inventory cannot do this job —
/// it prints one snapshot at start-up, and this row is empty until somebody turns the
/// dial, so a snapshot will always show it empty however many times it is run.
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
            Console.WriteLine("Turn the selector on the back. Nothing is written to the device.");
            Console.WriteLine();

            var clock = Stopwatch.StartNew();
            string last = string.Empty;

            while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                CapabilityValue value = await connection
                    .ReadAsync(CapabilityIds.MicPolarPattern)
                    .ConfigureAwait(false);

                string now = value is NumberValue number && number.Value >= 0 && number.Value < Named.Length
                    ? $"{number.Value} — {Named[number.Value]}"
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
