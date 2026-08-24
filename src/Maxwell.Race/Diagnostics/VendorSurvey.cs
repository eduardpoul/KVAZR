using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;
using Maxwell.Race.Native;
using Maxwell.Race.Windows;

namespace Maxwell.Race.Diagnostics;

/// <summary>
/// What one vendor's HID collections declare, and what they answer when asked.
///
/// Read-only, every line of it. A vendor pipe is a door into firmware and a wrong
/// sixty-four bytes is a write into a register nobody has decoded; the first thing to do
/// with one is to find out what it says when nothing is written at all.
///
/// The device this was built for is a HyperX QuadCast S, whose lighting is known to be
/// driven over a 64-byte feature report and whose gain knob has been measured to say
/// nothing at all. Between those two facts is a question worth asking before any of it is
/// implemented: does the pipe answer? A collection that declares feature reports and
/// returns their current contents is one the microphone's own state can be read from,
/// which is the difference between a page that shows what the hardware is doing and a page
/// that only writes at it and hopes.
///
/// Declared rather than swept, because Windows already knows. The report descriptor was
/// parsed by the HID class driver at enumeration and HidP_GetValueCaps will list every
/// report id, its size and its usages — which is the same answer a blind sweep of 256 ids
/// would take a minute to guess at, only exact.
/// </summary>
[SupportedOSPlatform("windows")]
public static class VendorSurvey
{
    /// <summary>GENERIC_READ. Query-only access is enough to enumerate but not to read a report.</summary>
    private const uint Read = 0x80000000;

    public static int Dump(TextWriter writer, ushort vendorId)
    {
        ArgumentNullException.ThrowIfNull(writer);

        List<HidInterface> collections =
        [
            .. new WindowsHidEnumerator().EnumerateAll()
                .Where(i => i.VendorId == vendorId)
                .OrderBy(i => i.ProductId)
                .ThenBy(i => i.UsagePage),
        ];

        if (collections.Count == 0)
        {
            writer.WriteLine($"Nothing on this machine has vendor id 0x{vendorId:X4}.");
            return 1;
        }

        writer.WriteLine($"{collections.Count} collection(s) with vendor id 0x{vendorId:X4}. Nothing is written.");
        writer.WriteLine();

        foreach (HidInterface collection in collections)
        {
            Describe(writer, collection);
            writer.WriteLine();
        }

        return 0;
    }

