using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Maxwell.App.Views.Pages;

/// <summary>Artboard 2a rendered from the live controls. See the view model.</summary>
public partial class CapabilityShowcaseView : UserControl
{
    public CapabilityShowcaseView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
