using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.Platform;

/// <summary>
/// Which device the tray icon and the global shortcuts act on.
///
/// One shortcut can move one slider, so with several devices attached something has to
/// choose. Artboard 4c settles how: the user picks, by hand, from the row's own menu.
///
/// The rule underneath — first device that can be driven, then first present at all —
/// stays as the fallback rather than as an override. It exists so a fresh installation
/// with no choice yet still has an answer, and so unplugging the chosen device does not
/// leave the tray pointing at nothing. As the only answer it was wrong about as often as
/// it was right, and it changed its mind whenever something else connected, which is the
/// worst property for the device a global shortcut acts on.
/// </summary>
public sealed class PrimaryDevice
{
    private readonly IDeviceRegistry _registry;
    private readonly HiddenDevices _hidden;
    private readonly ISettingsStore? _settings;

    public PrimaryDevice(IDeviceRegistry registry, HiddenDevices hidden, ISettingsStore? settings = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hidden);

        _registry = registry;
        _hidden = hidden;
        _settings = settings;
    }

    /// <summary>Which device the user picked, or empty for none.</summary>
    public string Chosen => _settings?.Current.PrimaryDeviceKey ?? string.Empty;

    /// <summary>Records the choice. The rule stays as the fallback, not as an override.</summary>
    public Task ChooseAsync(string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        if (_settings is null) return Task.CompletedTask;

        _settings.Current.PrimaryDeviceKey = deviceKey;
        return _settings.SaveAsync();
    }

    /// <summary>
    /// A hidden device is never the primary one, and filtering here covers both surfaces
    /// that hang off the choice: the tray icon draws this device's battery, and the
    /// connect popup fires when this device becomes usable. A device that had been put
    /// away and then announced itself with a popup would break the promise hiding makes
    /// in the most conspicuous way available.
    /// </summary>
    private IEnumerable<DeviceSnapshot> Candidates => _registry.Devices.Where(_hidden.IsVisible);

    /// <summary>
    /// The chosen device if it is here, otherwise the rule. The choice comes first
    /// because it was made on purpose; the rule exists so that a fresh installation with
    /// no choice yet still has an answer, and so that unplugging the chosen device does
    /// not leave the tray showing nothing at all.
    /// </summary>
    public DeviceSnapshot? Snapshot =>
        Candidates.FirstOrDefault(d => d.IsPresent && string.Equals(d.Info.Key, Chosen, StringComparison.Ordinal))
        ?? Candidates.FirstOrDefault(d => d.CanControl)
        ?? Candidates.FirstOrDefault(d => d.IsPresent);

    public IDeviceSession? Session =>
        Snapshot is { } snapshot ? _registry.Session(snapshot.Info.Key) : null;
}
