using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.Platform;

/// <summary>
/// A driver's scratchpad, kept in the same file as everything else the user has chosen.
///
/// The same file on purpose. A colour picked for a microphone is a preference like any
/// other and belongs where somebody would look for it; a second store beside the settings
/// would be one more thing to back up, migrate and explain.
///
/// Every write saves. That is a two-kilobyte file rewritten on a change somebody made by
/// hand, and the writes that could come fast — a dragged colour slider — are debounced
/// before they ever reach here.
/// </summary>
public sealed class SettingsDeviceMemory : IDeviceMemory
{
    private readonly ISettingsStore _store;

    public SettingsDeviceMemory(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public string? Recall(string deviceKey, string slot)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        ArgumentNullException.ThrowIfNull(slot);

        return _store.Current.DeviceMemory.TryGetValue(deviceKey, out Dictionary<string, string>? device)
            && device.TryGetValue(slot, out string? value)
                ? value
                : null;
    }

    public void Remember(string deviceKey, string slot, string value)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(value);

        Dictionary<string, Dictionary<string, string>> memory = _store.Current.DeviceMemory;

        if (!memory.TryGetValue(deviceKey, out Dictionary<string, string>? device))
        {
            memory[deviceKey] = device = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (device.TryGetValue(slot, out string? before) && before == value) return;

        device[slot] = value;

        // Fire and forget, like every other setting written from a control: the value is
        // already in memory and the file catching up a moment later is not something
        // anybody can observe.
        _ = _store.SaveAsync();
    }
}
