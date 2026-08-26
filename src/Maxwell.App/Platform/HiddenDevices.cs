using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.Platform;

/// <summary>
/// Which devices the user has put away, shared by every surface that lists devices.
///
/// One owner rather than each screen reading the setting for itself, because the promise
/// hiding makes is that the device disappears from the dashboard, the tray, the flyout
/// and the notifications together. A screen that forgot to ask would break that promise
/// in the one place the user notices least and trusts most.
///
/// Hidden is not forgotten. The pairing in Windows is untouched, the registry still
/// holds the device, and unhiding brings it back whole — which is also why the registry
/// is not filtered at the source: something has to be able to list what is hidden.
/// </summary>
public sealed class HiddenDevices
{
    private readonly ISettingsStore _settings;

    public HiddenDevices(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>Raised when something is hidden or brought back, so lists can re-filter.</summary>
    public event EventHandler? Changed;

    public int Count => _settings.Current.HiddenDevices.Count;

    public bool IsVisible(DeviceSnapshot device)
    {
        ArgumentNullException.ThrowIfNull(device);

        AppSettings settings = _settings.Current;

        // Removed, and still gone. The entry is not consulted once the device is back:
        // hardware somebody has just plugged in outranks anything remembered about it,
        // and a list that quietly ignores it is the one fault a device manager cannot
        // afford. Housekeeping drops the stale entry separately — see PruneAsync.
        if (!device.IsPresent && settings.RemovedDevices.ContainsKey(device.Info.Key)) return false;

        return !DeviceVisibility.IsHidden(
            device.Info,
            settings.HiddenDevices.Keys.ToHashSet(StringComparer.Ordinal),
            settings.HideSilentDevices);
    }

    /// <summary>
    /// Every device currently hidden, newest first, paired with when it went away. Read
    /// from the registry rather than from the setting alone so a device that has been
    /// unpaired since is not offered for un-hiding.
    /// </summary>
    public IReadOnlyList<(DeviceSnapshot Device, DateTimeOffset HiddenAt)> List(IDeviceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        Dictionary<string, DateTimeOffset> hidden = _settings.Current.HiddenDevices;

        return
        [
            .. registry.Devices
                .Where(d => hidden.ContainsKey(d.Info.Key))
                .Select(d => (Device: d, HiddenAt: hidden[d.Info.Key]))
                .OrderByDescending(p => p.HiddenAt),
        ];
    }

    public Task HideAsync(string deviceKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        _settings.Current.HiddenDevices[deviceKey] = now;
        return SaveAsync();
    }

    /// <summary>
    /// Takes a device that is no longer here off every list, and drops what was
    /// remembered about it.
    ///
    /// The stored settings go with the row on purpose: that is the whole difference
    /// between this and hiding. Hiding keeps a device's colour and its slots ready for
    /// when it comes out of the drawer; removing says the thing itself is gone, and
    /// keeping its preferences would be keeping a note about somebody else's hardware.
    ///
    /// The pairing in Windows is not touched. Unpairing is a heavier and separate thing —
    /// it is offered as "forget" — and doing it here would answer "take this row away"
    /// by changing the system.
    /// </summary>
    public Task RemoveAsync(string deviceKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        AppSettings settings = _settings.Current;

        settings.RemovedDevices[deviceKey] = now;
        settings.DeviceMemory.Remove(deviceKey);
        settings.HiddenDevices.Remove(deviceKey);

        return SaveAsync();
    }

    /// <summary>
    /// Drops the removals of devices that have come back, so the file does not collect
    /// keys for hardware that is on the desk.
    ///
    /// Housekeeping only: <see cref="IsVisible"/> already shows a device that is present
    /// whatever this dictionary says, so a pass that never runs costs nothing but bytes.
    /// </summary>
    public Task PruneAsync(IDeviceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        Dictionary<string, DateTimeOffset> removed = _settings.Current.RemovedDevices;
        if (removed.Count == 0) return Task.CompletedTask;

        List<string> back =
        [
            .. registry.Devices.Where(d => d.IsPresent && removed.ContainsKey(d.Info.Key)).Select(d => d.Info.Key),
        ];

        if (back.Count == 0) return Task.CompletedTask;

        foreach (string key in back) removed.Remove(key);

        return SaveAsync();
    }

    public Task ShowAsync(string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);

        _settings.Current.HiddenDevices.Remove(deviceKey);
        return SaveAsync();
    }

    /// <summary>
    /// Brings everything back, including devices swept aside by the anonymous-device
    /// rule — which is why that setting is turned off here too. Leaving it on would put
    /// them straight back where they were and make the button look broken.
    /// </summary>
    public Task ShowAllAsync()
    {
        _settings.Current.HiddenDevices.Clear();
        _settings.Current.RemovedDevices.Clear();
        _settings.Current.HideSilentDevices = false;
        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        await _settings.SaveAsync().ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
