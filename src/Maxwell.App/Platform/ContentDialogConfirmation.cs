using Avalonia.Controls;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.Views;
using Maxwell.Core.Devices;

namespace Maxwell.App.Platform;

/// <summary>
/// Asks with a modal window over the main one.
///
/// Defaults to no in every direction: no owner window means no confirmation is
/// possible, and no confirmation means the command does not run.
/// </summary>
public sealed class ModalConfirmation : IConfirmationPrompt
{
    private readonly Func<Window?> _owner;

    public ModalConfirmation(Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public async Task<bool> ConfirmAsync(Capability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        if (_owner() is not { } owner) return false;

        var dialog = new ConfirmDialog(capability);
        return await dialog.ShowDialog<bool>(owner).ConfigureAwait(true);
    }
}
