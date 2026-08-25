using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Maxwell.Cli;

/// <summary>
/// The pointer settings Windows itself keeps — the ones that can actually be changed
/// without any vendor protocol at all.
///
/// Everything else measured about mice so far is read-only or worse: a descriptor that
/// over-declares, a charge that only Bluetooth gives, a resolution that lives in one
/// manufacturer's protocol. These are different. They are readable and writable, they are
/// what people actually adjust — speed, acceleration, how far a notch scrolls — and they
/// work for every mouse from every maker, because they belong to Windows rather than to
/// the device.
///
/// The catch is stated with them: they are settings of the pointer, not of a mouse. One
/// machine has one pointer however many mice are on the desk, so changing the speed
/// changes it for all of them. That is exactly why a gaming mouse keeps its resolution in
/// its own firmware instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PointerSettingsProbe
{
    private const uint GetMouse = 0x0003;
    private const uint GetMouseSpeed = 0x0070;
    private const uint GetWheelScrollLines = 0x0068;
    private const uint GetWheelScrollChars = 0x006C;
    private const uint GetMouseHoverTime = 0x0066;

    private const int SwapButton = 23;

    public static int Run()
    {
        Console.WriteLine("[pointer] What Windows keeps about the pointer, for every mouse at once");
        Console.WriteLine();

        int speed = 0;
        Native.SystemParametersInfo(GetMouseSpeed, 0, ref speed, 0);

        int[] acceleration = new int[3];
        Native.SystemParametersInfoArray(GetMouse, 0, acceleration, 0);

        int lines = 0;
        Native.SystemParametersInfo(GetWheelScrollLines, 0, ref lines, 0);

        int chars = 0;
        Native.SystemParametersInfo(GetWheelScrollChars, 0, ref chars, 0);

        int hover = 0;
        Native.SystemParametersInfo(GetMouseHoverTime, 0, ref hover, 0);

        Console.WriteLine($"  speed          {speed}  of 20        (pointer speed slider)");
        Console.WriteLine($"  acceleration   {(acceleration[2] != 0 ? "on" : "off")}"
                          + $"           (thresholds {acceleration[0]}, {acceleration[1]})");
        Console.WriteLine($"  scroll         {(lines == -1 ? "a page" : lines + " lines")}  per notch");
        Console.WriteLine($"  scroll sideways {chars} characters per notch");
        Console.WriteLine($"  double click   {Native.GetDoubleClickTime()} ms");
        Console.WriteLine($"  buttons        {(Native.GetSystemMetrics(SwapButton) != 0 ? "swapped" : "as drawn")}");
        Console.WriteLine($"  hover          {hover} ms");

        Console.WriteLine();
        Console.WriteLine("All of these are writable through the same call that read them, and all of");
        Console.WriteLine("them belong to the machine rather than to a mouse: one pointer, however many");
        Console.WriteLine("mice. Which is the whole reason a gaming mouse keeps its resolution in its own");
        Console.WriteLine("firmware — that one is per-device and these cannot be.");

        return 0;
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        internal static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint update);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        internal static extern bool SystemParametersInfoArray(
            uint action, uint param, [In, Out] int[] value, uint update);

        [DllImport("user32.dll")]
        internal static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);
    }
}
