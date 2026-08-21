using Avalonia.Controls;
using Avalonia.Input;

using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Settings;

namespace Maxwell.App.Views.Pages;

/// <summary>
/// Captures the key press that becomes a shortcut.
///
/// This lives in the view rather than the view model because there is no control that can
/// be asked to report a combination: a TextBox swallows the modifiers into text, a Button
/// never sees a bare key at all, and both would take Tab and Escape for navigation before
/// anything here saw them. Handling the tunnelled event on the page is the only place
/// every key press is still available.
/// </summary>
public partial class HotkeysView : UserControl
{
    public HotkeysView()
    {
        InitializeComponent();

        // Tunnelling, not bubbling. By the time a key press has bubbled, a focused button
        // has already acted on Space and Enter, and the recording would never see them.
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HotkeysViewModel page || !page.IsRecording) return;

        // A modifier on its own is somebody still assembling the combination, not the
        // combination itself. Waiting for the non-modifier key is what lets Ctrl+Alt+Up
        // be pressed the way anyone actually presses it.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            page.Stop();
            return;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Win;

        // A bare letter as a global shortcut would take that key away from every other
        // application on the machine, which is not a thing to let somebody do by accident.
        if (modifiers == HotkeyModifiers.None) return;

        page.Capture(HotkeyGesture.Format(modifiers, e.Key.ToString()));
    }
}
