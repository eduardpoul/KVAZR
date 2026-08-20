using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Native;

namespace Maxwell.Race;

/// <summary>Which physical link a Maxwell HID interface represents.</summary>
public enum MaxwellTransport
{
    Unknown,
    XboxDongle,
    PlayStationDongle,
    PlayStationUsbC,
    XboxUsbC,
}

/// <summary>One enumerated Maxwell HID interface.</summary>
public sealed record MaxwellInterface(
    string Path,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    string ProductName)
{
    public MaxwellTransport Transport => ProductId switch
    {
        0x4B18 => MaxwellTransport.XboxDongle,
        0x4B19 => MaxwellTransport.PlayStationDongle,
        0x4B1A => MaxwellTransport.PlayStationUsbC,
        0x4B1E => MaxwellTransport.XboxUsbC,
        _ => MaxwellTransport.Unknown,
    };

    public bool IsDongle => ProductId is 0x4B18 or 0x4B19;

    /// <summary>The vendor-defined RACE collection — usage page 0xFF13, enumerated as col02.</summary>
    public bool IsRaceInterface =>
        UsagePage == MaxwellHid.RaceUsagePage ||
        Path.Contains("col02", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Discovery and opening of Maxwell HID interfaces.</summary>
[SupportedOSPlatform("windows")]
public static class MaxwellHid
{
    public const ushort VendorId = 0x3329;
    public const ushort RaceUsagePage = 0xFF13;

    public static readonly ushort[] KnownProductIds = { 0x4B18, 0x4B19, 0x4B1A, 0x4B1E };

    /// <summary>Every HID interface path on the system.</summary>
    private static IEnumerable<string> EnumerateHidPaths()
    {
        HidNative.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DigcfPresent | HidNative.DigcfDeviceInterface);
        if (set == HidNative.InvalidHandle) yield break;

        try
        {
            var data = new HidNative.SpDeviceInterfaceData
            {
                CbSize = Marshal.SizeOf<HidNative.SpDeviceInterfaceData>(),
            };

            for (int i = 0; HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
            {
                HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                if (needed <= 0) continue;

                IntPtr buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, needed, out _, IntPtr.Zero))
                    {
                        string? path = Marshal.PtrToStringUni(buffer + 4);
                        if (!string.IsNullOrEmpty(path)) yield return path;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { HidNative.SetupDiDestroyDeviceInfoList(set); }
    }

    /// <summary>Every present Maxwell HID interface, including the audio and consumer-control collections.</summary>
    public static List<MaxwellInterface> Enumerate()
    {
        var found = new List<MaxwellInterface>();

        foreach (string path in EnumerateHidPaths())
        {
            // Query-only access: never blocked by another process holding the device.
            IntPtr handle = HidNative.CreateFile(path, 0, HidNative.FileShareReadWrite,
                IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
            if (handle == HidNative.InvalidHandle) continue;

            try
            {
                var attributes = new HidNative.HiddAttributes { Size = Marshal.SizeOf<HidNative.HiddAttributes>() };
                if (!HidNative.HidD_GetAttributes(handle, ref attributes)) continue;
                if (attributes.VendorId != VendorId) continue;
                if (Array.IndexOf(KnownProductIds, attributes.ProductId) < 0) continue;

                ushort usagePage = 0, usage = 0;
                int inputLength = 0, outputLength = 0;
                if (HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed))
                {
                    var caps = new HidNative.HidpCaps();
                    HidNative.HidP_GetCaps(preparsed, ref caps);
                    HidNative.HidD_FreePreparsedData(preparsed);
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inputLength = caps.InputReportByteLength;
                    outputLength = caps.OutputReportByteLength;
                }

                found.Add(new MaxwellInterface(
                    path,
                    attributes.ProductId,
                    usagePage,
                    usage,
                    inputLength > 0 ? inputLength : 63,
                    outputLength > 0 ? outputLength : 63,
                    ReadProductString(handle)));
            }
            finally { HidNative.CloseHandle(handle); }
        }

        return found;
    }

    private static string ReadProductString(IntPtr handle)
    {
        byte[] buffer = new byte[254];
        if (!HidNative.HidD_GetProductString(handle, buffer, buffer.Length)) return "";
        string s = System.Text.Encoding.Unicode.GetString(buffer);
        int nul = s.IndexOf('\0');
        return (nul >= 0 ? s[..nul] : s).Trim();
    }

    /// <summary>The RACE-capable interfaces only, dongles listed before cabled headsets.</summary>
    public static List<MaxwellInterface> EnumerateRaceInterfaces() =>
        Enumerate().Where(i => i.IsRaceInterface).OrderBy(i => i.IsDongle ? 0 : 1).ToList();
}
