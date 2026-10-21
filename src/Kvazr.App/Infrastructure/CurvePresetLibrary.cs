using Kvazr.Core.Devices;
using Kvazr.Core.Settings;

namespace Kvazr.App.Infrastructure;

/// <summary>
/// One row of the equaliser's picker, whichever side it came from.
///
/// <paramref name="Device"/> is the headset's own choice and null for a saved curve, and
/// that is what tells the two apart everywhere else: only a saved one can be deleted, and
/// only a device one is set by writing a preset number.
/// </summary>
public sealed record CurvePresetOption(string Label, bool IsSaved, CapabilityChoice? Device);

/// <summary>
/// The equaliser curves the wearer has saved, as the curve control sees them.
///
/// A thin thing on purpose. It owns no state of its own — the settings file is the only
/// copy — and every method reads that file's list afresh, so two pages showing the same
/// device cannot drift apart. Saving is fire-and-forget against the store, because the
/// person has already seen the curve applied and a spinner on a file write of two hundred
/// bytes would be theatre.
/// </summary>
public sealed class CurvePresetLibrary
{
    private readonly ISettingsStore _store;

    public CurvePresetLibrary(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Raised after the list changes, so open pages can redraw their picker.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The saved curves that fit a device with this many bands, in the order they were
    /// saved. Ordering by name was tried and is worse: a list that reshuffles itself when
    /// a preset is renamed loses the one thing a person navigates it by, which is where
    /// the entry was last time.
    /// </summary>
    public IReadOnlyList<EqCurvePreset> For(int bands) =>
        [.. _store.Current.EqPresets.Where(p => p.Bands.Count == bands)];

    public EqCurvePreset? Find(string name) =>
        _store.Current.EqPresets.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Stores a curve under a name, replacing a curve already saved under it.
    ///
    /// Replacing rather than refusing, because the name box is prefilled with the name of
    /// whatever is loaded: saving twice in a row is how a person edits a preset, and
    /// making that an error would mean deleting the old one first every time.
    /// </summary>
    public void Save(string name, IReadOnlyList<int> bands)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(bands);

        string trimmed = name.Trim();
        if (trimmed.Length == 0 || bands.Count == 0) return;

        List<EqCurvePreset> all = _store.Current.EqPresets;

        if (Find(trimmed) is { } existing)
        {
            existing.Name = trimmed;
            existing.Bands = [.. bands];
        }
        else
        {
            all.Add(new EqCurvePreset { Name = trimmed, Bands = [.. bands] });
        }

        Persist();
    }

    public void Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Find(name) is not { } preset) return;

        _store.Current.EqPresets.Remove(preset);
        Persist();
    }

    private void Persist()
    {
        Changed?.Invoke(this, EventArgs.Empty);

        // Not awaited. The list on screen is already right — it reads the same object the
        // write is serialising — so nothing is waiting on the file, and stopping to watch
        // it would interrupt someone naming their second preset. The store serialises its
        // own writes behind a gate and logs a write it could not make, so a failure here
        // is late news rather than no news.
        _ = _store.SaveAsync();
    }
}
