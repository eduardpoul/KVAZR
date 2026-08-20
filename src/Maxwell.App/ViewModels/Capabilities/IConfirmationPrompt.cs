using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>
/// Asks the user before something irreversible happens.
///
/// Deliberately not inside the action view model. A confirmation that the thing being
/// confirmed can implement is a confirmation the thing being confirmed can skip, and
/// two of the commands behind this are a factory reset and a re-pairing — the ones the
/// public protocol notes mislabel, and the ones that cost real hardware state.
/// </summary>
public interface IConfirmationPrompt
{
    /// <summary>
    /// Describes the consequence and returns whether to go ahead. Must default to no:
    /// a dialog dismissed with Escape or by clicking away is a refusal.
    /// </summary>
    Task<bool> ConfirmAsync(Capability capability);
}
