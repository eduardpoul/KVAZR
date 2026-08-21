using Maxwell.App.Branding;
using Maxwell.Core.Devices;

namespace Maxwell.App.Tray;

/// <summary>
/// The part of one device that the tray glyph can actually express. Four facts, because
/// four is all sixteen pixels hold.
/// </summary>
internal readonly record struct TrayDeviceReading(
    bool IsPresent,
    bool Answers,
    int? BatteryPercent,
    bool IsCharging)
{
    public static TrayDeviceReading From(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new TrayDeviceReading(
            snapshot.IsPresent,
            snapshot.Availability == DeviceAvailability.Active,
            snapshot.BatteryPercent,
            snapshot.IsCharging);
    }
}

/// <summary>
/// Reduces everything attached to the one state the mark can show.
///
/// A single glyph and several devices is a lossy problem, so the order below is the
/// whole design: it is a priority list, not a lookup. The most urgent thing wins,
/// because the tray icon's job is to make the user open the window at the right
/// moment — not to summarise. Kept apart from the drawing so the decision can be
/// argued about, and tested, without a render surface.
/// </summary>
internal static class TrayState
{
    public static KvazrMarkState For(
        IReadOnlyList<TrayDeviceReading> devices,
        bool discovering,
        bool firmwareInstalling,
        int lowBatteryThreshold)
    {
        ArgumentNullException.ThrowIfNull(devices);

        // Above everything: a write in progress is the one state where the user
        // unplugging something does real damage.
        if (firmwareInstalling) return KvazrMarkState.FirmwareUpdating;

        List<TrayDeviceReading> present = [.. devices.Where(d => d.IsPresent)];

        // Nothing present. Discovery still running is a different thing from having
        // looked and found nothing, and the identity gives them different states —
        // one turns, the other goes grey.
        if (present.Count == 0) return discovering ? KvazrMarkState.Searching : KvazrMarkState.NoDevices;

        // Something is plugged in and silent. Ranked above low battery because a
        // device that does not answer has no battery reading worth believing.
        if (present.Any(d => !d.Answers)) return KvazrMarkState.LinkError;

        // Charging is excluded deliberately: a headset on the cradle at 8% is being
        // dealt with, and an amber planet would be nagging about a solved problem.
        if (present.Any(d => !d.IsCharging && d.BatteryPercent is { } percent && percent < lowBatteryThreshold))
        {
            return KvazrMarkState.LowBattery;
        }

        // Everything present answers, and discovery is still sweeping for more. The
        // turning mark is honest here and the resting one would not be.
        return discovering ? KvazrMarkState.Searching : KvazrMarkState.AllConnected;
    }
}
