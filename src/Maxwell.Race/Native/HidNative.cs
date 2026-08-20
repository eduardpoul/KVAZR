using System.Runtime.InteropServices;

namespace Maxwell.Race.Native;

/// <summary>Raw Win32 HID / SetupAPI surface. No external dependencies.</summary>
internal static class HidNative
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareReadWrite = 0x3;
    internal const uint OpenExisting = 3;
    internal const int DigcfPresent = 0x2;
    internal const int DigcfDeviceInterface = 0x10;

    internal static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] internal static extern bool HidD_GetAttributes(IntPtr handle, ref HiddAttributes attributes);
    [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsed);
    [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr preparsed);
    [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr preparsed, ref HidpCaps caps);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetValueCaps(int reportType, [Out] HidpValueCaps[] caps,
        ref ushort capsLength, IntPtr preparsed);

    internal const int HidpInput = 0;
    internal const int HidpOutput = 1;
    internal const int HidpFeature = 2;

    /// <summary>HIDP_VALUE_CAPS. 72 bytes on both architectures; every field is naturally aligned.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }
    [DllImport("hid.dll")] internal static extern bool HidD_GetInputReport(IntPtr handle, byte[] buffer, int length);
    [DllImport("hid.dll")] internal static extern bool HidD_SetOutputReport(IntPtr handle, byte[] buffer, int length);
    [DllImport("hid.dll")] internal static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int length);
    [DllImport("hid.dll")] internal static extern bool HidD_SetFeature(IntPtr handle, byte[] buffer, int length);
    [DllImport("hid.dll")] internal static extern bool HidD_SetNumInputBuffers(IntPtr handle, int count);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    internal static extern bool HidD_GetProductString(IntPtr handle, byte[] buffer, int length);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfo, IntPtr devInfoData, ref Guid guid,
        int index, ref SpDeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devInfo, ref SpDeviceInterfaceData interfaceData,
        IntPtr detail, int detailSize, out int requiredSize, IntPtr devInfoData);

    [DllImport("setupapi.dll")]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteFile(IntPtr handle, byte[] buffer, int length, out int written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(IntPtr handle, byte[] buffer, int length, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    /// <summary>Cancels pending I/O on a handle. Without it, CloseHandle blocks on an in-flight synchronous ReadFile.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);
}
