using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Kvazr.Core.Devices;

namespace Kvazr.App.Views;

/// <summary>Modal question for a capability whose write cannot be taken back.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        // There used to be a hand-written InitializeComponent() beside this call. The
        // generated one takes an optional argument, so both compiled and the hand-written
        // one won the call — leaving TitleText, Body and the buttons below null, and this
        // dialog throwing the moment it was constructed for a real capability.
        InitializeComponent();
    }

    public ConfirmDialog(Capability capability)
        : this()
    {
        ArgumentNullException.ThrowIfNull(capability);

        Title = capability.Title;
        TitleText.Text = capability.Title;
        ConfirmButton.Content = Kvazr.App.Localization.Localizer.Current.Say("Run");
        CancelButton.Content = Kvazr.App.Localization.Localizer.Current.Say("Cancel");

        Add(capability.Subtitle);
        if (!capability.IsReversible) Add(Kvazr.App.Localization.Localizer.Current.Say("This cannot be undone."));
        Add(capability.Note);

        // Focus lands on the way out, not on the way through.
        CancelButton.Focus();
    }

    private void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Body.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "secondary" },
        });
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    // No OnClosing override, and that is the fix rather than a simplification.
    //
    // There was one, and it called Close(false) whenever the dialog had not been
    // confirmed — from inside the closing pass, which is to say it started a second
    // close while the first was still running. Pressing Cancel went through it, and
    // the process went away without a word: the kind of failure that leaves no dialog
    // and no log because the stack is gone.
    //
    // What it was for is already true. A dialog dismissed any other way — Escape, the
    // close button, Cancel — leaves ShowDialog<bool> with the default of its result
    // type, and the default of a bool is no.
}
