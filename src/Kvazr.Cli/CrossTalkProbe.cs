using System.Runtime.Versioning;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Two readers asking one device where two different features live, at the same time.
///
/// The page named a mouse "Logitech b" followed by a control character, and the two bytes
/// were the battery percentage and the byte after it — the mouse was on 98%, and 98 is the
/// letter b. So a question about the name was answered with a reading about the charge.
///
/// The suspected reason is that a feature lookup cannot be told apart from another feature
/// lookup. Every one of them is sent to the same index and the same function, and the
/// answer carries no echo of what was asked — so with two in flight at once, either
/// reader can take the other's reply and go on to read a completely different feature
/// under the name of the one it wanted.
///
/// This is the experiment for that: one thread asking where the name is, another asking
/// where the battery is, both told what the right answers are from a quiet run. Anything
/// else is a swapped reply.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CrossTalkProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort LongUsage = 0x02;
    private const ushort DeviceName = 0x0005;
    private const ushort UnifiedBattery = 0x1004;

    public static int Run(int rounds)
    {
        HidInterface? face = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == Logitech
                && i.UsagePage == HidPlusPlusPage
                && i.Usage == LongUsage
                && i.OutputReportLength >= 20);

        if (face is null)
        {
            Console.WriteLine("No HID++ long-report interface found.");
            return 1;
        }

        const byte slot = 1;

        // The truth, established with nothing else talking.
        using (var quiet = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength))
        {
            HidPlusPlusReader.Interrogate(quiet, slot);
            byte? name = HidPlusPlusReader.FeatureIndex(quiet, slot, DeviceName);
            byte? battery = HidPlusPlusReader.FeatureIndex(quiet, slot, UnifiedBattery);

            Console.WriteLine($"[crosstalk] alone: name is at {name}, battery is at {battery}");
            Console.WriteLine();

            if (name is not { } expectedName || battery is not { } expectedBattery)
            {
                Console.WriteLine("  The device would not place both features. Nothing to race.");
                return 1;
            }

            int swapped = 0;
            int missed = 0;
            var wrong = new List<string>();

            // Two channels on one device, which is what the application has: the driver
            // opens one to look the device over and the poller opens another to read it.
            // Every report goes to both handles.
            var barrier = new Barrier(2);

            void Race(ushort feature, byte right, string label)
            {
                using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

                for (int i = 0; i < rounds; i++)
                {
                    barrier.SignalAndWait();

                    byte? found = HidPlusPlusReader.FeatureIndex(channel, slot, feature);

                    if (found is null) Interlocked.Increment(ref missed);
                    else if (found != right)
                    {
                        Interlocked.Increment(ref swapped);
                        lock (wrong) wrong.Add($"    round {i}: {label} came back at {found}, not {right}");
                    }
                }
            }

            var one = new Thread(() => Race(DeviceName, expectedName, "the name"));
            var two = new Thread(() => Race(UnifiedBattery, expectedBattery, "the battery"));

            one.Start();
            two.Start();
            one.Join();
            two.Join();

            Console.WriteLine($"  {rounds} rounds, two readers at once:");
            Console.WriteLine($"    swapped  {swapped}");
            Console.WriteLine($"    silent   {missed}");

            foreach (string line in wrong.Take(10)) Console.WriteLine(line);

            Console.WriteLine();
            Console.WriteLine(swapped > 0
                ? "  A lookup can be answered with another lookup's answer."
                : "  Nothing swapped in this run.");

            return 0;
        }
    }
}
