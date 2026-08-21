namespace Maxwell.Race.Abstractions;

/// <summary>
/// What the operating system knows about a Bluetooth-attached headset.
///
/// Lives beside the HID abstractions rather than in the domain layer: this is the same
/// kind of question as "which hidraw nodes exist", answered by the same platform code,
/// and the diagnostic harness needs it as much as the application does.
/// </summary>
/// <param name="Present">True when the headset is paired and connected over Bluetooth.</param>
/// <param name="BatteryPercent">Level, or -1 when the system does not report one.</param>
public readonly record struct BluetoothStatus(bool Present, int BatteryPercent)
{
    public static readonly BluetoothStatus Absent = new(false, -1);

    public bool HasBattery => BatteryPercent >= 0;
}

/// <summary>
/// Reports a Bluetooth connection that carries audio but no control channel.
///
/// The Maxwell advertises only audio profiles over Bluetooth — A2DP, AVRCP and
/// Hands-Free — with no HID service, so the RACE stack has nothing to attach to and
/// settings are genuinely unreachable on that link. What does come across is the
/// battery level, which Hands-Free reports and Windows exposes as a device property,
/// so the headset is not entirely opaque even when it is only on Bluetooth.
///
/// A vendor RFCOMM channel does carry something, but it speaks a protocol of Audeze's
/// own that no binary we can read describes. See docs/BLUETOOTH.md for what was
/// established and what it would take to go further.
/// </summary>
public interface IBluetoothMonitor
{
    BluetoothStatus Query();
}

/// <summary>Used where the platform has no implementation: always reports nothing.</summary>
public sealed class NullBluetoothMonitor : IBluetoothMonitor
{
    public static readonly NullBluetoothMonitor Instance = new();

    private NullBluetoothMonitor()
    {
    }

    public BluetoothStatus Query() => BluetoothStatus.Absent;
}
