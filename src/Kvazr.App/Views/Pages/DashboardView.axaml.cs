using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kvazr.App.Views.Pages;

/// <summary>Artboard 1a: every device, one row each.</summary>
public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
