using System.Globalization;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Diagnostics;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

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
        List<HidInterface> pointers =
        [
            .. new WindowsHidEnumerator().EnumerateAll().Where(PointerSurvey.IsPointer),
        ];

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

            Console.WriteLine();
        }

        Console.WriteLine("Everything above is declared, not measured: no report was read and nothing was");
        Console.WriteLine("written. A mouse cannot be opened for reading while Windows is driving it, which");
        Console.WriteLine("is why the declaration is the floor a page has to be built on.");

        return 0;
    }
}
