using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Maxwell.App.Controls;

/// <summary>
/// Shared chrome for one capability. The inner <c>ContentControl</c> resolves the
/// primitive's own control through the implicit templates in
/// <c>Styles/CapabilityTemplates.axaml</c>.
/// </summary>
public partial class CapabilityRow : UserControl
{
    public CapabilityRow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
