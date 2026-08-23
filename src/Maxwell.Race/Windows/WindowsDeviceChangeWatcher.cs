using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Device arrival and removal, straight from the configuration manager.
///
/// <c>CM_Register_Notification</c> rather than the older <c>WM_DEVICECHANGE</c>, because
/// that one needs a window to deliver messages to and this component has none — it runs
/// below the interface and must work in a headless harness as well as under a shell.
/// Registered for every interface class rather than for HID alone: a dongle, a Bluetooth
/// radio and an audio endpoint all count as "the set of devices changed", and deciding
/// what actually changed is discovery's job, not this one's.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDeviceChangeWatcher : IDeviceChangeWatcher
{
    private const int CrSuccess = 0;
    private const int FilterTypeDeviceInterface = 0;
    private const int FlagAllInterfaceClasses = 0x00000001;

    /// <summary>
    /// Held in a field for the lifetime of the registration. A delegate handed to native
    /// code is not rooted by that code: collect it and the next notification calls into
    /// freed memory, which is a crash in someone else's stack trace weeks later.
    /// </summary>
    private readonly Callback _callback;

    private IntPtr _handle;
    private bool _disposed;

    public WindowsDeviceChangeWatcher() => _callback = OnNotify;

    public event EventHandler? Changed;

    public void Start()
    {
        if (_disposed || _handle != IntPtr.Zero) return;

        var filter = new NotifyFilter
        {
            Size = (uint)Marshal.SizeOf<NotifyFilter>(),
            Flags = FlagAllInterfaceClasses,
            FilterType = FilterTypeDeviceInterface,
        };

        // A failure here is not fatal and must not be: without notifications the slow
        // discovery sweep still finds everything, just later. Losing the whole
        // application because a registration was refused would be the worse trade — but
        // the answer is kept, because "refused" and "nothing has happened yet" look
        // identical from the outside and only one of them is a fault.
        Result = CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out _handle);
    }

    /// <summary>The configuration manager's answer to the registration. Zero is success.</summary>
    public int Result { get; private set; } = -1;

    /// <summary>True when the registration stands, so notifications can actually arrive.</summary>
    public bool IsWatching => _handle != IntPtr.Zero && Result == CrSuccess;

    private int OnNotify(IntPtr notify, IntPtr context, int action, IntPtr data, uint size)
    {
        // Arrival and removal only. The configuration manager also reports query-remove
        // and its cancellation, which are questions about a device rather than news
        // about one, and answering them with a full rescan would rescan twice per
        // unplug.
        if (action is DeviceInterfaceArrival or DeviceInterfaceRemoval)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return CrSuccess;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle == IntPtr.Zero) return;

        _ = CM_Unregister_Notification(_handle);
        _handle = IntPtr.Zero;

        GC.KeepAlive(_callback);
    }

    private const int DeviceInterfaceArrival = 0;
    private const int DeviceInterfaceRemoval = 1;

    private delegate int Callback(IntPtr notify, IntPtr context, int action, IntPtr data, uint size);

    /// <summary>
    /// The native filter. Its union is dominated by a device instance id of 200 wide
    /// characters, so the whole structure is padded out to that: passing a shorter one
    /// makes the call fail on a size check.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyFilter
    {
        public uint Size;
        public int Flags;
        public int FilterType;
        public uint Reserved;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 400)]
        public byte[] Union;

        public NotifyFilter() => Union = new byte[400];
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Register_Notification(
        ref NotifyFilter filter, IntPtr context, Callback callback, out IntPtr notifyContext);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Unregister_Notification(IntPtr notifyContext);
}