    private static void Describe(TextWriter writer, HidInterface collection)
    {
        writer.WriteLine(
            $"{collection.VendorId:X4}:{collection.ProductId:X4}  page 0x{collection.UsagePage:X4} " +
            $"usage 0x{collection.Usage:X2}   in {collection.InputReportLength} " +
            $"out {collection.OutputReportLength} feat {collection.FeatureReportLength}");

        // Read access, not the query-only handle the enumeration uses: a handle opened
        // for nothing can be asked what a device is and not what it holds.
        IntPtr handle = HidNative.CreateFile(
            collection.Path, Read, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
        {
            writer.WriteLine("    will not open for reading — something else holds it, or the class driver refuses.");
            return;
        }

        try
        {
            if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                writer.WriteLine("    no preparsed data.");
                return;
            }

            try
            {
                byte[] featureIds = Declared(writer, preparsed, HidNative.HidpFeature, "feature");
                _ = Declared(writer, preparsed, HidNative.HidpInput, "input");
                _ = Declared(writer, preparsed, HidNative.HidpOutput, "output");

                ReadFeatures(writer, handle, collection, featureIds);
                ReadInput(writer, handle, collection);
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            HidNative.CloseHandle(handle);
        }
    }

    /// <summary>
    /// The report ids of one kind that this collection declares, with what each one holds.
    ///
    /// Only value caps are asked for. A vendor pipe is a block of opaque bytes rather than
    /// named controls, and it shows up here as one value of eight bits repeated sixty-four
    /// times — which is exactly the shape that says "this is a transport, not a control".
    /// </summary>
    private static byte[] Declared(TextWriter writer, IntPtr preparsed, int reportType, string what)
    {
        var caps = new HidNative.HidpCaps();
        if (HidNative.HidP_GetCaps(preparsed, ref caps) != HidNative.HidpStatusSuccess) return [];

        ushort count = reportType switch
        {
            HidNative.HidpFeature => caps.NumberFeatureValueCaps,
            HidNative.HidpOutput => caps.NumberOutputValueCaps,
            _ => caps.NumberInputValueCaps,
        };

        if (count == 0) return [];

        var values = new HidNative.HidpValueCaps[count];
        if (HidNative.HidP_GetValueCaps(reportType, values, ref count, preparsed) != HidNative.HidpStatusSuccess)
        {
            return [];
        }

        var ids = new List<byte>();

        foreach (HidNative.HidpValueCaps value in values.Take(count))
        {
            if (!ids.Contains(value.ReportID)) ids.Add(value.ReportID);

            writer.WriteLine(
                $"    {what,-7} report 0x{value.ReportID:X2}   usage 0x{value.UsagePage:X4}:0x{value.UsageMin:X2}" +
                (value.IsRange != 0 ? $"..0x{value.UsageMax:X2}" : string.Empty) +
                $"   {value.ReportCount} × {value.BitSize} bit");
        }

        return [.. ids];
    }

    private static void ReadFeatures(
        TextWriter writer, IntPtr handle, HidInterface collection, byte[] declared)
    {
        if (collection.FeatureReportLength == 0)
        {
            writer.WriteLine("    no feature reports.");
            return;
        }

        // Report id zero when the collection declares none: a descriptor with a single
        // unnumbered report is the commonest shape for a vendor pipe, and Windows still
        // wants the leading byte.
        byte[] ids = declared.Length > 0 ? declared : [0];

        foreach (byte id in ids)
        {
            byte[] buffer = new byte[collection.FeatureReportLength];
            buffer[0] = id;

            if (!HidNative.HidD_GetFeature(handle, buffer, buffer.Length))
            {
                writer.WriteLine($"    feature 0x{id:X2}   refused");
                continue;
            }

            writer.WriteLine($"    feature 0x{id:X2}   {Hex(buffer)}");
        }
    }

    /// <summary>
    /// Polls every feature report on one collection and prints only what changes.
    ///
    /// The question this answers is worth more than its size: a vendor pipe that answers a
    /// read at all might be answering with a constant, or it might be handing back the
    /// state of the device. If turning a knob moves a byte here, the whole page can be
    /// read from this pipe and nothing has to be guessed. If nothing moves while the
    /// hardware plainly does, then this pipe only takes orders, and the state has to come
    /// from somewhere else.
    ///
    /// Read-only, at four times a second, which is slow enough to be polite to firmware
    /// and fast enough that a hand on a dial shows up on the same line it happened.
    /// </summary>
    public static int Watch(TextWriter writer, ushort vendorId, ushort usagePage, int seconds)
    {
        ArgumentNullException.ThrowIfNull(writer);

        HidInterface? found = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == vendorId && i.UsagePage == usagePage && i.FeatureReportLength > 0);

        if (found is not { } collection)
        {
            writer.WriteLine($"No collection with feature reports on 0x{vendorId:X4} page 0x{usagePage:X4}.");
            return 1;
        }

        IntPtr handle = HidNative.CreateFile(
            collection.Path, Read, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
        {
            writer.WriteLine("Will not open for reading.");
            return 1;
        }

        writer.WriteLine($"Polling feature 0x00 on page 0x{usagePage:X4} for {seconds}s. Nothing is written.");
        writer.WriteLine("Turn every dial on the device and press every button; only changes are printed.");
        writer.WriteLine();

        try
        {
            string last = string.Empty;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                byte[] buffer = new byte[collection.FeatureReportLength];

                if (HidNative.HidD_GetFeature(handle, buffer, buffer.Length))
                {
                    string now = Hex(buffer);

                    if (now != last)
                    {
                        writer.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0}s   {now}");
                        last = now;
                    }
                }

                Thread.Sleep(250);
            }

            writer.WriteLine();
            writer.WriteLine("Done.");
            return 0;
        }
        finally
        {
            HidNative.CloseHandle(handle);
        }
    }

    /// <summary>
    /// One feature report from one collection, as hex, or null when there is nothing to
    /// read. For callers that want the value rather than a printed survey.
    /// </summary>
    public static string? ReadFeature(ushort vendorId, ushort usagePage)
    {
        HidInterface? found = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == vendorId && i.UsagePage == usagePage && i.FeatureReportLength > 0);

        if (found is not { } collection) return null;

        IntPtr handle = HidNative.CreateFile(
            collection.Path, Read, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle) return null;

        try
        {
            byte[] buffer = new byte[collection.FeatureReportLength];
            return HidNative.HidD_GetFeature(handle, buffer, buffer.Length) ? Hex(buffer) : null;
        }
        finally
        {
            HidNative.CloseHandle(handle);
        }
    }

    private static void ReadInput(TextWriter writer, IntPtr handle, HidInterface collection)
    {
        if (collection.InputReportLength == 0) return;

        byte[] buffer = new byte[collection.InputReportLength];

        // Asking for the current input rather than waiting for one. A device that only
        // sends on change — which is every button — answers this with what it last sent,
        // and that is the state a page would want on the way up.
        writer.WriteLine(HidNative.HidD_GetInputReport(handle, buffer, buffer.Length)
            ? $"    input now    {Hex(buffer)}"
            : "    input now    refused (sends only on change)");
    }

    /// <summary>
    /// Hex, with the tail of zeroes summarised. A sixty-four byte pipe is mostly padding
    /// and printing all of it hides the eight bytes that are not.
    /// </summary>
    private static string Hex(byte[] buffer)
    {
        int last = buffer.Length - 1;
        while (last > 0 && buffer[last] == 0) last--;

        string shown = string.Join(' ', buffer.Take(last + 1).Select(b => b.ToString("X2")));
        int padding = buffer.Length - last - 1;

        return padding > 0 ? $"{shown}  (+{padding} zero)" : shown;
    }
}
