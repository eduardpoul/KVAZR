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

    public PrimaryDevice(IDeviceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public DeviceSnapshot? Snapshot =>
        _registry.Devices.FirstOrDefault(d => d.CanControl)
        ?? _registry.Devices.FirstOrDefault(d => d.IsPresent);

    public IDeviceSession? Session =>
        Snapshot is { } snapshot ? _registry.Session(snapshot.Info.Key) : null;
}
