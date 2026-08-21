using Maxwell.Core.Devices;

namespace Maxwell.App.Platform;

/// <summary>
/// Which device the tray icon and the global shortcuts act on.
///
/// One icon can show one number and one shortcut can move one slider, so with several
/// devices attached something has to choose. The rule is the simplest one that is never
/// surprising: the first device that can actually be driven, falling back to the first
/// one present at all.
///
/// The settings screen offers this as a choice ("чей процент показывать"), so this will
/// grow a stored preference; until then it stays a rule rather than a guess.
/// </summary>
public sealed class PrimaryDevice
{
    private readonly IDeviceRegistry _registry;
    private readonly HiddenDevices _hidden;

    public PrimaryDevice(IDeviceRegistry registry, HiddenDevices hidden)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hidden);

        _registry = registry;
        _hidden = hidden;
    }

    /// <summary>
    /// A hidden device is never the primary one, and filtering here covers both surfaces
    /// that hang off the choice: the tray icon draws this device's battery, and the
    /// connect popup fires when this device becomes usable. A device that had been put
    /// away and then announced itself with a popup would break the promise hiding makes
    /// in the most conspicuous way available.
    /// </summary>
    private IEnumerable<DeviceSnapshot> Candidates => _registry.Devices.Where(_hidden.IsVisible);

    public DeviceSnapshot? Snapshot =>
        Candidates.FirstOrDefault(d => d.CanControl)
        ?? Candidates.FirstOrDefault(d => d.IsPresent);

    public IDeviceSession? Session =>
        Snapshot is { } snapshot ? _registry.Session(snapshot.Info.Key) : null;
}
