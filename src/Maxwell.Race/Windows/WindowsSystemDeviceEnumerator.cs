using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Walks the device tree and reports every peripheral node Windows has, whoever made it.
///
/// This is the floor the whole application stands on: it needs no protocol, no vendor
/// knowledge and no catalogue entry, so it finds a device the first time it is plugged
/// in rather than the first time someone teaches us about it. What it returns is thin —
/// a name, a class, a link and sometimes a battery — but it is thin for every device
/// equally, which is what makes the tail of the catalogue tractable.
///
/// Only the enumerators that carry peripherals are scanned. A full pass over the device
/// tree is thousands of nodes, nearly all of them software.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemDeviceEnumerator : ISystemDeviceEnumerator
{
    /// <summary>
    /// BTHENUM carries classic profile nodes, BTHHFENUM the Hands-Free audio node —
    /// which is the one that actually holds the battery — and BTHLEDEVICE the low-energy
    /// devices. HID and USB cover everything on a wire or a dongle.
    /// </summary>
    private static readonly (string Enumerator, SystemDeviceLink Link)[] Sources =
    [
        ("BTHENUM", SystemDeviceLink.Bluetooth),
        ("BTHHFENUM", SystemDeviceLink.Bluetooth),
        ("BTHLEDEVICE", SystemDeviceLink.BluetoothLe),
        // The device itself, as against BTHLEDEVICE's one node per GATT service. This is
        // where the friendly name and the charge live — measured: without it a Bluetooth
        // mouse arrived as "Bluetooth LE Service {0000180a-...}" with no battery, while
        // Windows' own settings showed its name and 100% beside it.
        ("BTHLE", SystemDeviceLink.BluetoothLe),
        ("HID", SystemDeviceLink.Usb),
        ("USB", SystemDeviceLink.Usb),
    ];

    /// <summary>
    /// The HID-over-GATT service, which is what makes a HID node a Bluetooth one.
    /// </summary>
    private const string HidOverGatt = "{00001812-0000-1000-8000-00805F9B34FB}";

    public IReadOnlyList<SystemDeviceNode> Enumerate()
    {
        var nodes = new List<SystemDeviceNode>();

        foreach ((string enumerator, SystemDeviceLink link) in Sources)
        {
            IntPtr set = Native.SetupDiGetClassDevs(
                IntPtr.Zero, enumerator, IntPtr.Zero, Native.DigcfAllClasses | Native.DigcfPresent);

            if (set == Native.InvalidHandle) continue;

            try
            {
                var info = new Native.SpDevInfoData { Size = Marshal.SizeOf<Native.SpDevInfoData>() };

                for (int i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref info); i++)
                {
                    if (Read(set, ref info, link) is { } node) nodes.Add(node);
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(set);
            }
        }

        return nodes;
    }

    private static SystemDeviceNode? Read(IntPtr set, ref Native.SpDevInfoData info, SystemDeviceLink link)
    {
        if (ReadString(set, ref info, Native.DevPkeyInstanceId) is not { Length: > 0 } instanceId) return null;

        // Without a container id there is nothing to group this node with, and an
        // ungrouped node would surface as a device of its own — one headset arriving as
        // four entries. Better to drop it than to invent an identity for it.
        if (ReadGuid(set, ref info, Native.DevPkeyContainerId) is not { } container) return null;
        if (container == Guid.Empty || container == Native.NotAPhysicalDevice) return null;

        // A BLE node sitting on the classic enumerators still belongs on the LE link.
        //
        // Two ways it can be one. It can be enumerated as a low-energy node outright; or
        // it can be a HID node whose instance id carries the HID-over-GATT service — which
        // is what a Bluetooth mouse is, and it enumerates under HID like anything on a
        // wire. Without the second test a Bluetooth mouse reported itself as attached by
        // USB, on a container that also held its LE service nodes, so the one device
        // claimed two kinds of lead at once.
        SystemDeviceLink actual =
            instanceId.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase)
            || instanceId.Contains(HidOverGatt, StringComparison.OrdinalIgnoreCase)
                ? SystemDeviceLink.BluetoothLe
                : link;

        (ushort? vid, ushort? pid) = ParseIds(instanceId);

        // What the device says about itself over the Device Information Service, which a
        // low-energy device fills in as a standard and Windows lays out as properties.
        //
        // Worth reaching for because the instance id does not carry it in the form the
        // parser above understands: a Bluetooth node writes VID&0203F0 — a source byte
        // and then the id — where USB writes VID_046D, so a low-energy mouse arrived with
        // no identifiers at all and could never be matched against the catalogue.
        ushort? statedVendor = ReadUInt16(set, ref info, Native.DevPkeyBluetoothVendorId);
        ushort? statedProduct = ReadUInt16(set, ref info, Native.DevPkeyBluetoothProductId);

        return new SystemDeviceNode
        {
            InstanceId = instanceId,
            ContainerId = container,
            Link = actual,
            ClassGuid = info.ClassGuid,
            IsDeviceRoot = instanceId.Contains(@"\DEV_", StringComparison.OrdinalIgnoreCase),
            Name = ReadString(set, ref info, Native.DevPkeyName),
            BusReportedName = ReadString(set, ref info, Native.DevPkeyBusReportedName),
            // The device's own maker where it states one. The Windows property beside it
            // names whoever wrote the driver, which for anything on the Bluetooth stack
            // is Microsoft — true, and not what anybody means by the manufacturer.
            Manufacturer = ReadString(set, ref info, Native.DevPkeyBluetoothManufacturer)
                ?? ReadString(set, ref info, Native.DevPkeyManufacturer),
            BatteryPercent = ReadByte(set, ref info, Native.DevPkeyBatteryLevel),
            ClassOfDevice = ReadUInt32(set, ref info, Native.DevPkeyClassOfDevice),
            Address = ReadString(set, ref info, Native.DevPkeyBluetoothAddress),
            VendorId = vid ?? statedVendor,
            ProductId = pid ?? statedProduct,
            Revision = ReadUInt16(set, ref info, Native.DevPkeyBluetoothProductVersion),
            ArrivedAt = ReadFileTime(set, ref info, Native.DevPkeyLastArrival),
            IsConnected = ReadBoolean(set, ref info, Native.DevPkeyBluetoothIsConnected),
        };
    }

    /// <summary>
    /// Pulls the USB ids out of an instance id such as
    /// <c>HID\VID_046D&amp;PID_C547&amp;MI_01\7&amp;1e2f...</c>. Bluetooth instance ids carry
    /// them too on devices that declare a PnP id, and leave them out otherwise.
    /// </summary>
    private static (ushort? Vendor, ushort? Product) ParseIds(string instanceId)
    {
        return (Find("VID_"), Find("PID_"));

        ushort? Find(string marker)
        {
            int at = instanceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;

            int start = at + marker.Length;
            if (start + 4 > instanceId.Length) return null;

            return ushort.TryParse(
                instanceId.AsSpan(start, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value)
                ? value
                : null;
        }
    }

    /// <summary>
    /// A string property, and only a string property.
    ///
    /// The type is checked rather than assumed, because a key can hold something else
    /// entirely and the bytes decode anyway. Measured on a pair of AirPods:
    /// DEVPKEY_Bluetooth_Manufacturer holds a UInt32 on this stack, not a name — 2360344,
    /// which is 0x00240418, which read as UTF-16 is two characters of punctuation and
    /// Cyrillic. That went on the page as the maker of the device.
    /// </summary>
    private static string? ReadString(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[]? buffer = ReadRaw(set, ref info, key, out ulong type);
        if (buffer is null || type != Native.DevpropTypeString) return null;

        string value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return value.Length == 0 ? null : value;
    }

    private static Guid? ReadGuid(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[]? buffer = ReadRaw(set, ref info, key);
        return buffer is { Length: 16 } ? new Guid(buffer) : null;
    }

    private static ushort? ReadUInt16(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[]? raw = ReadRaw(set, ref info, key);

        return raw is { Length: >= 2 } ? BitConverter.ToUInt16(raw) : null;
    }

    /// <summary>
    /// A DEVPROP_TYPE_BOOLEAN, which is one byte: 0xFF for true and 0 for false.
    ///
    /// Null where the node does not carry the key at all, and the difference matters —
    /// on a Bluetooth device most nodes have no opinion about being connected and saying
    /// "false" for them would be inventing an answer.
    /// </summary>
    private static bool? ReadBoolean(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[]? buffer = ReadRaw(set, ref info, key);
        return buffer is { Length: >= 1 } ? buffer[0] != 0 : null;
    }

    private static uint? ReadUInt32(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[]? buffer = ReadRaw(set, ref info, key);
        return buffer is { Length: >= 4 } ? BitConverter.ToUInt32(buffer, 0) : null;
    }

    private static DateTimeOffset? ReadFileTime(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[]? buffer = ReadRaw(set, ref info, key);
        if (buffer is not { Length: >= 8 }) return null;

        long ticks = BitConverter.ToInt64(buffer, 0);
        if (ticks <= 0) return null;

        try
        {
            return DateTimeOffset.FromFileTime(ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int? ReadByte(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key)
    {
        byte[] buffer = new byte[1];

        return Native.SetupDiGetDeviceProperty(set, ref info, ref key, out _, buffer, 1, out _, 0)
            ? buffer[0]
            : null;
    }

    private static byte[]? ReadRaw(IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key) =>
        ReadRaw(set, ref info, key, out _);

    /// <summary>The bytes and what Windows says they are, for readers that care.</summary>
    private static byte[]? ReadRaw(
        IntPtr set, ref Native.SpDevInfoData info, Native.DevPropKey key, out ulong type)
    {
        Native.SetupDiGetDeviceProperty(set, ref info, ref key, out type, null, 0, out int needed, 0);
        if (needed <= 0) return null;

        byte[] buffer = new byte[needed];

        return Native.SetupDiGetDeviceProperty(set, ref info, ref key, out type, buffer, needed, out _, 0)
            ? buffer
            : null;
    }

    private static class Native
    {
        /// <summary>DEVPROP_TYPE_STRING.</summary>
        internal const ulong DevpropTypeString = 0x12;

        internal const int DigcfPresent = 0x2;
        internal const int DigcfAllClasses = 0x4;

        internal static readonly IntPtr InvalidHandle = new(-1);

        /// <summary>
        /// The container id Windows stamps on nodes that are not devices in their own
        /// right — hubs, host controllers, the Bluetooth radio itself. Windows saying so
        /// outright beats any list of names that look like plumbing: an ASMedia hub was
        /// passing a peripheral-class filter because it exposes a HID interface for port
        /// control, and no naming rule would have caught that.
        /// </summary>
        internal static readonly Guid NotAPhysicalDevice = new("00000000-0000-0000-ffff-ffffffffffff");

        /// <summary>DEVPKEY_NAME.</summary>
        internal static readonly DevPropKey DevPkeyName =
            new(new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), 10);

        /// <summary>DEVPKEY_Device_InstanceId.</summary>
        internal static readonly DevPropKey DevPkeyInstanceId =
            new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

        /// <summary>DEVPKEY_Device_ContainerId — the same value on every node of one device.</summary>
        internal static readonly DevPropKey DevPkeyContainerId =
            new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

        /// <summary>DEVPKEY_Device_Manufacturer.</summary>
        internal static readonly DevPropKey DevPkeyManufacturer =
            new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 13);

        /// <summary>
        /// DEVPKEY_Device_BusReportedDeviceDesc — the USB iProduct string or the
        /// Bluetooth friendly name, which is the only place the product name appears.
        /// </summary>
        internal static readonly DevPropKey DevPkeyBusReportedName =
            new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);

        /// <summary>DEVPKEY_Device_LastArrivalDate.</summary>
        internal static readonly DevPropKey DevPkeyLastArrival =
            new(new Guid("80497100-8c73-48b9-aad9-ce387e19c56e"), 102);

        /// <summary>
        /// The Device Information Service, as Windows lays it out. Four keys under one
        /// formatter: the maker's name, the two identifiers and the revision.
        /// </summary>
        // Declared before the keys that use it, and that is load-bearing: static field
        // initialisers run in source order, so a formatter written below the keys leaves
        // every one of them addressing the empty guid. Which fails silently — the reads
        // return nothing, exactly as they do for a device that has nothing to say.
        private static readonly Guid BluetoothDeviceInfo =
            new("2bd67d8b-8beb-48d5-87e0-6cda3428040a");

        /// <summary>
        /// DEVPKEY_Bluetooth_Manufacturer, which on this stack is not a name.
        ///
        /// Measured: the key answers with a UInt32 rather than a string — the same
        /// 0x240418 the class-of-device key carries. The company is in a different key
        /// and as a number: sub 7 held 76, which is Apple, beside sub 8 holding 0x2013,
        /// the product id. Naming a maker out of a number needs a table of company ids,
        /// which is a separate thing to build; until then the read refuses, and the
        /// Windows manufacturer beside it says "Microsoft" for the whole Bluetooth stack
        /// and is filtered out as the nothing it is.
        /// </summary>
        internal static readonly DevPropKey DevPkeyBluetoothManufacturer = new(BluetoothDeviceInfo, 4);

        internal static readonly DevPropKey DevPkeyBluetoothVendorId = new(BluetoothDeviceInfo, 7);

        internal static readonly DevPropKey DevPkeyBluetoothProductId = new(BluetoothDeviceInfo, 8);

        internal static readonly DevPropKey DevPkeyBluetoothProductVersion = new(BluetoothDeviceInfo, 9);

        /// <summary>DEVPKEY_Bluetooth_DeviceAddress.</summary>
        internal static readonly DevPropKey DevPkeyBluetoothAddress =
            new(new Guid("2bd67d8b-8beb-48d5-87e0-6cda3428040a"), 1);

        /// <summary>DEVPKEY_Bluetooth_ClassOfDevice.</summary>
        internal static readonly DevPropKey DevPkeyClassOfDevice =
            new(new Guid("2bd67d8b-8beb-48d5-87e0-6cda3428040a"), 10);

        /// <summary>DEVPKEY_Bluetooth_Battery — a single byte, 0..100.</summary>
        internal static readonly DevPropKey DevPkeyBatteryLevel =
            new(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);

        /// <summary>
        /// DEVPKEY_Bluetooth_IsConnected — paired is not connected, and this is the
        /// difference.
        ///
        /// Measured on a pair of AirPods, both ways. In the case: eighteen Bluetooth
        /// nodes on this machine and every one of them false. Taken out and connected:
        /// exactly one turns true — <c>BTHENUM\DEV_&lt;address&gt;</c>, the node named
        /// after the device itself. Every service node beside it — Avrcp Transport,
        /// Hands-Free AG, the vendor's own — stays false while the thing is playing
        /// music. So the question to ask of a device is whether ANY of its nodes says
        /// true, never whether all of them do.
        /// </summary>
        internal static readonly DevPropKey DevPkeyBluetoothIsConnected =
            new(new Guid("83da6326-97a6-4088-9453-a1923f573b29"), 15);

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
