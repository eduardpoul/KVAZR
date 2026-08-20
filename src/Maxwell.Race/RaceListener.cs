using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Native;

namespace Maxwell.Race;

/// <summary>
/// Passive listener. Sends nothing and reports everything the device emits, on
/// both the interrupt endpoint and the polled input reports (0x03, 0x04, 0x07).
/// Distinguishes "device is silent" from "device replies on a path we are not reading".
/// </summary>
[SupportedOSPlatform("windows")]
public static class RaceListener
{
    private static readonly byte[] InputReportIds = { 0x07, 0x03, 0x04 };

    public static void Listen(MaxwellInterface iface, TextWriter output, int seconds)
    {
        IntPtr handle = HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
        {
            output.WriteLine($"    CreateFile failed, Win32 {Marshal.GetLastWin32Error()}");
            return;
        }

        HidNative.HidD_SetNumInputBuffers(handle, 64);

        // A second handle for the interrupt reader. Sharing one handle serialises
        // the control transfers behind the blocking read and stalls both paths.
        IntPtr readHandle = HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        var seen = new HashSet<string>();
        int interruptCount = 0;
        bool running = true;

        // Interrupt endpoint: this is where an unsolicited notification would arrive.
        var reader = new Thread(() =>
        {
            if (readHandle == HidNative.InvalidHandle) return;
            while (Volatile.Read(ref running))
            {
                byte[] buffer = new byte[iface.InputReportLength];
                if (!HidNative.ReadFile(readHandle, buffer, buffer.Length, out int read, IntPtr.Zero) || read <= 0)
                    return;

                Interlocked.Increment(ref interruptCount);
                string hex = Convert.ToHexString(buffer[..Math.Min(28, buffer.Length)]);
                lock (seen)
                {
                    if (seen.Add("INT " + hex))
                        output.WriteLine($"    [interrupt] {hex}");
                }
            }
        })
        { IsBackground = true };

        reader.Start();

        // 0x03 and 0x04 are declared but undocumented. The device stalls the control
        // transfer for anything it does not implement, so they get one shot each and
        // are then left alone rather than polled in the loop.
        foreach (byte id in InputReportIds.Where(i => i != 0x07))
        {
            byte[] probe = new byte[iface.InputReportLength];
            probe[0] = id;
            long started = Environment.TickCount64;
            bool ok = HidNative.HidD_GetInputReport(handle, probe, probe.Length);
            long took = Environment.TickCount64 - started;
            output.WriteLine(ok && !IsEmpty(probe)
                ? $"    [report {id:X2}]  {Convert.ToHexString(probe[..Math.Min(28, probe.Length)])} ({took} ms)"
                : $"    [report {id:X2}]  {(ok ? "empty" : "not supported")} ({took} ms)");
        }

        long deadline = Environment.TickCount64 + seconds * 1000;
        while (Environment.TickCount64 < deadline)
        {
            byte[] buffer = new byte[iface.InputReportLength];
            buffer[0] = 0x07;
            if (HidNative.HidD_GetInputReport(handle, buffer, buffer.Length) && !IsEmpty(buffer))
            {
                string hex = Convert.ToHexString(buffer[..Math.Min(28, buffer.Length)]);
                lock (seen)
                {
                    if (seen.Add($"07 {hex}"))
                        output.WriteLine($"    [report 07]  {hex}");
                }
            }
            Thread.Sleep(50);
        }

        Volatile.Write(ref running, false);

        // The reader is parked in a synchronous ReadFile; CloseHandle would block
        // behind it, so cancel the pending I/O first.
        HidNative.CancelIoEx(readHandle, IntPtr.Zero);
        reader.Join(500);

        output.WriteLine($"    {interruptCount} interrupt report(s), {seen.Count} distinct payload(s) in {seconds}s");
        if (seen.Count == 0) output.WriteLine("    device emitted nothing at all.");

        if (readHandle != HidNative.InvalidHandle) HidNative.CloseHandle(readHandle);
        HidNative.CloseHandle(handle);
    }

    /// <summary>Empty means everything past the report ID is zero.</summary>
    private static bool IsEmpty(byte[] report)
    {
        for (int i = 1; i < report.Length; i++)
            if (report[i] != 0) return false;
        return true;
    }
}
