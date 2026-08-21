using Maxwell.Race.Abstractions;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Lists every HID interface on the machine with its usage page, so a class protocol can
/// be found before any code is written to speak it.
///
/// The usage page is the whole question. VIA answers on 0xFF60 whatever brand is printed
/// on the keyboard, HID++ on 0xFF00 for every Logitech receiver — so before implementing
/// either it is worth knowing whether anything here answers at all. Building a protocol
/// against hardware that turns out not to expose it is the most expensive way to find out.
/// </summary>
internal static class HidSurvey
{
    /// <summary>Vendor-defined pages start here; everything below is a standard class.</summary>
    private const ushort VendorDefined = 0xFF00;

    private static readonly (ushort Page, string What)[] Known =
    [
        (0x0001, "Generic Desktop"),
        (0x000C, "Consumer"),
        (0x0085, "Battery System"),
        (0xFF00, "vendor · HID++ lives here"),
        (0xFF13, "vendor · Audeze RACE"),
        (0xFF60, "vendor · VIA / QMK"),
    ];

    public static int Run(bool vendorOnly)
    {
        IReadOnlyList<HidInterface> interfaces = new WindowsHidEnumerator().EnumerateAll();

        List<HidInterface> shown =
        [
            .. interfaces
                .Where(i => !vendorOnly || i.UsagePage >= VendorDefined)
                .OrderBy(i => i.VendorId)
                .ThenBy(i => i.ProductId)
                .ThenBy(i => i.UsagePage),
        ];

        Console.WriteLine($"{interfaces.Count} HID interfaces, {shown.Count} shown.");
        Console.WriteLine();

        foreach (HidInterface iface in shown)
        {
            string what = Known.FirstOrDefault(k => k.Page == iface.UsagePage).What ?? string.Empty;

            Console.WriteLine(
                $"  {iface.VendorId:X4}:{iface.ProductId:X4}  page 0x{iface.UsagePage:X4} usage 0x{iface.Usage:X2}  " +
                $"in {iface.InputReportLength,3} out {iface.OutputReportLength,3}  {iface.ProductName}");

            if (what.Length > 0) Console.WriteLine($"        {what}");
        }

        Console.WriteLine();

        foreach ((ushort page, string what) in Known.Where(k => k.Page >= VendorDefined))
        {
            int count = interfaces.Count(i => i.UsagePage == page);
            Console.WriteLine($"  0x{page:X4}  {count} interface(s)   {what}");
        }

        return 0;
    }
}
