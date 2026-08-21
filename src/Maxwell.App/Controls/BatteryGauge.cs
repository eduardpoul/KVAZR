using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Maxwell.App.Controls;

/// <summary>
/// The little battery beside the percentage in a device row.
///
/// Its measurements come from the artboard rather than from taste: a 26 by 13 shell with
/// a 1.4 stroke, a fill inset by 1.5 on every side, and a nub on the right. Drawn rather
/// than assembled from nested borders because the fill is a fraction of the inner width
/// and a layout panel makes that a calculation in three places instead of one.
///
/// It carries no number. The percentage is already next to it in a size that can be read
/// across a desk, and a gauge that repeats it is two things to keep in agreement.
/// </summary>
public sealed class BatteryGauge : Control
{
    /// <summary>The green the design uses. Not a theme colour: it means "charged", and
    /// that meaning does not change between a light and a dark window.</summary>
    private static readonly IBrush Charged = new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F));

    public static readonly StyledProperty<int?> PercentProperty =
        AvaloniaProperty.Register<BatteryGauge, int?>(nameof(Percent));

    /// <summary>
    /// Below this the fill turns amber. The design never draws a low battery in a row,
    /// so this is not transcribed from it — it follows the threshold the user already
    /// set for notifications, on the grounds that one idea of "low" is better than two.
    /// </summary>
    public static readonly StyledProperty<int> LowThresholdProperty =
        AvaloniaProperty.Register<BatteryGauge, int>(nameof(LowThreshold), 15);

    static BatteryGauge()
    {
        AffectsRender<BatteryGauge>(PercentProperty, LowThresholdProperty);
        AffectsMeasure<BatteryGauge>(PercentProperty);
    }

    public int? Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public int LowThreshold
    {
        get => GetValue(LowThresholdProperty);
        set => SetValue(LowThresholdProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(30, 13);

    public override void Render(DrawingContext context)
    {
        if (Percent is not { } percent) return;

        var ink = Foreground(this);
        var pen = new Pen(ink, 1.4);

        // Stroke sits on the outline, so the rectangle is inset by half of it or the
        // shell renders a pixel wider than the artboard on every side.
        var shell = new Rect(0.7, 0.7, 26 - 1.4, 13 - 1.4);
        context.DrawRectangle(null, pen, new RoundedRect(shell, 3));

        // The nub, on the right, drawn as a small filled round rectangle rather than as
        // part of the outline: at this size an outlined nub is two grey pixels.
        context.DrawRectangle(ink, null, new RoundedRect(new Rect(26.5, 4.5, 2.5, 4), 1));

        double inner = 26 - 3;
        double filled = inner * Math.Clamp(percent, 0, 100) / 100.0;
        if (filled <= 0) return;

        IBrush fill = percent <= LowThreshold
            ? Application.Current?.FindResource("WarningBrush") as IBrush ?? Charged
            : Charged;

        context.DrawRectangle(fill, null, new RoundedRect(new Rect(1.5, 1.5, filled, 13 - 3), 1.5));
    }

    private static IBrush Foreground(Control control) =>
        control.FindResource("TextSecondaryBrush") as IBrush ?? Brushes.Gray;
}
