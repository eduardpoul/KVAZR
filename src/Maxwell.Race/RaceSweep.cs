using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Native;

namespace Maxwell.Race;

/// <summary>How a request report is pushed to the device.</summary>
public enum WriteMethod
{
    /// <summary>Interrupt OUT endpoint.</summary>
    WriteFile,

    /// <summary>SET_REPORT control transfer.</summary>
    SetOutputReport,

    /// <summary>SET_FEATURE control transfer.</summary>
    SetFeature,
}

/// <summary>
/// Brute-force sweep over every plausible transport combination, so the working
/// one is found in a single run instead of by trial and error.
/// A report is "interesting" when anything past the two length bytes is non-zero.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RaceSweep
{
    public static readonly byte[] BatteryBody = { 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C };
    public static readonly byte[] RightGainBody = { 0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x09, 0x2A };

    public static void Run(MaxwellInterface iface, TextWriter output)
    {
        output.WriteLine($"  interface : {iface.Transport} PID 0x{iface.ProductId:X4}  in {iface.InputReportLength} out {iface.OutputReportLength}");
        output.WriteLine();

        var commands = new (string Name, byte[] Body)[]
        {
            ("battery  ", BatteryBody),
            ("rightGain", RightGainBody),
        };

        int hits = 0;

        foreach (WriteMethod method in Enum.GetValues<WriteMethod>())
        {
            foreach ((string name, byte[] body) in commands)
            {
                hits += ProbeCombination(iface, output, method, name, body);
            }
        }

        output.WriteLine();
        output.WriteLine("  interrupt endpoint (blocking ReadFile after WriteFile):");
        ProbeInterrupt(iface, output, BatteryBody);

        output.WriteLine();
        output.WriteLine(hits > 0
            ? $"  => {hits} combination(s) produced a non-idle reply."
            : "  => every combination returned an idle report.");
    }

    private static int ProbeCombination(MaxwellInterface iface, TextWriter output,
        WriteMethod method, string commandName, byte[] body)
    {
        IntPtr handle = Open(iface);
        if (handle == HidNative.InvalidHandle)
        {
            output.WriteLine($"  {method,-16} {commandName}  CreateFile failed, Win32 {Marshal.GetLastWin32Error()}");
            return 0;
        }

        try
        {
            HidNative.HidD_SetNumInputBuffers(handle, 64);

            byte[] report = new byte[iface.OutputReportLength];
            report[0] = 0x06;
            report[1] = (byte)body.Length;
            Array.Copy(body, 0, report, 2, Math.Min(body.Length, report.Length - 2));

            bool sent = method switch
            {
                WriteMethod.WriteFile => HidNative.WriteFile(handle, report, report.Length, out _, IntPtr.Zero),
                WriteMethod.SetOutputReport => HidNative.HidD_SetOutputReport(handle, report, report.Length),
                _ => HidNative.HidD_SetFeature(handle, report, report.Length),
            };

            if (!sent)
            {
                output.WriteLine($"  {method,-16} {commandName}  send failed, Win32 {Marshal.GetLastWin32Error()}");
                return 0;
            }

            // Sweep every input report ID; only 0x07 is documented, but a wrong
            // guess there would look exactly like a silent device.
            var found = new List<string>();
            long deadline = Environment.TickCount64 + 900;
            while (Environment.TickCount64 < deadline)
            {
                for (byte id = 0; id <= 0x0F; id++)
                {
                    byte[] buffer = new byte[iface.InputReportLength];
                    buffer[0] = id;
                    if (!HidNative.HidD_GetInputReport(handle, buffer, buffer.Length)) continue;
                    if (!IsInteresting(buffer)) continue;

                    string hex = $"id {id:X2} -> {Convert.ToHexString(buffer[..Math.Min(28, buffer.Length)])}";
                    if (!found.Contains(hex)) found.Add(hex);
                }
                Thread.Sleep(10);
            }

            if (found.Count == 0)
            {
                output.WriteLine($"  {method,-16} {commandName}  idle");
                return 0;
            }

            output.WriteLine($"  {method,-16} {commandName}  {found.Count} REPLY:");
            foreach (string hex in found.Take(6)) output.WriteLine($"        {hex}");
            return 1;
        }
        finally { HidNative.CloseHandle(handle); }
    }

    /// <summary>An idle report is the report ID plus zeros; anything past byte 2 means real data.</summary>
    private static bool IsInteresting(byte[] report)
    {
        for (int i = 3; i < report.Length; i++)
            if (report[i] != 0) return true;
        return false;
    }

    private static void ProbeInterrupt(MaxwellInterface iface, TextWriter output, byte[] body, int waitMs = 2500)
    {
        IntPtr handle = Open(iface);
        if (handle == HidNative.InvalidHandle)
        {
            output.WriteLine($"    CreateFile failed, Win32 {Marshal.GetLastWin32Error()}");
            return;
        }

        byte[]? received = null;
        int readError = 0;

        // Abandoned rather than cancelled: acceptable for a probe that ends the process.
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
        HidNative.CancelIoEx(handle, IntPtr.Zero);
        reader.Join(200);

        if (received is not null)
            output.WriteLine($"    GOT {Convert.ToHexString(received[..Math.Min(28, received.Length)])}");
        else if (readError != 0)
            output.WriteLine($"    failed, Win32 {readError}");
        else
            output.WriteLine($"    nothing in {waitMs} ms");
    }

    private static IntPtr Open(MaxwellInterface iface) =>
        HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
}
