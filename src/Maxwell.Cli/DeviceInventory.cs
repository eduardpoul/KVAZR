using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.Mock;
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
    public static async Task<int> RunAsync(bool includeMocks, CancellationToken cancellationToken = default)
    {
        List<IDeviceDriver> drivers =
            [new AudezeRaceDriver(new WindowsHidEnumerator(), new WindowsHidTransportFactory())];

        if (includeMocks) drivers.Add(new MockDeviceDriver());

        using var registry = new DeviceRegistry(
            drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

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

            // A capability that has not answered is not zero and not off. Printing a
            // dash keeps the distinction visible in the harness too.
            _ => "—",
        };

        string bounds = state.Range is { } range ? $"   ({range.Min}..{range.Max})" : string.Empty;

        return value + bounds;
    }
}
