using Avalonia;
using Avalonia.Reactive;
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

    /// <summary>
    /// How thick to draw the silhouette, and how much air to leave round it. Both follow
    /// the tile.
    ///
    /// A Path with Stretch="Uniform" scales its geometry and not its stroke, so one
    /// thickness that reads correctly in a 36-pixel tray row becomes a hairline drawn
    /// round a 96-pixel outline in a device's hero — the same drawing looking like two
    /// different sets. The proportions here are the ones the 24-grid was drawn at.
    /// </summary>
    public static readonly StyledProperty<double> GlyphStrokeProperty =
        AvaloniaProperty.Register<DeviceThumb, double>(nameof(GlyphStroke), 1.6);

    public static readonly StyledProperty<Thickness> GlyphMarginProperty =
        AvaloniaProperty.Register<DeviceThumb, Thickness>(nameof(GlyphMargin), new Thickness(3));

    public DeviceThumb()
    {
        InitializeComponent();

        // Bounds rather than Width/Height: the tile is sized by whatever contains it, and
        // several call sites stretch it rather than setting a number.
        this.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(Resize));
    }

    public double GlyphStroke
    {
        get => GetValue(GlyphStrokeProperty);
        set => SetValue(GlyphStrokeProperty, value);
    }

    public Thickness GlyphMargin
    {
        get => GetValue(GlyphMarginProperty);
        set => SetValue(GlyphMarginProperty, value);
    }

    private void Resize(Rect bounds)
    {
        double side = Math.Min(bounds.Width, bounds.Height);
        if (side <= 0) return;

        // 24 is the grid the glyphs are drawn on; 1.5 is their weight on it. The floor
        // keeps a 16-pixel rail icon from thinning into nothing.
        GlyphStroke = Math.Max(1.15, side / 24 * 1.5);

        // Enough air that the outline is a picture of a device rather than a box the
        // device is jammed into. The artboards leave about a sixth of the tile.
        GlyphMargin = new Thickness(Math.Round(side * 0.16));
    }

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
