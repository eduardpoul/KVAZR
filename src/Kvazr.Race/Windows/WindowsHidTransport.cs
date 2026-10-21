using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Native;
using Kvazr.Race.Protocol;

namespace Kvazr.Race.Windows;

/// <summary>Win32 HID channel: WriteFile out, HidD_GetInputReport in.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidTransport : IHidTransport
{
    private IntPtr _handle;

    public MaxwellInterface Descriptor { get; }

    private WindowsHidTransport(IntPtr handle, MaxwellInterface iface)
    {
        _handle = handle;
        Descriptor = iface;
    }

    internal static WindowsHidTransport Open(MaxwellInterface iface)
    {
        IntPtr handle = HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
        {
            throw new IOException(
                $"Cannot open {iface.Path} for read/write (Win32 {Marshal.GetLastWin32Error()}). " +
                "Close Audeze HQ and try again.");
        }

        // A deeper queue makes bursts of replies less likely to be dropped.
        HidNative.HidD_SetNumInputBuffers(handle, 64);

        return new WindowsHidTransport(handle, iface);
    }

    public bool Write(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ObjectDisposedException.ThrowIf(_handle == HidNative.InvalidHandle, this);

        return HidNative.WriteFile(_handle, report, report.Length, out _, IntPtr.Zero);
    }

    public byte[]? ReadInputReport(byte reportId)
    {
        ObjectDisposedException.ThrowIf(_handle == HidNative.InvalidHandle, this);

        byte[] buffer = new byte[Descriptor.InputReportLength];
        buffer[0] = reportId;

        if (!HidNative.HidD_GetInputReport(_handle, buffer, buffer.Length)) return null;

        // Byte 1 is the fresh-data length; zero means the device replayed an idle
        // buffer. Callers still need those to detect staleness, so they are returned.
        return buffer;
    }

    public void Dispose()
    {
        if (_handle == HidNative.InvalidHandle || _handle == IntPtr.Zero) return;

        // Cancel anything in flight first: CloseHandle blocks behind a pending
        // synchronous read, which hangs the process rather than failing.
        HidNative.CancelIoEx(_handle, IntPtr.Zero);
        HidNative.CloseHandle(_handle);
        _handle = HidNative.InvalidHandle;
    }
}

/// <summary>Opens Windows HID transports.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidTransportFactory : IHidTransportFactory
{
    public IHidTransport Open(MaxwellInterface iface)
    {
        ArgumentNullException.ThrowIfNull(iface);
        return WindowsHidTransport.Open(iface);
    }
}
