using Kvazr.Core.Devices;

namespace Kvazr.App.ViewModels.Devices;

/// <summary>How a device's power should be drawn.</summary>
public enum BatteryDisplay
{
    /// <summary>A real percentage. The page fills its glyph; the tray row prints the number.</summary>
    Charge,

    /// <summary>
    /// The class carries a battery and this link will not report it — a keyboard behind
    /// its dongle, a pad whose receiver claims to be wired. Drawn as an empty cell with
    /// the reason beside it, never as a zero.
    /// </summary>
    Absent,

    /// <summary>
    /// A device that runs off the wire and was never going to have a battery: a USB
    /// microphone, a desk speaker. "From the cable" is the whole story, and a dash here would
    /// invite the reader to go looking for a number that does not exist.
    /// </summary>
    Mains,

    /// <summary>
    /// Not connected, so nothing is drawn. The last charge read is not the device's charge
    /// now, and a number beside a device that is not here reads as if it were — a pair of
    /// AirPods under "not connected" with 100% and 90% beside them, and "updated 20:50"
    /// under that, said two contradictory things at once.
    /// </summary>
    Offline,
}

/// <summary>
/// Which of the three power states a snapshot is in.
///
/// Shared by the device page and the tray row on purpose. They draw the answer very
/// differently — a 30×17 glyph against a line of 12-pixel text — but they must not
/// disagree about what it is, and two copies of "does this class have a battery" is
/// exactly the sort of pair that drifts.
/// </summary>
public static class BatteryReadout
{
    public static BatteryDisplay Mode(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsPresent) return BatteryDisplay.Offline;

        if (snapshot.BatteryPercent is not null) return BatteryDisplay.Charge;

        return ExpectsBattery(snapshot.Info.Class) ? BatteryDisplay.Absent : BatteryDisplay.Mains;
    }

    /// <summary>
    /// Whether a device of this class runs on a battery at all. By class rather than by
    /// model: the answer is a property of the kind of thing it is, and a catalogue entry
    /// for every headset ever made is not a maintainable way to learn that headsets have
    /// batteries.
    /// </summary>
    public static bool ExpectsBattery(DeviceClass deviceClass) => deviceClass
        is DeviceClass.Headset or DeviceClass.Earbuds or DeviceClass.Mouse
        or DeviceClass.Keyboard or DeviceClass.Gamepad or DeviceClass.Speaker;
}
