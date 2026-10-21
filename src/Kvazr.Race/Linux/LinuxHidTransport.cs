using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kvazr.Race.Abstractions;

namespace Kvazr.Race.Linux;

/// <summary>
/// A /dev/hidraw channel. Unlike Windows, Linux exposes one node for the whole HID
/// interface rather than one per collection, so the report id in the packet — 0x06 out,
/// 0x07 in — is what selects the vendor collection.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHidTransport : IHidTransport
{
    private int _fd = -1;

    public MaxwellInterface Descriptor { get; }

    private LinuxHidTransport(int fd, MaxwellInterface descriptor)
    {
        _fd = fd;
        Descriptor = descriptor;
    }

    internal static LinuxHidTransport Open(MaxwellInterface descriptor)
    {
        int fd = LinuxHidNative.Open(descriptor.Path,
            LinuxHidNative.OpenReadWrite | LinuxHidNative.OpenCloseOnExec);

        if (fd < 0)
        {
            int error = Marshal.GetLastPInvokeError();

            // EACCES is the usual first-run failure: hidraw nodes are root-only until a
            // udev rule grants the user access, and without that hint the app just
            // looks broken.
            string hint = error == 13
                ? " Install the udev rule for vendor 3329 and reconnect the headset."
                : "";

            throw new IOException(string.Create(CultureInfo.InvariantCulture,
                $"Cannot open {descriptor.Path} (errno {error}).{hint}"));
        }

        return new LinuxHidTransport(fd, descriptor);
    }

    public bool Write(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ObjectDisposedException.ThrowIf(_fd < 0, this);

        nint written = LinuxHidNative.Write(_fd, report, (nuint)report.Length);
        return written == report.Length;
    }

    public byte[]? ReadInputReport(byte reportId)
    {
        ObjectDisposedException.ThrowIf(_fd < 0, this);

        byte[] buffer = new byte[Descriptor.InputReportLength];
        buffer[0] = reportId;

        // The ioctl both takes the requested id in byte 0 and returns the report there.
        int result = LinuxHidNative.Ioctl(_fd, LinuxHidNative.HidIocGetInput(buffer.Length), buffer);
        return result < 0 ? null : buffer;
    }

    public void Dispose()
    {
        if (_fd < 0) return;

        LinuxHidNative.Close(_fd);
        _fd = -1;
    }
}

/// <summary>Opens Linux hidraw transports.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHidTransportFactory : IHidTransportFactory
{
    public IHidTransport Open(MaxwellInterface iface)
    {
        ArgumentNullException.ThrowIfNull(iface);
        return LinuxHidTransport.Open(iface);
    }
}
