using System.Globalization;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Diagnostics;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Every pointing device on the machine and what it says about itself without being asked
/// anything at all.
///
/// The question behind it is what a mouse page could be built from. Three links, and they
/// are not three answers: over a cable and over Bluetooth the operating system sees the
/// mouse itself, while over a receiver it sees the receiver and a mouse-shaped collection
/// on it — so a dongle is the case where the descriptor alone stops being enough and a
/// vendor protocol starts being the only way to say which mouse, or what its charge is.
/// </summary>
internal static class PointerProbe
{
    public static int Run()
    {
        List<HidInterface> all = [.. new WindowsHidEnumerator().EnumerateAll()];
        List<HidInterface> pointers = [.. all.Where(PointerSurvey.IsPointer)];

        Dictionary<string, RawPointer> windows = RawInputPointers.Enumerate()
            .GroupBy(p => RawPointer.Key(p.Path), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Console.WriteLine("[pointers] What every mouse on this machine declares");
        Console.WriteLine();

        if (pointers.Count == 0)
        {
            Console.WriteLine("  No mouse collection found.");
            return 1;
        }

        foreach (HidInterface face in pointers)
        {
            if (PointerSurvey.Describe(face) is not { } facts)
            {
                Console.WriteLine($"  {face.ProductName}  {face.VendorId:X4}:{face.ProductId:X4}  — could not be opened");
                Console.WriteLine();
                continue;
            }

            Console.WriteLine(
                $"  {(face.ProductName is { Length: > 0 } named ? named : "(unnamed)")}  "
                + $"{face.VendorId:X4}:{face.ProductId:X4}");

            Console.WriteLine($"      link      {facts.Link}");
            Console.WriteLine($"      report    {face.InputReportLength} bytes in"
                              + (face.FeatureReportLength > 0 ? $", {face.FeatureReportLength} feature" : string.Empty));
            Console.WriteLine($"      buttons   {facts.Buttons}");

            foreach (PointerAxis axis in facts.Axes)
            {
                Console.WriteLine(
                    $"      axis      {axis.Name,-22} {axis.Bits,2} bits  "
                    + $"{axis.Min.ToString(CultureInfo.InvariantCulture)}..{axis.Max.ToString(CultureInfo.InvariantCulture)}");
            }

            Console.WriteLine($"      wheel     {(facts.HasFineWheel ? "high resolution" : "stepped")}"
                              + $"{(facts.HasTilt ? ", tilts" : string.Empty)}");

            Console.WriteLine($"      write     {PointerSurvey.WriteTest(face)}");

            // Windows' own account of the same device, which is the other universal
            // source and the only one that states a button count and a report rate
            // rather than passing on what the device declared.
            if (windows.TryGetValue(RawPointer.Key(face.Path), out RawPointer? raw))
            {
                Console.WriteLine(
                    $"      windows   buttons {raw.Buttons}  rate {raw.SampleRate}"
                    + $"  horizontal wheel {(raw.HasHorizontalWheel ? "yes" : "no")}");
            }

            // The rest of the same device. A mouse's settings, where it has any without a
            // vendor protocol, live on a sibling collection rather than on the one the
            // cursor is driven from — Windows has that one open for itself.
            foreach (HidInterface sibling in all.Where(i =>
                         i.VendorId == face.VendorId
                         && i.ProductId == face.ProductId
                         && !ReferenceEquals(i, face)))
            {
                Console.WriteLine(
                    $"      also      page 0x{sibling.UsagePage:X4} usage 0x{sibling.Usage:X2}  "
                    + $"in {sibling.InputReportLength}  out {sibling.OutputReportLength}  "
                    + $"feature {sibling.FeatureReportLength}  · write {PointerSurvey.WriteTest(sibling)}");

                List<(byte Id, byte[] Data)> features = PointerSurvey.Features(sibling);

                foreach ((byte id, byte[] data) in features)
                {
                    Console.WriteLine(
                        $"        feature 0x{id:X2}  {Convert.ToHexString(data.AsSpan(0, Math.Min(20, data.Length)))}");
                }

                // Said explicitly, because "no lines here" and "nothing answered" are the
                // same sight and completely different facts: a collection that declares
                // feature reports and answers none has nothing behind the declaration.
                if (sibling.FeatureReportLength > 0 && features.Count == 0)
                {
                    Console.WriteLine("        feature   declared, none answered");
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine("Everything above is declared, not measured: no report was read and nothing was");
        Console.WriteLine("written. A mouse cannot be opened for reading while Windows is driving it, which");
        Console.WriteLine("is why the declaration is the floor a page has to be built on.");

        return 0;
    }
}
