using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Native;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Diagnostics;

/// <summary>One declared report in a collection's HID descriptor.</summary>
public sealed record ReportDeclaration(int Direction, byte ReportId, ushort UsagePage, ushort Usage, int Bits)
{
    public string DirectionName => Direction switch
    {
        HidNative.HidpInput => "input",
        HidNative.HidpOutput => "output",
        _ => "feature",
    };

    public int Bytes => Bits / 8;
}

/// <summary>
/// Reads the report IDs a collection actually declares, so guesses about
/// 0x06 / 0x07 can be replaced with what the device really exposes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HidDescriptor
{
    public static List<ReportDeclaration> Describe(MaxwellInterface iface) => Describe(iface.Path);

    /// <summary>
    /// The same by path, for callers holding the other shape of interface. Every HID
    /// device on the machine has a path and only some of them are a Maxwell.
    /// </summary>
    public static List<ReportDeclaration> Describe(string path)
    {
        var declarations = new List<ReportDeclaration>();

        IntPtr handle = HidNative.CreateFile(path, 0, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle == HidNative.InvalidHandle) return declarations;

        try
        {
            if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return declarations;

            try
            {
                var caps = new HidNative.HidpCaps();
                if (HidNative.HidP_GetCaps(preparsed, ref caps) != HidNative.HidpStatusSuccess)
                    return declarations;

                Collect(HidNative.HidpInput, caps.NumberInputValueCaps, preparsed, declarations);
                Collect(HidNative.HidpOutput, caps.NumberOutputValueCaps, preparsed, declarations);
                Collect(HidNative.HidpFeature, caps.NumberFeatureValueCaps, preparsed, declarations);
            }
            finally { HidNative.HidD_FreePreparsedData(preparsed); }
        }
        finally { HidNative.CloseHandle(handle); }

        return declarations;
    }

    private static void Collect(int direction, ushort count, IntPtr preparsed, List<ReportDeclaration> into)
    {
        if (count == 0) return;

        var buffer = new HidNative.HidpValueCaps[count];
        ushort length = count;
        if (HidNative.HidP_GetValueCaps(direction, buffer, ref length, preparsed) < 0) return;

        for (int i = 0; i < length; i++)
        {
            HidNative.HidpValueCaps cap = buffer[i];
            into.Add(new ReportDeclaration(
                direction,
                cap.ReportID,
                cap.UsagePage,
                cap.IsRange != 0 ? cap.UsageMin : cap.UsageMin,
                cap.BitSize * cap.ReportCount));
        }
    }

    public static void Dump(TextWriter output)
    {
        foreach (MaxwellInterface iface in new Windows.WindowsHidEnumerator().Enumerate())
        {
            string collection = ExtractCollection(iface.Path);
            output.WriteLine($"  PID 0x{iface.ProductId:X4}  {collection}  usage {iface.UsagePage:X4}:{iface.Usage:X2}  " +
                             $"in {iface.InputReportLength} out {iface.OutputReportLength}");

            List<ReportDeclaration> declarations = Describe(iface);
            if (declarations.Count == 0)
            {
                output.WriteLine("      (no value caps declared)");
                continue;
            }

            foreach (ReportDeclaration declaration in declarations)
                output.WriteLine($"      {declaration.DirectionName,-7} reportId 0x{declaration.ReportId:X2}  " +
                                 $"usage {declaration.UsagePage:X4}:{declaration.Usage:X2}  {declaration.Bytes} bytes");
        }
    }

    private static string ExtractCollection(string path)
    {
        int index = path.IndexOf("col", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && index + 5 <= path.Length ? path.Substring(index, 5) : "-----";
    }
}
