using Kvazr.Core.Settings;

namespace Kvazr.Core.Devices;

/// <summary>
/// Remembers which untested capabilities the hardware has since accepted.
///
/// The amber "not verified yet" mark is supposed to disappear for good after the first
/// successful write, not come back on the next launch — so the promotion has to outlive
/// the process. Kept behind an interface because the session should not care whether
/// that means a settings file or something else later.
/// </summary>
public interface ICapabilityLedger
{
    bool IsConfirmed(string deviceKey, string capabilityId);

    Task ConfirmAsync(string deviceKey, string capabilityId, CancellationToken cancellationToken = default);
}

/// <summary>Ledger backed by the application settings file.</summary>
public sealed class SettingsCapabilityLedger : ICapabilityLedger
{
    private readonly ISettingsStore _store;

    public SettingsCapabilityLedger(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public bool IsConfirmed(string deviceKey, string capabilityId)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        ArgumentNullException.ThrowIfNull(capabilityId);

        return _store.Current.ConfirmedCapabilities.TryGetValue(deviceKey, out List<string>? confirmed)
            && confirmed.Contains(capabilityId, StringComparer.Ordinal);
    }

    public async Task ConfirmAsync(
        string deviceKey,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceKey);
        ArgumentNullException.ThrowIfNull(capabilityId);

        if (IsConfirmed(deviceKey, capabilityId)) return;

        Dictionary<string, List<string>> all = _store.Current.ConfirmedCapabilities;

        if (!all.TryGetValue(deviceKey, out List<string>? confirmed))
        {
            confirmed = [];
            all[deviceKey] = confirmed;
        }

        confirmed.Add(capabilityId);

        await _store.SaveAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Forgets everything on exit. For tests and for hosts without settings.</summary>
public sealed class InMemoryCapabilityLedger : ICapabilityLedger
{
    private readonly HashSet<string> _confirmed = new(StringComparer.Ordinal);

    public bool IsConfirmed(string deviceKey, string capabilityId) =>
        _confirmed.Contains($"{deviceKey}/{capabilityId}");

    public Task ConfirmAsync(string deviceKey, string capabilityId, CancellationToken cancellationToken = default)
    {
        _confirmed.Add($"{deviceKey}/{capabilityId}");
        return Task.CompletedTask;
    }
}
