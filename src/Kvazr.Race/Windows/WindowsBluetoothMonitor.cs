using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;

namespace Kvazr.Race.Windows;

/// <summary>
/// Finds the headset among the Bluetooth device nodes and reads the battery level
/// Windows keeps on them.
///
/// The level arrives over Hands-Free, which is why it hangs off the "Hands-Free AG"
/// node rather than the one named plainly after the headset — every node is scanned
/// and the first that reports a level wins.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBluetoothMonitor : IBluetoothMonitor
{
    /// <summary>
    /// Bluetooth device nodes live under these two enumerators: BTHENUM for the
    /// classic profile nodes, BTHHFENUM for the Hands-Free audio node.
    /// </summary>
    private static readonly string[] Enumerators = ["BTHENUM", "BTHHFENUM"];

    /// <summary>
    /// Matched on the product name the headset itself reports.
    ///
    /// Not ideal — a name is a weaker key than an id — but the Bluetooth nodes carry no
    /// USB vendor id to match on, and the vendor-assigned ids in the instance path are
    /// specific to this one model. A name at least keeps working across models.
    /// </summary>
    private const string ProductMarker = "Maxwell";

    public BluetoothStatus Query()
    {
        bool present = false;
        int battery = -1;
        string? address = null;

        foreach (string enumerator in Enumerators)
        {
            IntPtr set = Native.SetupDiGetClassDevs(
                IntPtr.Zero, enumerator, IntPtr.Zero, Native.DigcfAllClasses | Native.DigcfPresent);

            if (set == Native.InvalidHandle) continue;

            try
            {
                var info = new Native.SpDevInfoData { Size = Marshal.SizeOf<Native.SpDevInfoData>() };

                for (int i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref info); i++)
                {
                    if (ReadString(set, ref info, Native.DevPkeyName) is not { } name) continue;
                    if (!name.Contains(ProductMarker, StringComparison.OrdinalIgnoreCase)) continue;

                    present = true;

                    if (battery < 0 && ReadByte(set, ref info, Native.DevPkeyBatteryLevel) is { } level)
                        battery = level;

                    address ??= ReadString(set, ref info, Native.DevPkeyBluetoothAddress);
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }
        }

        return present ? new BluetoothStatus(true, battery, address) : BluetoothStatus.Absent;
    }

    private static string? ReadString(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        Native.SetupDiGetDeviceProperty(set, ref info, ref key, out _, null, 0, out int needed, 0);
        if (needed <= 0) return null;

        byte[] buffer = new byte[needed];
        if (!Native.SetupDiGetDeviceProperty(set, ref info, ref key, out _, buffer, needed, out _, 0))
            return null;

        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private static byte? ReadByte(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[] buffer = new byte[1];

        return Native.SetupDiGetDeviceProperty(set, ref info, ref key, out _, buffer, 1, out _, 0)
            ? buffer[0]
            : null;
    }

    private static class Native
    {
        internal const int DigcfPresent = 0x2;
        internal const int DigcfAllClasses = 0x4;

        internal static readonly IntPtr InvalidHandle = new(-1);

        /// <summary>DEVPKEY_NAME.</summary>
        internal static readonly DevPropKey DevPkeyName =
            new(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10);

        /// <summary>DEVPKEY_Bluetooth_Battery — a single byte, 0..100.</summary>
        internal static readonly DevPropKey DevPkeyBatteryLevel =
            new(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);

        /// <summary>DEVPKEY_Bluetooth_DeviceAddress.</summary>
        internal static readonly DevPropKey DevPkeyBluetoothAddress =
            new(new Guid("2bd67d8b-8beb-48d5-87e0-6cda3428040a"), 1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDevInfoData
        {
            public int Size;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DevPropKey(Guid formatId, uint propertyId)
        {
            public Guid FormatId = formatId;
            public uint PropertyId = propertyId;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
            IntPtr classGuid, string? enumerator, IntPtr hwnd, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(IntPtr devInfo, int index, ref SpDevInfoData info);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceProperty(
            IntPtr devInfo, ref SpDevInfoData info, ref DevPropKey key,
            out ulong propertyType, byte[]? buffer, int bufferSize, out int requiredSize, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);
    }
}
