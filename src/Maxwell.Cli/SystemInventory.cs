using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Dumps what the operating system alone knows about the peripherals attached, grouped
/// the way the device layer will group them.
///
/// This runs before any driver exists for those devices on purpose. The tier-zero list
/// is what every user sees on first launch, and it has to be checked against a real
/// machine rather than assumed. Doing so immediately corrected two guesses: the friendly
/// name is useless on every node that has one, and hubs have to be told from peripherals
/// by setup class rather than by ignoring names that look like plumbing.
/// </summary>
internal static class SystemInventory
{
    /// <summary>
    /// Setup classes that mean "a person plugged this in". Everything else on the USB
    /// enumerators — hubs, root hubs, host controllers, serial bridges — is the wiring
    /// that carries peripherals rather than a peripheral, and listing it would bury the
    /// devices the user actually owns.
    /// </summary>
    private static readonly HashSet<Guid> PeripheralClasses =
    [
        new("745a17a0-74d3-11d0-b6fe-00a0c90f57da"), // HIDClass
        new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974"), // Bluetooth
        new("4d36e96c-e325-11ce-bfc1-08002be10318"), // Media — audio devices
        new("c166523c-fe0c-4a94-a586-f1a80cfbbf3e"), // AudioEndpoint
        new("4d36e96b-e325-11ce-bfc1-08002be10318"), // Keyboard
        new("4d36e96f-e325-11ce-bfc1-08002be10318"), // Mouse
        new("d61ca365-5af4-4486-998b-9db4734c6ca3"), // XnaComposite — Xbox controllers
        new("bb7df450-25cd-4504-9e7d-c435f514e5d0"), // Xbox peripherals
    ];

    public static int Run(bool raw)
    {
        IReadOnlyList<SystemDeviceNode> nodes = new WindowsSystemDeviceEnumerator().Enumerate();

        Console.WriteLine($"{nodes.Count} present device nodes across the peripheral enumerators.");
        Console.WriteLine();

        if (raw)
        {
            foreach (SystemDeviceNode n in nodes.OrderBy(n => n.InstanceId, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {n.Link,-12} {n.BusReportedName ?? n.Name ?? "(no name)"}");
                Console.WriteLine($"      {n.InstanceId}");
                Console.WriteLine(
                    $"      container {n.ContainerId}  class {n.ClassGuid}  battery {Show(n.BatteryPercent)}");
            }

            return 0;
        }

        List<IGrouping<Guid, SystemDeviceNode>> devices = nodes
            .GroupBy(n => n.ContainerId)
            .Where(g => g.Any(n => PeripheralClasses.Contains(n.ClassGuid)))
            .OrderByDescending(g => g.Any(n => n.BatteryPercent.HasValue))
            .ThenBy(Name, StringComparer.CurrentCulture)
            .ToList();

        foreach (IGrouping<Guid, SystemDeviceNode> device in devices)
        {
            int? battery = device.Select(n => n.BatteryPercent).FirstOrDefault(b => b.HasValue);
            uint? cod = device.Select(n => n.ClassOfDevice).FirstOrDefault(c => c.HasValue);
            SystemDeviceNode? ided = device.FirstOrDefault(n => n is { VendorId: not null, ProductId: not null });

            string ids = ided is { VendorId: { } v, ProductId: { } p } ? $"  {v:X4}:{p:X4}" : string.Empty;

            Console.WriteLine($"  {Name(device)}{ids}");
            Console.WriteLine(
                $"      {Links(device),-12} battery {Show(battery)}   {DescribeCod(cod)}   nodes {device.Count()}");
        }

        Console.WriteLine();
        Console.WriteLine($"{devices.Count} peripherals, {nodes.Select(n => n.ContainerId).Distinct().Count()} containers seen.");
        return 0;
    }

    /// <summary>
    /// What to call the device. The order matters and is not the obvious one: a
    /// Bluetooth device carries the name the user gave it on its root node only, while
    /// every profile node underneath is named after the profile. On USB the product
    /// string is the bus-reported description; <see cref="SystemDeviceNode.Name"/> there
    /// is the driver's own label and is the same "USB Input Device" for everything.
    /// </summary>
    private static string Name(IEnumerable<SystemDeviceNode> device)
    {
        List<SystemDeviceNode> nodes = [.. device];

        return nodes.FirstOrDefault(n => n.IsDeviceRoot && !string.IsNullOrWhiteSpace(n.Name))?.Name
            ?? nodes.Select(n => n.BusReportedName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? nodes.Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? "(unnamed)";
    }

    /// <summary>
    /// Reads the Bluetooth Class of Device, which is how a device says what it is
    /// without anyone knowing the model. Bits 2..7 are the minor class, 8..12 the major.
    /// </summary>
    private static string DescribeCod(uint? cod)
    {
        if (cod is not { } value) return "class —";

        uint major = (value >> 8) & 0x1F;
        uint minor = (value >> 2) & 0x3F;

        string name = (major, minor) switch
        {
            (0x04, 0x01) => "headset",
            (0x04, 0x02) => "hands-free",
            (0x04, 0x06) => "headphones",
            (0x04, 0x07) => "portable audio",
            (0x04, 0x0A) => "speaker",
            (0x04, _) => "audio",
            (0x05, 0x10) => "keyboard",
            (0x05, 0x20) => "pointing device",
            (0x05, 0x30) => "keyboard and pointer",
            (0x05, 0x08) => "gamepad",
            (0x05, _) => "peripheral",
            (0x02, _) => "phone",
            _ => $"major 0x{major:X2}",
        };

        return $"class {name} (0x{value:X6})";
    }

    private static string Links(IEnumerable<SystemDeviceNode> device) =>
        string.Join('+', device.Select(n => n.Link).Distinct().OrderBy(l => l));

    private static string Show(int? value) => value is { } v ? $"{v,3}%" : "  —";
}
