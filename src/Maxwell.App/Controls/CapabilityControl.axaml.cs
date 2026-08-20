using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Maxwell.App.Controls;

/// <summary>
/// The control for one capability, with the eight states drawn on the frame around it.
/// The inner <c>ContentControl</c> resolves the primitive's own control through the
/// implicit templates declared in the XAML.
/// </summary>
public partial class CapabilityControl : UserControl
{
    public CapabilityControl() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
