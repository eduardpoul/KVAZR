using Maxwell.Core.Abstractions;
using Maxwell.Race.Abstractions;
using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.Audio;
using Maxwell.Core.Drivers.Mock;
using Maxwell.Core.Drivers.System;
using Maxwell.Core.Polling;
using Maxwell.Race.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxwell.Cli;

/// <summary>
/// Exercises the multi-device layer end to end without any UI: discover through the
/// registry, read every capability through a session, print what came back.
///
/// This is how the device model gets proved on real hardware before a single screen
/// exists — the same order the protocol layer was built in, and for the same reason.
/// </summary>
internal static class DeviceInventory
{
    public static async Task<int> RunAsync(
        bool includeMocks,
        TimeSpan? watchFor = null,
        CancellationToken cancellationToken = default)
    {
        // The Bluetooth monitor matters here: paired over Bluetooth alone the headset
        // has no HID interface at all, and without this the harness would report no
        // device to someone who is wearing it.
        List<IDeviceDriver> drivers =
        [
            new AudezeRaceDriver(
                new WindowsHidEnumerator(),
                new WindowsHidTransportFactory(),
                new WindowsBluetoothMonitor()),

            // Everything else on the machine. Expected to be shadowed for the Audeze —
            // that is the arrangement working, not a clash.
            new SystemDeviceDriver(new WindowsSystemDeviceEnumerator(), new DeviceCatalogue()),
        ];

        if (includeMocks) drivers.Add(new MockDeviceDriver());

        // The same contributors the application composes, so the harness prints the page
        // a person would see rather than a thinner one.
        ICapabilityContributor[] contributors =
        [
            new AudioContributor(new WindowsAudioEndpoints(), new WindowsSystemDeviceEnumerator()),
        ];

        using var registry = new DeviceRegistry(
            drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance, contributors);

        if (watchFor is { } window) return await WatchAsync(registry, window, cancellationToken).ConfigureAwait(false);

        await registry.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (registry.Devices.Count == 0)
        {
            Console.WriteLine("No devices. Connect hardware, or pass --mock for stand-ins.");
            return 1;
        }

        foreach (DeviceSnapshot device in registry.Devices)
        {
            if (registry.Session(device.Info.Key) is { } session)
            {
                await session.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
            }

            Print(registry.Session(device.Info.Key)?.Snapshot ?? device);
        }

        return 0;
    }

    /// <summary>
    /// Runs the real scheduler against real hardware for a while and prints every read.
    /// The point is to watch the cadence: whether the interval stretches when nothing is
    /// on screen, whether a device that stops answering backs off instead of hammering.
    /// </summary>
    private static async Task<int> WatchAsync(
        DeviceRegistry registry,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var settings = new MemorySettingsStore();

        using var scheduler = new DevicePollingScheduler(
            registry,
            NullNotifier.Instance,
            settings,
            new StaticOptions<DevicePollingOptions>(new DevicePollingOptions
            {
                // Short enough to see several passes inside the watch window.
                Interval = TimeSpan.FromSeconds(3),
                DiscoveryInterval = TimeSpan.FromSeconds(2),
                IdleDiscoveryInterval = TimeSpan.FromSeconds(6),
            }),
            NullLogger<DevicePollingScheduler>.Instance);

        registry.DevicesChanged += (_, _) =>
        {
            foreach (DeviceSnapshot device in registry.Devices)
            {
                string battery = device.BatteryPercent is { } percent ? $"{percent}%" : "—";
                string chat = device.Get(CapabilityIds.MixChat)?.Value is NumberValue mix
                    ? $"  chatmix {mix.Value}"
                    : string.Empty;

                Console.WriteLine(
                    $"  {DateTimeOffset.Now:HH:mm:ss}  {device.Info.DisplayName,-28} " +
                    $"{device.Availability,-12} {battery,4}{chat}");
            }
        };

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Console.WriteLine($"Watching for {window.TotalSeconds:0} s. Attention: Idle -> Glance -> Focused.");
        Console.WriteLine();

        await scheduler.StartAsync(stop.Token).ConfigureAwait(false);

        // Walk the attention levels so the change in cadence is visible in the timestamps.
        await Task.Delay(window / 3, stop.Token).ConfigureAwait(false);
        Console.WriteLine("  -- flyout opened --");
        scheduler.SetAttention(PollingAttention.Glance);

        await Task.Delay(window / 3, stop.Token).ConfigureAwait(false);
        string? first = registry.Devices.FirstOrDefault(d => d.CanControl)?.Info.Key;
        Console.WriteLine($"  -- page opened: {first ?? "none"} --");
        scheduler.SetAttention(PollingAttention.Focused, first);

        await Task.Delay(window / 3, stop.Token).ConfigureAwait(false);

        await stop.CancelAsync().ConfigureAwait(false);
        await scheduler.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Stopped.");
        return 0;
    }

    private static void Print(DeviceSnapshot device)
    {
        Console.WriteLine();
        Console.WriteLine($"{device.Info.DisplayName}  [{device.Info.Class}]  {device.Availability}");
        Console.WriteLine($"  key {device.Info.Key}  driver {device.Info.DriverId}");

        foreach (TransportInfo transport in device.Info.Transports)
        {
            string carries = (transport.CarriesControl, transport.CarriesAudio) switch
            {
                (true, true) => "control+audio",
                (true, false) => "control",
                (false, true) => "audio only",
                _ => "presence only",
            };

            Console.WriteLine($"  transport  {transport.Label,-24} {carries}");
        }

        foreach (CapabilityDomain domain in device.Domains())
        {
            Console.WriteLine();
            Console.WriteLine($"  [{domain}]");

            foreach (CapabilityState state in device.InDomain(domain))
            {
                Console.WriteLine($"    {Mark(state)} {state.Descriptor.Title,-34} {Describe(state)}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>One character carrying the state the UI would draw as a badge.</summary>
    private static string Mark(CapabilityState state) => state switch
    {
        { IsAvailable: false } => "-",
        { Status: CapabilityStatus.Unavailable } => "x",
        { Status: CapabilityStatus.Untested } => "?",
        _ => " ",
    };

    private static string Describe(CapabilityState state)
    {
        string value = state.Value switch
        {
            NumberValue n => state.Range?.Unit is { } unit ? $"{n.Value} {unit}" : n.Value.ToString(),
            FlagValue f => f.Value ? "да" : "нет",
            TextValue t => t.Value,
            ChoiceValue c => state.Descriptor.Choices.FirstOrDefault(o => o.Index == c.Index)?.Label
                ?? $"#{c.Index}",

            // A curve or a channel pair, printed as its members. An unanswered member
            // stays a dash inside the list rather than collapsing the whole reading.
            VectorValue v => string.Join(" ", v.Items.Select(
                item => item is NumberValue m ? m.Value.ToString() : "—")),

            // A capability that has not answered is not zero and not off. Printing a
            // dash keeps the distinction visible in the harness too.
            _ => "—",
        };

        string bounds = state.Range is { } range ? $"   ({range.Min}..{range.Max})" : string.Empty;

        return value + bounds;
    }
}
