using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Diagnostics;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Which devices on this desk report a charge the standard way.
///
/// The plan calls this the measurement before the work, and it had never been taken.
/// HID has three ways to declare a battery — Generic Device Controls 0x06:0x20 for the
/// one byte a wireless keyboard or mouse usually sends, and the Power Device and Battery
/// System pages 0x84 and 0x85 for the full description a UPS gives — and if a device
/// declares any of them, its charge is readable by a class driver with no vendor protocol
/// behind it and no reverse engineering.
///
/// If nothing on the desk declares one, that is the answer and it is worth having: the
/// charge over a 2.4 GHz dongle is then a vendor matter for every keyboard, and a phase
/// of work goes down the queue rather than being attempted blind.
///
/// Read only. Descriptors are parsed; nothing is opened for input and nothing is written.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HidBatteryProbe
{
    /// <summary>Generic Device Controls, where a wireless peripheral puts its one byte.</summary>
    private const ushort GenericDeviceControls = 0x0006;
    private const ushort BatteryStrength = 0x0020;

    /// <summary>The two pages a device with something to say about power uses.</summary>
    private const ushort PowerDevice = 0x0084;
    private const ushort BatterySystem = 0x0085;

    public static int Run(bool everything)
    {
        List<HidInterface> all = [.. new WindowsHidEnumerator().EnumerateAll()];

        Console.WriteLine($"{all.Count} HID-коллекций. Ищу штатное объявление заряда.");
        Console.WriteLine();

        var found = 0;
        var unread = 0;

        foreach (HidInterface iface in all
            .OrderBy(i => i.VendorId)
            .ThenBy(i => i.ProductId)
            .ThenBy(i => i.UsagePage))
        {
            HidDescriptor.HidCollectionFacts facts = HidDescriptor.Inspect(iface.Path);
            IReadOnlyList<ReportDeclaration> declared = facts.Declarations;

            if (!facts.Read) unread++;

            List<ReportDeclaration> power =
            [
                .. declared.Where(d =>
                    (d.UsagePage == GenericDeviceControls && d.Usage == BatteryStrength)
                    || d.UsagePage is PowerDevice or BatterySystem),
            ];

            if (power.Count == 0)
            {
                if (!everything) continue;

                Console.WriteLine(
                    $"  {iface.VendorId:X4}:{iface.ProductId:X4}  page {iface.UsagePage:X4}:{iface.Usage:X2}"
                    + $"  {iface.ProductName}  — "
                    + (facts.Read
                        ? $"кнопок {facts.Buttons}, значений {facts.Values}, заряда нет"
                        : "ДЕСКРИПТОР НЕ ПРОЧИТАН"));

                continue;
            }

            found++;

            Console.WriteLine(
                $"  {iface.VendorId:X4}:{iface.ProductId:X4}  page {iface.UsagePage:X4}:{iface.Usage:X2}  {iface.ProductName}");

            foreach (ReportDeclaration d in power)
            {
                Console.WriteLine(
                    $"      {d.DirectionName,-7} отчёт 0x{d.ReportId:X2}  "
                    + $"{Page(d.UsagePage)} usage 0x{d.Usage:X2}  {d.Bits} бит");
            }
        }

        Console.WriteLine();
        if (found > 0)
        {
            Console.WriteLine($"Объявляют штатный заряд: {found}. Их читает классовый драйвер без вендорского протокола.");
        }
        else if (unread > 0)
        {
            Console.WriteLine($"Заряда не объявил никто — но {unread} из {all.Count} коллекций не открылись.");
            Console.WriteLine("Про них ответа нет, и вывод «ни у кого нет батареи» пока не заслужен.");
        }
        else
        {
            Console.WriteLine($"Все {all.Count} коллекций прочитаны, заряд не объявляет ни одна.");
            Console.WriteLine("Значит по донглу и кабелю заряд — вендорский разговор для каждой модели.");
        }

        return 0;
    }

    private static string Page(ushort page) => page switch
    {
        GenericDeviceControls => "Generic Device Controls",
        PowerDevice => "Power Device",
        BatterySystem => "Battery System",
        _ => $"0x{page:X4}",
    };
}
