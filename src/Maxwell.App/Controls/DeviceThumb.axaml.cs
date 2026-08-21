using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Maxwell.App.Controls;

/// <summary>
/// The tile that stands for a device wherever one is listed — dashboard row, flyout row,
/// connect popup, device page.
///
/// It exists so the rule about which picture wins is written once. Five call sites each
/// deciding for themselves is five chances for a device to be drawn one way in the list
/// and another way in the tray.
/// </summary>
public partial class DeviceThumb : UserControl
{
    public static readonly StyledProperty<Bitmap?> PhotoProperty =
        AvaloniaProperty.Register<DeviceThumb, Bitmap?>(nameof(Photo));

    public static readonly StyledProperty<Geometry?> GlyphProperty =
        AvaloniaProperty.Register<DeviceThumb, Geometry?>(nameof(Glyph));

    public DeviceThumb() => InitializeComponent();

    /// <summary>The curated photograph for this model, when one exists. Usually none.</summary>
    public Bitmap? Photo
    {
        get => GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    /// <summary>The silhouette for the device's class. Always present.</summary>
    public Geometry? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
