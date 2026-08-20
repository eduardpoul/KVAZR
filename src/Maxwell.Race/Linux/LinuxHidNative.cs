using System.Runtime.InteropServices;

namespace Maxwell.Race.Linux;

/// <summary>libc entry points needed to drive a /dev/hidraw node.</summary>
internal static partial class LinuxHidNative
{
    internal const int OpenReadWrite = 0x0002;   // O_RDWR
    internal const int OpenCloseOnExec = 0x80000; // O_CLOEXEC

    /// <summary>
    /// HIDIOCGINPUT(len) — fetch an input report by id, the Linux equivalent of
    /// HidD_GetInputReport. Encoded as _IOC(_IOC_WRITE|_IOC_READ, 'H', 0x0A, len).
    /// </summary>
    internal static uint HidIocGetInput(int length) =>
        (3u << 30) | ((uint)length << 16) | ((uint)'H' << 8) | 0x0A;

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    internal static partial nint Write(int fd, byte[] buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int Ioctl(int fd, uint request, byte[] argument);
}
