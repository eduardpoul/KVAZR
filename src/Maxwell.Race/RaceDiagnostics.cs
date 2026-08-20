using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Native;

namespace Maxwell.Race;

/// <summary>
/// Low-level probe that reports the outcome of every Win32 call rather than
/// swallowing failures, so a silent device can be told apart from a failing API.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RaceDiagnostics
{
    public static void Probe(MaxwellInterface iface, TextWriter output, byte[] body, int pollMs = 1500)
    {
        output.WriteLine($"    path      : {iface.Path}");
        output.WriteLine($"    caps      : in {iface.InputReportLength}  out {iface.OutputReportLength}");

        IntPtr handle = HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
        {
            output.WriteLine($"    CreateFile: FAILED, Win32 {Marshal.GetLastWin32Error()}");
            return;
        }

        output.WriteLine("    CreateFile: ok (read/write)");

        try
        {
            byte[] report = new byte[iface.OutputReportLength];
            report[0] = 0x06;
            report[1] = (byte)body.Length;
            Array.Copy(body, 0, report, 2, Math.Min(body.Length, report.Length - 2));

            output.WriteLine($"    request   : {Convert.ToHexString(report[..Math.Min(16, report.Length)])}...");

            bool written = HidNative.WriteFile(handle, report, report.Length, out int bytes, IntPtr.Zero);
            output.WriteLine(written
                ? $"    WriteFile : ok, {bytes} bytes"
                : $"    WriteFile : FAILED, Win32 {Marshal.GetLastWin32Error()}");

            int calls = 0, ok = 0, nonEmpty = 0, lastError = 0;
            var seen = new List<string>();

            long deadline = Environment.TickCount64 + pollMs;
            while (Environment.TickCount64 < deadline)
            {
                byte[] buffer = new byte[iface.InputReportLength];
                buffer[0] = 0x07;
                calls++;

                if (HidNative.HidD_GetInputReport(handle, buffer, buffer.Length))
                {
                    ok++;
                    int length = buffer[1] | (buffer[2] << 8);
                    if (length > 0) nonEmpty++;

                    string hex = Convert.ToHexString(buffer[..Math.Min(24, buffer.Length)]);
                    if (!seen.Contains(hex)) seen.Add(hex);
                }
                else
                {
                    lastError = Marshal.GetLastWin32Error();
                }

                Thread.Sleep(10);
            }

            output.WriteLine($"    GetInput  : {calls} calls, {ok} succeeded, {nonEmpty} with payload" +
                             (lastError != 0 ? $", last Win32 {lastError}" : ""));

            if (seen.Count == 0)
            {
                output.WriteLine("    distinct  : (nothing readable)");
            }
            else
            {
                output.WriteLine($"    distinct  : {seen.Count} unique report prefixes");
                foreach (string hex in seen.Take(8)) output.WriteLine($"        {hex}");
            }
        }
        finally { HidNative.CloseHandle(handle); }
    }

    /// <summary>
    /// Cross-check the other read path: send the request, then wait on a blocking
    /// interrupt ReadFile instead of polling. Confirms whether replies arrive on the
    /// interrupt endpoint rather than the input report.
    /// </summary>
    public static void ProbeInterrupt(MaxwellInterface iface, TextWriter output, byte[] body, int waitMs = 2000)
    {
        IntPtr handle = HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
        {
            output.WriteLine($"    ReadFile  : CreateFile FAILED, Win32 {Marshal.GetLastWin32Error()}");
            return;
        }

        byte[]? received = null;
        int readError = 0;

        // Blocking ReadFile on a background thread: it is abandoned rather than
        // cancelled, which is fine for a probe that runs just before process exit.
        var reader = new Thread(() =>
        {
            byte[] buffer = new byte[iface.InputReportLength];
            if (HidNative.ReadFile(handle, buffer, buffer.Length, out int read, IntPtr.Zero) && read > 0)
                received = buffer;
            else
                readError = Marshal.GetLastWin32Error();
        })
        { IsBackground = true };

        reader.Start();
        Thread.Sleep(50);

        byte[] report = new byte[iface.OutputReportLength];
        report[0] = 0x06;
        report[1] = (byte)body.Length;
        Array.Copy(body, 0, report, 2, Math.Min(body.Length, report.Length - 2));
        HidNative.WriteFile(handle, report, report.Length, out _, IntPtr.Zero);

        reader.Join(waitMs);

        if (received is not null)
            output.WriteLine($"    ReadFile  : GOT {Convert.ToHexString(received[..Math.Min(24, received.Length)])}");
        else if (readError != 0)
            output.WriteLine($"    ReadFile  : failed, Win32 {readError}");
        else
            output.WriteLine($"    ReadFile  : nothing on the interrupt endpoint in {waitMs} ms");
    }
}
