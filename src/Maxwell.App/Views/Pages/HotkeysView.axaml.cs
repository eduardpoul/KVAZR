using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Maxwell.App.ViewModels.Pages;

namespace Maxwell.App.Views.Pages;

public partial class HotkeysView : UserControl
{
    private HotkeyRow? _capturing;
    private Button? _capturingButton;

    public HotkeysView()
    {
        AvaloniaXamlLoader.Load(this);

        // Tunnelling: a captured gesture must not also reach the focused control, or
        // pressing Space to bind would activate the button we are binding from.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnCaptureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HotkeyRow row } button) return;

        // A second click cancels rather than leaving the UI stuck in capture mode.
        if (ReferenceEquals(_capturing, row))
        {
            EndCapture();
            return;
        }

        EndCapture();

        _capturing = row;
        _capturingButton = button;
        button.Content = "нажмите сочетание…";
        button.Focus();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing is null) return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            EndCapture();
            return;
        }

        // Backspace clears the binding entirely.
        if (e.Key == Key.Back)
        {
            Assign("");
            return;
        }

        if (IsModifier(e.Key)) return;

        // Windows refuses a bare key as a global shortcut, and binding one would
        // swallow that key everywhere else on the system.
        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift) return;

        var gesture = new StringBuilder();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) gesture.Append("Ctrl+");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) gesture.Append("Alt+");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) gesture.Append("Shift+");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) gesture.Append("Win+");
        gesture.Append(Describe(e.Key));

        Assign(gesture.ToString());
    }

    private void Assign(string gesture)
    {
        if (_capturing is null || DataContext is not HotkeysViewModel model) return;

        model.Assign(_capturing, gesture);
        EndCapture();
    }

    private void EndCapture()
    {
        if (_capturingButton is not null && _capturing is not null)
            _capturingButton.Content = _capturing.GestureText;

        _capturing = null;
        _capturingButton = null;
    }

    private static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin;

    private static string Describe(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Space => "Space",
        Key.Enter => "Enter",
        _ => key.ToString(),
    };
}
