using Maxwell.Core.Abstractions;
using Maxwell.Race.Abstractions;
using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.Audio;
using Maxwell.Core.Drivers.Mock;
using Maxwell.Core.Drivers.System;
using Maxwell.Core.Drivers.Logitech;
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
        bool vendorless = false,
        string? focus = null,
        CancellationToken cancellationToken = default)
    {
        List<IDeviceDriver> drivers =
        [
            // Everything on the machine, understood as little as possible.
            new SystemDeviceDriver(new WindowsSystemDeviceEnumerator(), new DeviceCatalogue()),
        ];

        // --no-vendor drops the deep drivers and leaves the class protocols, so a device
        // that has a vendor driver falls through to the shared page and that page can be
        // read off real hardware instead of imagined. Otherwise the Audeze goes in front,
        // where it shadows the system tier's copy of the same headset — the arrangement
        // working, not a clash.
        // The Bluetooth monitor matters here: paired over Bluetooth alone the headset has
        // no HID interface at all, and without it the harness would report no device to
        // someone who is wearing one.
        if (!vendorless)
        {
            drivers.Insert(0, new AudezeRaceDriver(
                new WindowsHidEnumerator(),
                new WindowsHidTransportFactory(),
                new WindowsBluetoothMonitor()));

            // HID++ goes with them. It is a class protocol rather than one vendor's, but
            // for the purpose of this switch that is a distinction without a difference:
            // what --no-vendor is for is seeing what a device gives before anybody has
            // written a protocol for it, and a Logitech mouse with HID++ in front of it
            // is not that.
            drivers.Insert(1, new HidPlusPlusDriver(new WindowsHidEnumerator(), new DeviceCatalogue()));
        }

        if (includeMocks) drivers.Add(new MockDeviceDriver());

        // The radio listener, started here and disposed with the run. It has to exist
        // before the sweep and outlive it: a beacon arrives when it arrives, and a
        // listener created per read would hear nothing at all.
        using var listening = new Maxwell.Race.Windows.WindowsProximityListener();

        // The same contributors the application composes, so the harness prints the page
        // a person would see rather than a thinner one.
        ICapabilityContributor[] contributors =
        [
            new AudioContributor(new WindowsAudioEndpoints(), new WindowsSystemDeviceEnumerator()),
            new Maxwell.Core.Drivers.HyperX.HyperXContributor(new WindowsHidListenerFactory()),
            new Maxwell.Core.Drivers.Apple.AppleProximityContributor(listening),
            new Maxwell.Core.Drivers.Keyboard.ViaContributor(new Maxwell.Race.Windows.WindowsHidReportPipeFactory()),
        ];

        using var registry = new DeviceRegistry(
            drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance, contributors);

        if (watchFor is { } window)
        {
            return await WatchAsync(registry, window, focus, cancellationToken).ConfigureAwait(false);
        }

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
        string? focus,
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

        // Transitions, not every read. A pass prints six lines and the interesting
        // event is one device changing its mind twenty seconds in; at that ratio the
        // change is there but nobody finds it.
        Dictionary<string, string> said = new(StringComparer.Ordinal);

        registry.DevicesChanged += (_, _) =>
        {
            foreach (DeviceSnapshot device in registry.Devices)
            {
                string battery = device.BatteryPercent is { } percent ? $"{percent}%" : "—";
                string chat = device.Get(CapabilityIds.MixChat)?.Value is NumberValue mix
                    ? $"  chatmix {mix.Value}"
                    : string.Empty;

                string line = $"{device.Availability,-12} {battery,4}{chat}";

                if (said.TryGetValue(device.Info.Key, out string? before) && before == line) continue;

                said[device.Info.Key] = line;

                Console.WriteLine(
                    $"  {DateTimeOffset.Now:HH:mm:ss}  {device.Info.DisplayName,-28} {line}");
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
        string? first = (focus is { Length: > 0 } wanted
            ? registry.Devices.FirstOrDefault(
                d => d.Info.DisplayName.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            : registry.Devices.Count > 0 ? registry.Devices[0] : null)?.Info.Key;
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
        // Marked when the application would sweep it aside, because this harness prints
        // the registry unfiltered and that is the point of it — but an entry the reader
        // will never see on a page looks like a leak, and one was reported as such.
        string swept = DeviceVisibility.IsSilent(device.Info) || device.Info.Auxiliary
            ? "   (в приложении скрыто)"
            : string.Empty;

        Console.WriteLine($"{device.Info.DisplayName}  [{device.Info.Class}]  {device.Availability}{swept}");
        Console.WriteLine($"  key {device.Info.Key}  driver {device.Info.DriverId}");

        // The identities the device was recognised by. Worth printing because one of them
        // is operational rather than descriptive: "forget in the system" needs the
        // Bluetooth address, and without a bt: id there is nothing to unpair.
        if (device.Info.HardwareIds.Count > 0)
        {
            Console.WriteLine($"  ids {string.Join("  ", device.Info.HardwareIds)}");
        }

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
            // The whole list, not only the one in force. What a device offers is as much
            // the point as what it has chosen — and a list that reads oddly is the first
            // sign that a reply was decoded from the wrong byte.
            ChoiceValue c => state.Descriptor.Choices.Count > 1
                ? $"{state.Descriptor.Choices.FirstOrDefault(o => o.Index == c.Index)?.Label}"
                    + $"   of {string.Join(", ", state.Descriptor.Choices.Select(o => o.Label))}"
                : state.Descriptor.Choices.FirstOrDefault(o => o.Index == c.Index)?.Label
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
