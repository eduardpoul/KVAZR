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
                byte[] inputIds = Declared(writer, preparsed, HidNative.HidpInput, "input");
                _ = Declared(writer, preparsed, HidNative.HidpOutput, "output");

                ReadFeatures(writer, handle, collection, featureIds);
                ReadInput(writer, handle, collection, inputIds);
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
    /// Polls every readable report on every collection of one vendor and prints what
    /// changes.
    ///
    /// The question it exists for: can the current state be asked for, or only waited
    /// for? A device that answers a read can have its page filled in the moment it is
    /// opened. One that does not leaves every row blank until somebody touches the
    /// hardware, which is a page that looks broken to the person who has just opened it.
    ///
    /// Both kinds of report, because the two answer differently on the same device. The
    /// QuadCast refuses to hand back its status report — measured, with the right report
    /// id — while the sixteen-byte channel on its audio function answers every time. What
    /// is in that answer is the whole question, and nothing but a dial in somebody's hand
    /// will settle it.
    ///
    /// The rate matters more than it looks. Four times a second is fine for a dial in
    /// somebody's hand and far too slow for a conversation between two pieces of
    /// software: a request written into a feature register and the answer read back out
    /// of it can be over in a few milliseconds, and at 250 ms it is invisible. Hence the
    /// interval, which is what turns this from a state watcher into an eavesdropper.
    ///
    /// Read-only at any rate. Nothing is written at any point.
    /// </summary>
    public static int Watch(TextWriter writer, ushort vendorId, int seconds, int everyMs = 250, bool quiet = false)
    {
        ArgumentNullException.ThrowIfNull(writer);

        List<Ear> ears = [.. Open(vendorId)];

        if (ears.Count == 0)
        {
            writer.WriteLine($"Nothing readable on 0x{vendorId:X4}.");
            return 1;
        }

        writer.WriteLine(
            $"Polling {ears.Count} report(s) for {seconds}s, every {everyMs} ms. Nothing is written.");
        writer.WriteLine();

        try
        {
            var last = new Dictionary<string, string>(StringComparer.Ordinal);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                foreach (Ear ear in ears)
                {
                    if (ear.Read() is not { } now) continue;
                    if (last.TryGetValue(ear.Tag, out string? before) && before == now) continue;

                    last[ear.Tag] = now;

                    if (quiet && IsChatter(now)) continue;
                    writer.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0}s  {ear.Tag}  {now}");
                }

                Thread.Sleep(everyMs);
            }

            writer.WriteLine();
            writer.WriteLine("Done.");
            return 0;
        }
        finally
        {
            foreach (Ear ear in ears) ear.Dispose();
        }
    }

    /// <summary>
    /// Frames that are known and boring, so a search for the unknown one is possible.
    ///
    /// The QuadCast's lighting is painted from the host at three frames a second and each
    /// frame passes through the same register a request would, which buries any exchange
    /// between the maker's software and the device in a thousand lines of rainbow. Three
    /// shapes account for all of it: the display header, a pair of colours, and the value
    /// the register holds when it is idle.
    ///
    /// A list of what to ignore rather than of what to keep, deliberately. The thing being
    /// looked for is by definition not yet known, and a filter that kept only recognised
    /// shapes would hide it.
    /// </summary>
    private static bool IsChatter(string frame) =>
        frame.StartsWith("00 04 F2", StringComparison.Ordinal)
        || frame.StartsWith("00 81", StringComparison.Ordinal)
        || frame.StartsWith("00 00 00 00 FF  (+60", StringComparison.Ordinal);

    /// <summary>Every report of every collection that will answer a read, opened once.</summary>
    private static IEnumerable<Ear> Open(ushort vendorId)
    {
        foreach (HidInterface collection in new WindowsHidEnumerator().EnumerateAll()
                     .Where(i => i.VendorId == vendorId)
                     .OrderBy(i => i.ProductId)
                     .ThenBy(i => i.UsagePage))
        {
            IntPtr handle = HidNative.CreateFile(
                collection.Path, Read, HidNative.FileShareReadWrite,
                IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

            if (handle == HidNative.InvalidHandle) continue;

            if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                HidNative.CloseHandle(handle);
                continue;
            }

            var shared = new Handle(handle, preparsed);
            bool used = false;

            foreach (byte id in Ids(preparsed, HidNative.HidpFeature))
            {
                used = true;
                yield return new Ear(shared, collection, id, feature: true);
            }

            foreach (byte id in Ids(preparsed, HidNative.HidpInput))
            {
                used = true;
                yield return new Ear(shared, collection, id, feature: false);
            }

            if (!used) shared.Dispose();
        }
    }

    /// <summary>The report ids of one kind, without printing them.</summary>
    private static byte[] Ids(IntPtr preparsed, int reportType)
    {
        var caps = new HidNative.HidpCaps();
        if (HidNative.HidP_GetCaps(preparsed, ref caps) != HidNative.HidpStatusSuccess) return [];

        ushort count = reportType == HidNative.HidpFeature
            ? caps.NumberFeatureValueCaps
            : caps.NumberInputValueCaps;

        if (count == 0) return [];

        var values = new HidNative.HidpValueCaps[count];
        if (HidNative.HidP_GetValueCaps(reportType, values, ref count, preparsed) != HidNative.HidpStatusSuccess)
        {
            return [];
        }

        return [.. values.Take(count).Select(v => v.ReportID).Distinct()];
    }

    /// <summary>One open handle, shared by every report of its collection.</summary>
    private sealed class Handle(IntPtr raw, IntPtr preparsed) : IDisposable
    {
        public IntPtr Raw { get; private set; } = raw;

        private int _users;

        public Handle Claim()
        {
            _users++;
            return this;
        }

        public void Release()
        {
            if (--_users > 0) return;

            Dispose();
        }

        public void Dispose()
        {
            if (Raw == IntPtr.Zero) return;

            HidNative.HidD_FreePreparsedData(preparsed);
            HidNative.CloseHandle(Raw);
            Raw = IntPtr.Zero;
        }
    }

    private sealed class Ear : IDisposable
    {
        private readonly Handle _handle;
        private readonly int _length;
        private readonly byte _id;
        private readonly bool _feature;

        public Ear(Handle handle, HidInterface collection, byte id, bool feature)
        {
            _handle = handle.Claim();
            _id = id;
            _feature = feature;
            _length = feature ? collection.FeatureReportLength : collection.InputReportLength;

            Tag = $"{collection.ProductId:X4} 0x{collection.UsagePage:X4} " +
                  $"{(feature ? "feat" : "in  ")} 0x{id:X2}";
        }

        public string Tag { get; }

        public string? Read()
        {
            if (_length == 0) return null;

            byte[] buffer = new byte[_length];
            buffer[0] = _id;

            bool ok = _feature
                ? HidNative.HidD_GetFeature(_handle.Raw, buffer, buffer.Length)
                : HidNative.HidD_GetInputReport(_handle.Raw, buffer, buffer.Length);

            return ok ? Hex(buffer) : null;
        }

        public void Dispose() => _handle.Release();
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

    /// <summary>
    /// Asks for the current contents of each input report rather than waiting for one to
    /// be sent.
    ///
    /// This is the difference between a page that knows what the hardware is doing when it
    /// opens and one that knows only what has changed since. A selector that reports on
    /// change and nothing else leaves a row reading "unknown" until somebody turns it,
    /// which is a bad row.
    ///
    /// The report id has to be right. The first version of this asked every collection for
    /// report 0 and wrote down "refused, sends only on change" for all of them — which was
    /// true of the question and not of the device: the mute and pattern channel numbers its
    /// report 0x05, and report 0 does not exist there at all.
    /// </summary>
    private static void ReadInput(TextWriter writer, IntPtr handle, HidInterface collection, byte[] declared)
    {
        if (collection.InputReportLength == 0) return;

        byte[] ids = declared.Length > 0 ? declared : [0];

        foreach (byte id in ids)
        {
            byte[] buffer = new byte[collection.InputReportLength];
            buffer[0] = id;

            writer.WriteLine(HidNative.HidD_GetInputReport(handle, buffer, buffer.Length)
                ? $"    input   0x{id:X2}   {Hex(buffer)}"
                : $"    input   0x{id:X2}   refused");
        }
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
