using System.Diagnostics;

using Kvazr.Core.Abstractions;
using Kvazr.Core.Catalogue;
using Kvazr.Core.Devices;
using Kvazr.Core.Drivers.Audeze;
using Kvazr.Core.Drivers.System;
using Kvazr.Race.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kvazr.Cli;

/// <summary>
/// Times every capability read on real hardware, one at a time.
///
/// Written because "the panel takes twenty seconds to fill in" is a claim nobody can act
/// on: the answer is either a slow link, a slow command, or a command that never answers
/// at all, and those have three different fixes. A read that replies costs milliseconds;
/// one that does not costs four attempts across a four-hundred-millisecond window. This
/// prints which is which, so the next change is aimed rather than guessed.
/// </summary>
internal static class TimingProbe
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        List<IDeviceDriver> drivers =
        [
            new AudezeRaceDriver(
                new WindowsHidEnumerator(),
                new WindowsHidTransportFactory(),
                new WindowsBluetoothMonitor()),
            new SystemDeviceDriver(new WindowsSystemDeviceEnumerator(), new DeviceCatalogue()),
        ];

        using var registry = new DeviceRegistry(
            drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        var discovery = Stopwatch.StartNew();
        await registry.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        discovery.Stop();

        Console.WriteLine($"discovery: {discovery.ElapsedMilliseconds} ms, {registry.Devices.Count} device(s)");
        Console.WriteLine();

        if (registry.Devices.Count == 0)
        {
            Console.WriteLine("No devices found.");
            return 1;
        }

        foreach (DeviceSnapshot device in registry.Devices)
        {
            if (registry.Session(device.Info.Key) is not { } session) continue;
            if (device.Capabilities.Count == 0) continue;

            Console.WriteLine($"=== {device.Info.DisplayName} ({device.Info.DriverId}) ===");
            Console.WriteLine($"    present={device.IsPresent} control={device.CanControl} caps={device.Capabilities.Count}");

            var whole = Stopwatch.StartNew();
            long slow = 0;

            foreach (CapabilityState state in device.Capabilities.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                if (!state.Descriptor.IsReadable) continue;

                var one = Stopwatch.StartNew();
                await session.RefreshAsync([state.Id], cancellationToken).ConfigureAwait(false);
                one.Stop();

                CapabilityValue after = session.Snapshot.Get(state.Id)?.Value ?? CapabilityValue.Absent;
                string answered = after.IsAbsent ? "SILENT" : "ok";

                if (one.ElapsedMilliseconds >= 200) slow += one.ElapsedMilliseconds;

                Console.WriteLine($"    {one.ElapsedMilliseconds,6} ms  {answered,-6}  {state.Id}");
            }

            whole.Stop();

            Console.WriteLine($"    ----- total {whole.ElapsedMilliseconds} ms, of which {slow} ms in reads over 200 ms");
            Console.WriteLine();
        }

        return 0;
    }
}
