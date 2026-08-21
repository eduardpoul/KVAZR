using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Maxwell.App.Views.Pages;

internal sealed partial class IdentityView : UserControl
{
    public IdentityView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
