using Kvazr.Core.Settings;

namespace Kvazr.App.Platform;

/// <summary>
/// Which of a device's controls the tray panel carries beyond the ones it always does.
///
/// One owner rather than a setting each surface reads for itself, for the same reason as
/// <see cref="HiddenDevices"/>: the switch is on the device's page and what it changes is
/// the panel, two screens that never see each other. A panel that read the file only when
/// it was built would go on showing yesterday's answer until the application restarted.
///
/// Today it holds one question — whether a microphone's headphone socket appears in the
/// panel — and it is a question rather than a policy because the panel is 360 pixels wide.
/// Every row it gains is a row something else loses, so it is the user's call and not
/// ours.
/// </summary>
public sealed class TrayExtras
{
    private readonly ISettingsStore _settings;

    public TrayExtras(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>Raised after a change, so an open panel rebuilds rather than going stale.</summary>
    public event EventHandler? Changed;

    public bool ShowsMonitoring(string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        return _settings.Current.MonitoringInTray.Contains(deviceKey);
    }

    public async Task SetMonitoringAsync(string deviceKey, bool show)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        HashSet<string> chosen = _settings.Current.MonitoringInTray;

        bool moved = show ? chosen.Add(deviceKey) : chosen.Remove(deviceKey);
        if (!moved) return;

        await _settings.SaveAsync().ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
