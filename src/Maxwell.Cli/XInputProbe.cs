using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Maxwell.Cli;

/// <summary>
/// Asks XInput what it knows about the controllers, before any driver claims it knows.
///
/// XInput is the one class protocol gamepads on Windows genuinely share, and it carries
/// exactly two things worth a row on a page: whether a pad is connected, and a battery
/// in four steps. This probe exists to check whether the Flydigi dongle answers both —
/// its pad speaks XInput through the dongle, and if the battery call works here, a
/// gamepad battery is a class capability rather than a vendor project.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class XInputProbe
{
    public static int Run()
    {
        Console.WriteLine("XInput slots 0..3");
        Console.WriteLine();

        for (uint slot = 0; slot < 4; slot++)
        {
            var state = default(XInputState);
            uint present = XInputGetState(slot, ref state);

            if (present != 0)
            {
                Console.WriteLine($"  slot {slot}  — пусто ({present})");
                continue;
            }

            var caps = default(XInputCapabilities);
            _ = XInputGetCapabilities(slot, 0, ref caps);

            var battery = default(XInputBatteryInformation);
            uint got = XInputGetBatteryInformation(slot, BatteryDevTypeGamepad, ref battery);

            string kind = battery.BatteryType switch
            {
                0x00 => "отключён",
                0x01 => "проводной, батареи нет",
                0x02 => "аккумулятор",
                0x03 => "щелочная",
                0x04 => "NiMH",
                _ => $"тип {battery.BatteryType}",
            };

            string level = battery.BatteryLevel switch
            {
                0x00 => "пусто",
                0x01 => "мало",
                0x02 => "половина",
                0x03 => "полный",
                _ => $"уровень {battery.BatteryLevel}",
            };

            Console.WriteLine($"  slot {slot}  подключён, subtype {caps.SubType}");
            Console.WriteLine($"            батарея ({got}): {kind}, {level}");
        }

        return 0;
    }

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
        public ushort LeftMotor;
        public ushort RightMotor;
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
