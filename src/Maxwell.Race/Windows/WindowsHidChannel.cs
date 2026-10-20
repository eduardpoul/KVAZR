using System.Runtime.Versioning;

using System.Runtime.InteropServices;

using Maxwell.Race.Native;

namespace Maxwell.Race.Windows;

/// <summary>
/// A HID interface opened by path, with a request that waits for the reply.
///
/// The Maxwell transport beside this one polls with HidD_GetInputReport, which suits RACE
/// because the headset never pushes anything. HID++ is the opposite: a request goes out on
/// the interrupt OUT pipe and the answer arrives on interrupt IN, mixed in with unsolicited
/// notifications the device sends whenever it feels like it — a wheel turning, a button, a
/// connection changing. So this reads until something matching turns up, and discards the
/// rest rather than mistaking a notification for an answer.
///
/// Reads are blocking, cancelled by CancelIoEx on timeout. That pairing is not optional:
/// a blocking ReadFile left in flight makes CloseHandle hang, which is a deadlock on
/// shutdown rather than a slow exit.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidChannel : IDisposable
{
    private readonly IntPtr _handle;
    private readonly object _gate = new();

    private bool _disposed;

    public WindowsHidChannel(string path, int inputLength, int outputLength)
    {
        ArgumentNullException.ThrowIfNull(path);

        _handle = HidNative.CreateFile(
            path,
            HidNative.GenericRead | HidNative.GenericWrite,
            HidNative.FileShareReadWrite,
            IntPtr.Zero,
            HidNative.OpenExisting,
            0,
            IntPtr.Zero);

        if (_handle == HidNative.InvalidHandle) throw new IOException($"Could not open {path}.");

        InputLength = inputLength;
        OutputLength = outputLength;

        // One to fifteen, cycling. Zero is the device's own, for what it says unprompted.
        SoftwareId = (byte)((Interlocked.Increment(ref _opened) % 15) + 1);
    }

    private static int _opened;

    /// <summary>
    /// Who this channel is, in the one nibble HID++ gives a reader to sign its requests
    /// with.
    ///
    /// The lock below keeps two requests on one channel from overlapping. It cannot keep
    /// two channels apart, and two channels on one device is the normal case here — the
    /// driver opens one to look a device over while the poller opens another to read it,
    /// and every report the device sends arrives on both handles. Signed with the same
    /// nibble, their requests are indistinguishable and so are the answers.
    /// </summary>
    public byte SoftwareId { get; }

    /// <summary>Diagnostics for the probe: a write that never left is a different fault
    /// from a device that never answered, and from outside they look the same.</summary>
    public bool LastWriteFailed { get; private set; }

    public int LastWriteError { get; private set; }

    public byte[]? LastReply { get; private set; }

    public int InputLength { get; }

    public int OutputLength { get; }

    /// <summary>
    /// Sends a report and waits for the first reply the predicate accepts.
    ///
    /// Serialised, because two overlapping requests on one device would each be able to
    /// take the other's answer — the reply carries no sequence number beyond the software
    /// id, and a device that has been asked two things answers them in its own order.
    /// </summary>
    public byte[]? Request(byte[] report, Func<byte[], bool> accepts, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(accepts);

        lock (_gate)
        {
            LastWriteFailed = !HidNative.WriteFile(_handle, report, report.Length, out _, IntPtr.Zero);
            LastWriteError = LastWriteFailed ? Marshal.GetLastWin32Error() : 0;
            if (LastWriteFailed) return null;

            DateTime deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                byte[]? reply = Read(deadline - DateTime.UtcNow);

                if (reply is null) return null;

                LastReply = reply;
                if (accepts(reply)) return reply;

                // Something the device volunteered. Not an error and not the answer;
                // keep waiting for the one that was asked for.
            }

            return null;
        }
    }

    /// <summary>
    /// Sends a report and returns at once, for a question whose answer arrives somewhere
    /// else. A HID++ long register is asked with a short report and answered with a long
    /// one, and on Windows those are two collections and two paths: waiting here would be
    /// waiting on the path the answer never comes to.
    /// </summary>
    public bool Send(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            LastWriteFailed = !HidNative.WriteFile(_handle, report, report.Length, out _, IntPtr.Zero);
            LastWriteError = LastWriteFailed ? Marshal.GetLastWin32Error() : 0;

            return !LastWriteFailed;
        }
    }

    /// <summary>
    /// Listens without writing anything first. For vendor-defined interfaces nothing is
    /// known about: some dongles broadcast status unprompted, and reading is the one
    /// operation guaranteed not to change a device you do not understand.
    /// </summary>
    public byte[]? Listen(TimeSpan timeout)
    {
        lock (_gate)
        {
            return Read(timeout);
        }
    }

    private byte[]? Read(TimeSpan timeout)
    {
        byte[] buffer = new byte[InputLength];

        using var cancel = new CancellationTokenSource(timeout);

        // On its own thread so the cancellation can actually interrupt it: a blocking
        // ReadFile ignores tokens, and CancelIoEx is the only thing that stops it.
        Task<int> read = Task.Run(
            () => HidNative.ReadFile(_handle, buffer, buffer.Length, out int got, IntPtr.Zero) ? got : -1);

        try
        {
            if (!read.Wait(timeout))
            {
                HidNative.CancelIoEx(_handle, IntPtr.Zero);
                return null;
            }
        }
        catch (AggregateException)
        {
            return null;
        }

        return read.Result > 0 ? buffer : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Before the close, always. A read still in flight makes CloseHandle block.
        HidNative.CancelIoEx(_handle, IntPtr.Zero);
        HidNative.CloseHandle(_handle);
    }
}
