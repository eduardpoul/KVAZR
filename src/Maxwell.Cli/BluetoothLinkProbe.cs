using System.Diagnostics;
using System.Runtime.Versioning;

using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audio;
using Maxwell.Core.Drivers.System;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

using Microsoft.Extensions.Logging.Abstractions;

namespace Maxwell.Cli;

/// <summary>
/// How long it takes for a Bluetooth device coming or going to reach the page.
///
/// The complaint is that closing a case takes an age to show, and there are three quite
/// different places that time could be spent and no way to tell them apart from the
/// interface: Windows itself deciding the device has gone, our discovery sweep coming
/// round to ask, or the poll after it. Blaming the wrong one costs a rewrite.
///
/// So this prints all three on one clock, a second apart: the flag Windows keeps, the
/// audio endpoints belonging to the device, and what a real registry — the same drivers
/// and contributors the application composes — makes of it.
///
/// Read only. Nothing here writes, pairs or unpairs.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class BluetoothLinkProbe
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    public static async Task<int> RunAsync(int seconds, string? match, CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsSystemDeviceEnumerator();

        using var listening = new WindowsProximityListener();

        // The operating system's own announcement, which is what the application listens
        // to and what makes a sweep happen within a second instead of at the next timer.
        // Counted rather than described: the point is when one arrives, not what it said.
        int announcements = 0;
        using var watcher = new WindowsDeviceChangeWatcher();
        watcher.Changed += (_, _) => Interlocked.Increment(ref announcements);
        watcher.Start();

        ICapabilityContributor[] contributors =
        [
            new AudioContributor(new WindowsAudioEndpoints(), new WindowsSystemDeviceEnumerator()),
            new Maxwell.Core.Drivers.Apple.AppleProximityContributor(listening),
        ];

        using var registry = new DeviceRegistry(
            [new SystemDeviceDriver(enumerator, new DeviceCatalogue())],
            new InMemoryCapabilityLedger(),
            NullLoggerFactory.Instance,
            contributors);

        Console.WriteLine($"Слежу {seconds} с. Закрывайте и открывайте кейс — печатаю только изменения.");
        Console.WriteLine();
        Console.WriteLine("   время    Windows      эндпоинты  событий  наше состояние   заряд");
        Console.WriteLine("   -------  -----------  ---------  -------  ---------------  -----");

        string? was = null;
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await registry.DiscoverAsync(cancellationToken).ConfigureAwait(false);

            List<SystemDeviceNode> nodes =
            [
                .. enumerator.Enumerate()
                    .Where(n => match is not { Length: > 0 }
                        || (n.Name?.Contains(match, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (n.BusReportedName?.Contains(match, StringComparison.OrdinalIgnoreCase) ?? false)),
            ];

            string flag = nodes.Any(n => n.IsConnected == true) ? "подключён"
                : nodes.Any(n => n.IsConnected == false) ? "нет"
                : "—";

            int endpoints = new WindowsAudioEndpoints().Enumerate()
                .Count(e => !e.IsContainerless
                    && (match is not { Length: > 0 } || e.Name.Contains(match, StringComparison.OrdinalIgnoreCase)));

            DeviceSnapshot? device = registry.Devices.FirstOrDefault(d =>
                match is not { Length: > 0 }
                || d.Info.DisplayName.Contains(match, StringComparison.OrdinalIgnoreCase));

            string charge = device?.Capabilities.GetValueOrDefault(CapabilityIds.PowerBattery)?.Value is NumberValue n
                ? $"{n.Value}%"
                : "—";

            string now = $"{flag,-11}  {endpoints,-9}  {Volatile.Read(ref announcements),-7}  "
                + $"{device?.Availability.ToString() ?? "нет в списке",-15}  {charge}";

            if (now != was)
            {
                was = now;
                Console.WriteLine($"   {clock.Elapsed.TotalSeconds,6:0.0}s  {now}");
            }

            await Task.Delay(Tick, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine("Столбцы слева направо — цепочка. Где задержка, там и виноватый:");
        Console.WriteLine("  щёлкнули, а «события» и флаг стоят → молчит Windows, у нас всё быстро;");
        Console.WriteLine("  события пришли, а флаг стоит → Windows объявил, но мнения ещё не поменял;");
        Console.WriteLine("  флаг сменился, а состояние нет → опаздывает наш обход;");
        Console.WriteLine("  состояние сменилось, а заряд нет → опаздывает опрос или маячок.");

        return 0;
    }
}
