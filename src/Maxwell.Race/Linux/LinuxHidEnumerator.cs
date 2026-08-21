using System.Globalization;
using System.Runtime.Versioning;
using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Linux;

/// <summary>
/// Finds Maxwell hidraw nodes through sysfs. There is no SetupAPI here: the kernel
/// publishes each node under /sys/class/hidraw, and the identifying data is read out of
/// plain text files next to it.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHidEnumerator : IHidEnumerator
{
    private const string HidrawClass = "/sys/class/hidraw";

    public IReadOnlyList<MaxwellInterface> Enumerate()
    {
        var found = new List<MaxwellInterface>();
        if (!Directory.Exists(HidrawClass)) return found;

        foreach (string node in Directory.EnumerateDirectories(HidrawClass))
        {
            string name = Path.GetFileName(node);
            string uevent = Path.Combine(node, "device", "uevent");
            if (!File.Exists(uevent)) continue;

            if (!TryReadIds(uevent, out ushort vendor, out ushort product)) continue;
            if (vendor != MaxwellInterface.VendorId) continue;
            if (Array.IndexOf(MaxwellInterface.KnownProductIds, product) < 0) continue;

            (ushort usagePage, int inputLength, int outputLength) = ReadDescriptor(node);

            found.Add(new MaxwellInterface(
                Path: $"/dev/{name}",
                ProductId: product,
                UsagePage: usagePage,
                Usage: 0,
                InputReportLength: inputLength,
                OutputReportLength: outputLength,
                ProductName: ReadText(Path.Combine(node, "device", "name"))));
        }

        return found;
    }


    /// <summary>
    /// Not implemented on this platform. The class protocols are Windows-only for now,
    /// and an empty list means "found none" rather than "failed", which is the honest
    /// answer here — there is no code to find them with.
    /// </summary>
    public IReadOnlyList<HidInterface> EnumerateAll() => [];

    public IReadOnlyList<MaxwellInterface> EnumerateRaceInterfaces() =>
        Enumerate().Where(i => i.UsagePage == MaxwellInterface.RaceUsagePage)
                   .OrderBy(i => i.IsDongle ? 0 : 1)
                   .ToList();

    /// <summary>uevent carries HID_ID=bus:vendor:product with each field zero-padded hex.</summary>
    private static bool TryReadIds(string ueventPath, out ushort vendor, out ushort product)
    {
        vendor = 0;
        product = 0;

        foreach (string line in File.ReadLines(ueventPath))
        {
            if (!line.StartsWith("HID_ID=", StringComparison.Ordinal)) continue;

            string[] parts = line["HID_ID=".Length..].Split(':');
            if (parts.Length < 3) return false;

            return ushort.TryParse(parts[1][^4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendor)
                && ushort.TryParse(parts[2][^4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out product);
        }

        return false;
    }

    /// <summary>
    /// Linux gives no parsed capabilities, so the raw report descriptor is scanned for
    /// the vendor usage page (06 13 FF) to tell the RACE interface from the audio ones.
    /// Report lengths are not published either and fall back to the known 62.
    /// </summary>
    private static (ushort UsagePage, int InputLength, int OutputLength) ReadDescriptor(string node)
    {
        const int KnownReportLength = 62;

        string path = Path.Combine(node, "device", "report_descriptor");
        if (!File.Exists(path)) return (0, KnownReportLength, KnownReportLength);

        byte[] descriptor;
        try
        {
            descriptor = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return (0, KnownReportLength, KnownReportLength);
        }

        for (int i = 0; i + 2 < descriptor.Length; i++)
        {
            // 0x06 = Usage Page, two-byte payload, little-endian.
            if (descriptor[i] != 0x06) continue;

            ushort page = (ushort)(descriptor[i + 1] | (descriptor[i + 2] << 8));
            if (page == MaxwellInterface.RaceUsagePage)
                return (page, KnownReportLength, KnownReportLength);
        }

        return (0, KnownReportLength, KnownReportLength);
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch (IOException)
        {
            return "";
        }
    }
}
