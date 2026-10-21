using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kvazr.App.Views.Pages;

/// <summary>The one device page, assembled from capabilities. See the view model.</summary>
public partial class DevicePageView : UserControl
{
    public DevicePageView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
