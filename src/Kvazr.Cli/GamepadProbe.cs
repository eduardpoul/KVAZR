using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Windows.Devices.Power;
using Windows.Gaming.Input;

namespace Kvazr.Cli;

/// <summary>
/// Asks Windows what it knows about the gamepads, through both doors at once.
///
/// There are two, and they disagree by design. XInput answers about four numbered slots
/// and describes whatever sits in one as a standard Xbox pad, because that is the only
/// shape it has a vocabulary for. Windows.Gaming.Input answers about devices — with the
/// maker's own name, the usb ids, the true count of buttons and axes, and a battery in
/// milliwatt-hours rather than in quarters.
///
/// Both are asked here rather than one, because which of them is worth trusting is a
/// question about the pad on the desk, not a question about the APIs. The Flydigi dongle
/// already lied once through XInput — it calls a wireless pad "wired, no battery" — and
/// the only way to know whether the other door tells the truth is to open both and read
/// them side by side.
///
/// Read-only. Vibration is a write, and it does not live here.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class GamepadProbe
{
    public static int Run(int watchSeconds)
    {
        XInputSection();
        Console.WriteLine();
        GamingInputSection();

        if (watchSeconds > 0)
        {
            Console.WriteLine();
            Watch(watchSeconds);
        }

        return 0;
    }

    // ---- XInput ------------------------------------------------------------------

    private static void XInputSection()
    {
        Console.WriteLine("[1] XInput — четыре слота, и слот это не устройство");

        bool any = false;

        for (uint slot = 0; slot < XUserMaxCount; slot++)
        {
            var state = default(XInputState);
            uint present = XInputGetState(slot, ref state);

            if (present != ErrorSuccess)
            {
                string named = present == ErrorDeviceNotConnected ? " = ERROR_DEVICE_NOT_CONNECTED" : "";
                Console.WriteLine($"    слот {slot}  пусто ({present}{named})");
                continue;
            }

            any = true;

            var caps = default(XInputCapabilities);
            uint gotCaps = XInputGetCapabilities(slot, 0, ref caps);

            var battery = default(XInputBatteryInformation);
            uint gotBattery = XInputGetBatteryInformation(slot, BatteryDevTypeGamepad, ref battery);

            Console.WriteLine($"    слот {slot}  занят, пакет {state.PacketNumber}");
            Console.WriteLine($"             тип {caps.Type} / подтип {SubType(caps.SubType)}  (код {gotCaps})");
            Console.WriteLine($"             признаки: {Flags(caps.Flags)}");
            Console.WriteLine($"             моторы объявлены: L={caps.Vibration.LeftMotorSpeed} R={caps.Vibration.RightMotorSpeed}");
            Console.WriteLine($"             батарея (код {gotBattery}): {BatteryType(battery.BatteryType)}, {BatteryLevel(battery.BatteryLevel)}");
            Console.WriteLine($"             сейчас: {Snapshot(state.Gamepad)}");
        }

        if (!any) Console.WriteLine("    ни одного пада в слотах.");
    }

    /// <summary>Names from the documented table; anything outside it stays a number.</summary>
    private static string SubType(byte code) => code switch
    {
        0x00 => "неизвестный (0x00)",
        0x01 => "геймпад (0x01)",
        0x02 => "руль (0x02)",
        0x03 => "аркадный стик (0x03)",
        0x04 => "авиастик (0x04)",
        0x05 => "танцпол (0x05)",
        0x06 => "гитара (0x06)",
        0x07 => "гитара, вариант (0x07)",
        0x08 => "барабаны (0x08)",
        0x0B => "бас-гитара (0x0B)",
        0x13 => "аркадный пад (0x13)",
        _ => $"0x{code:X2}",
    };

    private static string Flags(ushort flags)
    {
        if (flags == 0) return "нет";

        List<string> named = [];
        if ((flags & 0x0001) != 0) named.Add("голос");
        if ((flags & 0x0002) != 0) named.Add("force feedback");
        if ((flags & 0x0004) != 0) named.Add("беспроводной");
        if ((flags & 0x0008) != 0) named.Add("модули");
        if ((flags & 0x0010) != 0) named.Add("без навигации");

        int rest = flags & ~0x001F;
        return string.Join(", ", named) + (rest != 0 ? $" + 0x{rest:X4}" : "");
    }

    private static string BatteryType(byte code) => code switch
    {
        0x00 => "отключена (BATTERY_TYPE_DISCONNECTED)",
        0x01 => "проводной, батареи нет (BATTERY_TYPE_WIRED)",
        0x02 => "щелочная (BATTERY_TYPE_ALKALINE)",
        0x03 => "NiMH (BATTERY_TYPE_NIMH)",
        0xFF => "тип неизвестен (BATTERY_TYPE_UNKNOWN)",
        _ => $"0x{code:X2} — вне таблицы",
    };

    private static string BatteryLevel(byte code) => code switch
    {
        0x00 => "пусто (EMPTY)",
        0x01 => "мало (LOW)",
        0x02 => "половина (MEDIUM)",
        0x03 => "полная (FULL)",
        _ => $"0x{code:X2} — вне таблицы",
    };

    private static string Snapshot(XInputGamepad pad) =>
        $"кнопки [{Buttons(pad.Buttons)}]  триггеры {pad.LeftTrigger}/{pad.RightTrigger}  "
        + $"стики L({pad.ThumbLX},{pad.ThumbLY}) R({pad.ThumbRX},{pad.ThumbRY})";

    private static string Buttons(ushort mask)
    {
        if (mask == 0) return "—";

        List<string> down = [];
        if ((mask & 0x0001) != 0) down.Add("вверх");
        if ((mask & 0x0002) != 0) down.Add("вниз");
        if ((mask & 0x0004) != 0) down.Add("влево");
        if ((mask & 0x0008) != 0) down.Add("вправо");
        if ((mask & 0x0010) != 0) down.Add("START");
        if ((mask & 0x0020) != 0) down.Add("BACK");
        if ((mask & 0x0040) != 0) down.Add("L3");
        if ((mask & 0x0080) != 0) down.Add("R3");
        if ((mask & 0x0100) != 0) down.Add("LB");
        if ((mask & 0x0200) != 0) down.Add("RB");
        if ((mask & 0x1000) != 0) down.Add("A");
        if ((mask & 0x2000) != 0) down.Add("B");
        if ((mask & 0x4000) != 0) down.Add("X");
        if ((mask & 0x8000) != 0) down.Add("Y");

        // 0x0400 is the Guide button and 0x0800 is unassigned. The documentation calls
        // every undefined bit reserved with undefined state, so they are shown raw
        // rather than named — a probe that names an undocumented bit is guessing.
        int reserved = mask & 0x0C00;
        if (reserved != 0) down.Add($"зарезервировано 0x{reserved:X4}");

        return string.Join(" ", down);
    }

    // ---- Windows.Gaming.Input ------------------------------------------------------

    private static void GamingInputSection()
    {
        Console.WriteLine("[2] Windows.Gaming.Input — список устройств");

        // The list is documented as filling lazily, and this process has no message pump
        // to deliver the added event, so the read is retried rather than trusted the
        // first time. If it stays empty that is reported as an empty list, not as "no
        // gamepads" — the two are not the same thing here.
        int seen = 0;
        RawGameController.RawGameControllerAdded += (_, _) => Interlocked.Increment(ref seen);

        IReadOnlyList<RawGameController> pads = RawGameController.RawGameControllers;

        for (int attempt = 0; attempt < 10 && pads.Count == 0; attempt++)
        {
            Thread.Sleep(100);
            pads = RawGameController.RawGameControllers;
        }

        Console.WriteLine($"    в списке {pads.Count}, событий о подключении за это время {Volatile.Read(ref seen)}");

        foreach (RawGameController pad in pads)
        {
            Console.WriteLine();
            Console.WriteLine($"    «{Text(() => pad.DisplayName)}»");
            Console.WriteLine($"        usb {Text(() => pad.HardwareVendorId.ToString("X4", CultureInfo.InvariantCulture))}"
                            + $":{Text(() => pad.HardwareProductId.ToString("X4", CultureInfo.InvariantCulture))}"
                            + $"   беспроводной: {Text(() => pad.IsWireless ? "да" : "нет")}");
            Console.WriteLine($"        кнопок {pad.ButtonCount}, осей {pad.AxisCount}, переключателей {pad.SwitchCount}");
            Console.WriteLine($"        моторы: force feedback {Text(() => pad.ForceFeedbackMotors.Count.ToString(CultureInfo.InvariantCulture))}"
                            + $", тактильные {Text(() => pad.SimpleHapticsControllers.Count.ToString(CultureInfo.InvariantCulture))}");
            Console.WriteLine($"        показывается как Xbox-геймпад: {Text(() => Gamepad.FromGameController(pad) is not null ? "да" : "нет")}");

            Console.WriteLine($"        заряд: {Charge(pad)}");

            bool[] buttons = new bool[pad.ButtonCount];
            var switches = new GameControllerSwitchPosition[pad.SwitchCount];
            double[] axes = new double[pad.AxisCount];

            ulong stamp = pad.GetCurrentReading(buttons, switches, axes);
            string axisText = string.Join(" ", axes.Select(a => a.ToString("F2", CultureInfo.InvariantCulture)));

            Console.WriteLine($"        снимок {stamp}: нажато [{Pressed(buttons)}]  оси [{axisText}]"
                            + $"  переключатели [{string.Join(" ", switches)}]");
        }
    }

    private static string Charge(RawGameController pad)
    {
        BatteryReport? report;

        try
        {
            report = pad.TryGetBatteryReport();
        }
        catch (Exception error)
        {
            return $"запрос не прошёл ({error.GetType().Name})";
        }

        if (report is null) return "устройство его не отдаёт";

        int? left = report.RemainingCapacityInMilliwattHours;
        int? full = report.FullChargeCapacityInMilliwattHours;

        string percent = left is { } l && full is { } f && f > 0
            ? $"  = {100.0 * l / f:F0}%"
            : "";

        return $"{report.Status}, {Show(left)} из {Show(full)} мВт·ч{percent}, ток {Show(report.ChargeRateInMilliwatts)} мВт";
    }

    private static string Pressed(bool[] buttons)
    {
        string down = string.Join(
            " ",
            buttons.Select((on, index) => (on, index))
                   .Where(b => b.on)
                   .Select(b => b.index.ToString(CultureInfo.InvariantCulture)));

        return down.Length == 0 ? "—" : down;
    }

    private static string Show(int? milliwattHours) =>
        milliwattHours?.ToString(CultureInfo.InvariantCulture) ?? "?";

    /// <summary>
    /// A WinRT property the device does not implement throws rather than answering, and a
    /// probe that dies on one missing property tells you less than one that prints the
    /// rest and names what was missing.
    /// </summary>
    private static string Text(Func<string> read)
    {
        try { return read(); }
        catch (Exception error) { return $"недоступно ({error.GetType().Name})"; }
    }

    // ---- live ----------------------------------------------------------------------

    private static void Watch(int seconds)
    {
        Console.WriteLine($"[3] Живьём {seconds} с — только XInput, только изменения (Ctrl+C чтобы бросить)");

        var last = new uint[XUserMaxCount];
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until)
        {
            for (uint slot = 0; slot < XUserMaxCount; slot++)
            {
                var state = default(XInputState);
                if (XInputGetState(slot, ref state) != ErrorSuccess) continue;

                // The packet number is the device's own change counter. Equal means
                // nothing moved, and comparing it is both cheaper and more honest than
                // comparing the values, which jitter on a stick that is merely worn.
                if (state.PacketNumber == last[slot]) continue;

                last[slot] = state.PacketNumber;
                Console.WriteLine($"    {DateTime.Now:HH:mm:ss.fff} слот {slot}  {Snapshot(state.Gamepad)}");
            }

            Thread.Sleep(8);
        }
    }

    // ---- native --------------------------------------------------------------------

    private const uint XUserMaxCount = 4;
    private const uint ErrorSuccess = 0;
    private const uint ErrorDeviceNotConnected = 1167;
    private const byte BatteryDevTypeGamepad = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputCapabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, ref XInputState state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetCapabilities(uint userIndex, uint flags, ref XInputCapabilities capabilities);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(
        uint userIndex, byte devType, ref XInputBatteryInformation batteryInformation);
}
