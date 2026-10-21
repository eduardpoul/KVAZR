using System.Globalization;
using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Diagnostics;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// The anatomy of one vendor's HID collections: what each one declares, report by report.
///
/// The existing dump answers the same question for the Audeze interfaces alone, which was
/// the only device that needed it at the time. This asks it of anything — a keyboard's
/// dongle, a dock, a light controller — because the first thing worth knowing about an
/// unknown vendor channel is its shape: how many reports, which ids, how many bytes, and
/// in which direction. A 65-byte pipe with one report id and no feature reports is a
/// request-and-answer protocol; that much can be read off the descriptor before a single
/// byte is sent.
///
/// Read only. Descriptors are parsed and input pipes are never opened.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CollectionProbe
{
    public static int Run(string? vendor)
    {
        if (vendor is not { Length: > 0 }
            || !ushort.TryParse(vendor, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort vid))
        {
            Console.WriteLine("usage: --collections 19f5");
            return 1;
        }

        List<HidInterface> mine =
        [
            .. new WindowsHidEnumerator().EnumerateAll()
                .Where(i => i.VendorId == vid)
                .OrderBy(i => i.ProductId)
                .ThenBy(i => i.UsagePage)
                .ThenBy(i => i.Usage),
        ];

        if (mine.Count == 0)
        {
            Console.WriteLine($"Ничего с вендором 0x{vid:X4} не подключено.");
            return 1;
        }

        Console.WriteLine($"{mine.Count} коллекц(ий) у вендора 0x{vid:X4}.");
        Console.WriteLine();

        foreach (HidInterface iface in mine)
        {
            HidDescriptor.HidCollectionFacts facts = HidDescriptor.Inspect(iface.Path);

            Console.WriteLine(
                $"  {iface.VendorId:X4}:{iface.ProductId:X4}  page {iface.UsagePage:X4}:{iface.Usage:X2}"
                + $"  вход {iface.InputReportLength}  выход {iface.OutputReportLength}"
                + $"  feature {iface.FeatureReportLength}   {iface.ProductName}");

            if (!facts.Read)
            {
                Console.WriteLine("      дескриптор не прочитан");
                Console.WriteLine();
                continue;
            }

            Console.WriteLine($"      кнопок {facts.Buttons}, значений {facts.Values}");

            foreach (ReportDeclaration d in facts.Declarations
                .OrderBy(d => d.Direction)
                .ThenBy(d => d.ReportId))
            {
                Console.WriteLine(
                    $"      {d.DirectionName,-7} отчёт 0x{d.ReportId:X2}  "
                    + $"page {d.UsagePage:X4} usage 0x{d.Usage:X2}  {d.Bits} бит = {d.Bytes} байт");
            }

            Console.WriteLine();
        }

        return 0;
    }
}
