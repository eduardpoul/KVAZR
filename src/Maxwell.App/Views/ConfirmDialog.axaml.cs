using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Maxwell.Core.Devices;

namespace Maxwell.App.Views;

/// <summary>Modal question for a capability whose write cannot be taken back.</summary>
public partial class ConfirmDialog : Window
{
    private bool _accepted;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(Capability capability)
        : this()
    {
        ArgumentNullException.ThrowIfNull(capability);

        Title = capability.Title;
        TitleText.Text = capability.Title;
        ConfirmButton.Content = "Выполнить";

        Add(capability.Subtitle);
        if (!capability.IsReversible) Add("Это действие нельзя отменить.");
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

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _accepted = true;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Dismissed any other way — Escape, the close button — is a refusal.
        if (!_accepted) Close(false);

        base.OnClosing(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
