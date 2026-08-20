using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Win32;

namespace Maxwell.App.Tray;

/// <summary>Which taskbar the icon has to survive being drawn on.</summary>
public enum TrayPanel
{
    Dark,
    Light,
}

/// <summary>
/// Draws the tray icon at runtime with the battery percentage inside it.
///
/// This is the point of the whole tray presence — a static glyph would make the user
/// open a window to learn the one number they care about. Every rule below is from
/// artboard 4a and exists because of the 16-pixel case:
///
/// <list type="bullet">
///   <item>No per-cent sign. Two digits is all that reads, and a third glyph costs both.</item>
///   <item>100 is a filled plate with no digits, because three digits do not fit at all.</item>
///   <item>Charging is a dot in the corner, so the number survives.</item>
///   <item>No reading is an outlined plate with a dash — different in kind from a low
///   reading, not merely different in colour.</item>
///   <item>The plate is filled and the digits reversed out, so the glyph carries its own
///   contrast instead of depending on what the taskbar is doing.</item>
/// </list>
/// </summary>
internal static class TrayIconRenderer
{
    private const int Full = 100;

    /// <summary>Below this the plate turns amber, as the design shows at 9%.</summary>
    private const int LowThreshold = 20;

    private static readonly Typeface Face = new(
        new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
        FontStyle.Normal,
        FontWeight.SemiBold);

    public static WindowIcon Render(int percent, bool charging) =>
        new(Draw(percent, charging, 32, PanelOfSystem()));

    /// <summary>Renders one variant. Public so the whole grid can be dumped and looked at.</summary>
    public static Bitmap Draw(int percent, bool charging, int size, TrayPanel panel)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            bool known = percent >= 0;
            bool low = known && percent < LowThreshold;

            // Radius tracks the size: 3 at 16 px through 6 at 32, from the artboard.
            double radius = Math.Round(size * 0.1875);
            var plate = new Rect(0, 0, size, size);

            if (!known)
            {
                double stroke = Math.Max(1, size / 16.0);

                context.DrawRectangle(
                    null,
                    new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), stroke),
                    plate.Deflate(stroke / 2),
                    radius,
                    radius);

                DrawDash(context, size);
                return bitmap;
            }

            (Color fill, Color ink) = low
                ? (Color.FromRgb(0xFC, 0xE1, 0x00), Color.FromRgb(0x30, 0x2B, 0x00))
                : panel == TrayPanel.Dark
                    ? (Color.FromRgb(0x60, 0xCD, 0xFF), Color.FromRgb(0x00, 0x2E, 0x43))
                    : (Color.FromRgb(0x0F, 0x6C, 0xBD), Colors.White);

            context.DrawRectangle(new SolidColorBrush(fill), null, plate, radius, radius);

            if (percent < Full) DrawDigits(context, percent, size, ink, charging);

            if (charging) DrawChargingDot(context, size, panel);
        }

        return bitmap;
    }

    private static void DrawDigits(DrawingContext context, int percent, int size, Color ink, bool charging)
    {
        // The charging dot takes the top-right corner, so the number shrinks a little and
        // sits a little lower rather than being overlapped. Losing the digit would defeat
        // the entire point of drawing one.
        double scale = charging ? 0.54 : 0.62;
        double drop = charging ? size * 0.06 : 0;

        var text = new FormattedText(
            percent.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Face,
            size * scale,
            new SolidColorBrush(ink));

        context.DrawText(text, new Point((size - text.Width) / 2, ((size - text.Height) / 2) + drop));
    }

    private static void DrawDash(DrawingContext context, int size)
    {
        double y = size / 2.0;
        double inset = size * 0.3;

        context.DrawLine(
            new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)), Math.Max(1, size / 12.0)),
            new Point(inset, y),
            new Point(size - inset, y));
    }

    /// <summary>
    /// A dot in the corner rather than a bolt across the plate, because the number has to
    /// survive. Ringed in the panel colour so it still reads as separate at 16 px.
    /// </summary>
    private static void DrawChargingDot(DrawingContext context, int size, TrayPanel panel)
    {
        double r = size * 0.155;
        var centre = new Point(size - r - (size * 0.02), r + (size * 0.02));

        Color ring = panel == TrayPanel.Dark
            ? Color.FromRgb(0x20, 0x20, 0x20)
            : Color.FromRgb(0xF3, 0xF3, 0xF3);

        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F)),
            new Pen(new SolidColorBrush(ring), Math.Max(1, size / 16.0)),
            centre,
            r,
            r);
    }

    /// <summary>
    /// Which taskbar we are drawing on. Windows keeps this separate from the application
    /// theme, and it is the taskbar that decides whether our glyph disappears into it.
    /// </summary>
    private static TrayPanel PanelOfSystem()
    {
        // Only Windows keeps the taskbar theme in the registry. Elsewhere the dark
        // assumption is the safer default: light digits on a dark plate survive a light
        // panel better than the reverse.
        if (!OperatingSystem.IsWindows()) return TrayPanel.Dark;

        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme",
                0);

            return value is int light && light != 0 ? TrayPanel.Light : TrayPanel.Dark;
        }
        catch (System.Security.SecurityException)
        {
            return TrayPanel.Dark;
        }
    }
}
