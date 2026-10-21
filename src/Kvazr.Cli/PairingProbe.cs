using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kvazr.Cli;

/// <summary>
/// Every Bluetooth pairing the classic API can see.
///
/// Read-only, and that is the point: "forget the device" removes a pairing through
/// <c>BluetoothRemoveDevice</c>, and the only honest way to find out which devices that
/// call can reach is to list what its own enumerator returns. Trying it on a device to
/// see whether it works means unpairing somebody's mouse to learn that it could.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PairingProbe
{
    public static int Run()
    {
        var search = new Native.SearchParams
        {
            Size = Marshal.SizeOf<Native.SearchParams>(),
            ReturnAuthenticated = 1,
            ReturnRemembered = 1,
            ReturnUnknown = 0,
            ReturnConnected = 1,
            IssueInquiry = 0,
            TimeoutMultiplier = 0,
            Radio = IntPtr.Zero,
        };

        var info = new Native.DeviceInfo { Size = Marshal.SizeOf<Native.DeviceInfo>() };

        Console.WriteLine("[pairings] What BluetoothRemoveDevice can see");
        Console.WriteLine();

        IntPtr find = Native.BluetoothFindFirstDevice(ref search, ref info);

        if (find == IntPtr.Zero)
        {
            Console.WriteLine($"  Nothing listed (Win32 {Marshal.GetLastWin32Error()}).");
            return 1;
        }

        int seen = 0;

        try
        {
            do
            {
                seen++;

                Console.WriteLine(
                    $"  {info.Address.ToString("X12", CultureInfo.InvariantCulture)}  {info.Name}");
                Console.WriteLine(
                    $"      class 0x{info.ClassOfDevice:X6}  "
                    + $"{(info.Connected != 0 ? "connected" : "not connected")}  "
                    + $"{(info.Remembered != 0 ? "remembered" : "not remembered")}  "
                    + $"{(info.Authenticated != 0 ? "paired" : "not paired")}");

                info = new Native.DeviceInfo { Size = Marshal.SizeOf<Native.DeviceInfo>() };
            }
            while (Native.BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            Native.BluetoothFindDeviceClose(find);
        }

        Console.WriteLine();
        Console.WriteLine($"{seen} device(s). A device missing from this list cannot be unpaired by that call,");
        Console.WriteLine("however correct its address is — the classic enumerator does not carry low-energy");
        Console.WriteLine("devices, and a low-energy mouse is paired through a different door entirely.");

        return 0;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SearchParams
        {
            public int Size;
            public int ReturnAuthenticated;
            public int ReturnRemembered;
            public int ReturnUnknown;
            public int ReturnConnected;
            public int IssueInquiry;
            public byte TimeoutMultiplier;
            public IntPtr Radio;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DeviceInfo
        {
            public int Size;
            public ulong Address;
            public uint ClassOfDevice;
            public int Connected;
            public int Remembered;
            public int Authenticated;
            public SystemTime LastSeen;
            public SystemTime LastUsed;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string Name;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SystemTime
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        internal static extern IntPtr BluetoothFindFirstDevice(ref SearchParams search, ref DeviceInfo info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        internal static extern bool BluetoothFindNextDevice(IntPtr find, ref DeviceInfo info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        internal static extern bool BluetoothFindDeviceClose(IntPtr find);
    }
}
