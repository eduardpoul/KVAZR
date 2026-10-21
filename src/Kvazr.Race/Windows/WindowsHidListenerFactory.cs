using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Native;

namespace Kvazr.Race.Windows;

/// <summary>
/// Opens an ear on the first collection of a given vendor and usage page.
///
/// The first, not all of them: two identical microphones on one desk would need the
/// container id to tell their channels apart, and nothing here has one to hand. That is a
/// real limitation and it is written down rather than guarded against, because the guard
/// would be a guess at which of the two the caller meant.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidListenerFactory : IHidListenerFactory
{
    public IHidListener? Open(ushort vendorId, ushort usagePage)
    {
        HidInterface? found = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == vendorId && i.UsagePage == usagePage && i.InputReportLength > 1);

        return found is { } collection
            ? new WindowsHidListener(collection)
            : null;
    }

    public IHidVendorPipe? OpenPipe(ushort vendorId, ushort usagePage)
    {
        HidInterface? found = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == vendorId && i.UsagePage == usagePage && i.FeatureReportLength > 0);

        if (found is not { } collection) return null;

        IntPtr handle = HidNative.CreateFile(
            collection.Path, Read | Write, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        return handle == HidNative.InvalidHandle
            ? null
            : new WindowsVendorPipe(handle, collection.FeatureReportLength);
    }

    /// <summary>GENERIC_READ | GENERIC_WRITE. A question needs both halves.</summary>
    private const uint Read = 0x80000000;

    private const uint Write = 0x40000000;

    /// <summary>
    /// A vendor register asked and answered through feature reports.
    ///
    /// Measured on a QuadCast: the answer lands about four milliseconds after the command
    /// and the register returns to its idle value within twenty. So the wait is short and
    /// the polling inside it is tight; a question that has not been answered in a fifth of
    /// a second was not going to be.
    /// </summary>
    private sealed class WindowsVendorPipe(IntPtr handle, int length) : IHidVendorPipe
    {
        private readonly object _gate = new();

        private IntPtr _handle = handle;

        public byte[]? Ask(byte[] command, TimeSpan within)
        {
            ArgumentNullException.ThrowIfNull(command);

            lock (_gate)
            {
                if (_handle == HidNative.InvalidHandle) return null;

                byte[] written = new byte[length];
                command.CopyTo(written, 1);

                if (!HidNative.HidD_SetFeature(_handle, written, written.Length)) return null;

                var clock = System.Diagnostics.Stopwatch.StartNew();

                while (clock.Elapsed < within)
                {
                    byte[] answer = new byte[length];

                    if (HidNative.HidD_GetFeature(_handle, answer, answer.Length)
                        && !answer.AsSpan().SequenceEqual(written))
                    {
                        return answer;
                    }

                    Thread.Sleep(2);
                }

                return null;
            }
        }

        public bool Send(byte[] command)
        {
            ArgumentNullException.ThrowIfNull(command);

            lock (_gate)
            {
                if (_handle == HidNative.InvalidHandle) return false;

                byte[] written = new byte[length];
                command.CopyTo(written, 1);

                return HidNative.HidD_SetFeature(_handle, written, written.Length);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_handle == HidNative.InvalidHandle) return;

                HidNative.CloseHandle(_handle);
                _handle = HidNative.InvalidHandle;
            }
        }
    }

    private sealed class WindowsHidListener : IHidListener
    {
        private readonly WindowsHidChannel _channel;

        public WindowsHidListener(HidInterface collection) =>
            _channel = new WindowsHidChannel(
                collection.Path, collection.InputReportLength, collection.OutputReportLength);

        public byte[]? Listen(TimeSpan wait) => _channel.Listen(wait);

        public void Dispose() => _channel.Dispose();
    }
}
