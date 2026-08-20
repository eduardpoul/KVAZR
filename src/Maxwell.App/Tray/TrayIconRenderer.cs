using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls;

namespace Maxwell.App.Tray;

/// <summary>
/// Draws the tray icon at runtime with the battery percentage inside it, which is the
/// point of the whole tray presence — a static glyph would make the user open a window
/// to learn the one number they care about.
/// </summary>
internal static class TrayIconRenderer
{
    private const int Size = 32;

    private static readonly Typeface Face = new(
        new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
        FontStyle.Normal,
        FontWeight.SemiBold);

    /// <summary>
    /// Renders a rounded badge showing <paramref name="percent"/>, or a dash when the
    /// battery is unknown. Colour shifts to the warning and danger tones as it drains,
    /// so the state reads at a glance without squinting at the digits.
    /// </summary>
    public static WindowIcon Render(int percent, bool charging)
    {
        var background = percent switch
        {
            < 0 => Color.FromRgb(0x8A, 0x8A, 0x8A),
            <= 15 => Color.FromRgb(0xC4, 0x2B, 0x1C),
            <= 30 => Color.FromRgb(0x98, 0x6F, 0x0B),
            _ => Color.FromRgb(0x0F, 0x6C, 0xBD),
        };

        if (charging) background = Color.FromRgb(0x0F, 0x7B, 0x0F);

        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(
                new SolidColorBrush(background),
                null,
                new Rect(0, 0, Size, Size),
                7, 7);

            string label = percent < 0 ? "–" : percent.ToString(CultureInfo.InvariantCulture);

            // Three digits only happen at 100%, where the badge is too small for them —
            // "99+" would lie, so a full battery drops the digits entirely.
            if (percent >= 100) label = "•";

            var text = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Face,
                label.Length > 2 ? 14 : 17,
                Brushes.White);

            context.DrawText(text, new Point(
                (Size - text.Width) / 2,
                (Size - text.Height) / 2));
        }

        return new WindowIcon(bitmap);
    }
}
