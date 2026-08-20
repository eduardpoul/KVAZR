using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Maxwell.App.Tray;

/// <summary>
/// Dumps every tray-icon variant into one picture so it can be looked at instead of
/// argued about.
///
/// A 16-pixel glyph is the one thing in this application that cannot be judged from
/// source: whether two digits read at that size is a question about pixels, and the
/// only way to answer it is to render them and look. Artboard 4b exists for the same
/// reason, at 4× magnification.
/// </summary>
internal static class TrayIconPreview
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    private static readonly (string Label, int Percent, bool Charging)[] Cases =
    [
        ("обычное", 62, false),
        ("низкий заряд", 9, false),
        ("полный", 100, false),
        ("зарядка", 62, true),
        ("нет данных", -1, false),
    ];

    /// <summary>Writes the grid to <paramref name="path"/>, magnified so pixels are visible.</summary>
    public static void Dump(string path, int magnify = 4)
    {
        const int gutter = 16;
        const int labelWidth = 150;
        const int rowGap = 14;

        int cell = Sizes[^1] * magnify;
        int rowHeight = cell + rowGap;

        int width = labelWidth + (Sizes.Length * (cell + gutter)) + gutter;
        int height = (Cases.Length * rowHeight * 2) + 60;

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            // Two backgrounds, because the whole question is whether the glyph survives
            // the taskbar it lands on.
            int y = 10;

            foreach (TrayPanel panel in new[] { TrayPanel.Dark, TrayPanel.Light })
            {
                Color background = panel == TrayPanel.Dark
                    ? Color.FromRgb(0x20, 0x20, 0x20)
                    : Color.FromRgb(0xF3, 0xF3, 0xF3);

                Color ink = panel == TrayPanel.Dark ? Colors.White : Color.FromRgb(0x1B, 0x1B, 0x1B);

                int blockHeight = (Cases.Length * rowHeight) + 16;
                context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, y, width, blockHeight));

                int rowY = y + 8;

                foreach ((string label, int percent, bool charging) in Cases)
                {
                    Text(context, label, new Point(gutter, rowY + (cell / 2.0) - 8), ink, 13);

                    int x = labelWidth;

                    foreach (int size in Sizes)
                    {
                        using Bitmap icon = TrayIconRenderer.Draw(percent, charging, size, panel);

                        double drawn = size * magnify;
                        double offset = (cell - drawn) / 2;

                        context.DrawImage(icon, new Rect(x + offset, rowY + offset, drawn, drawn));
                        x += cell + gutter;
                    }

                    rowY += rowHeight;
                }

                y += blockHeight + 10;
            }
        }

#pragma warning disable CS0618 // The encoder-options overload is not in this Avalonia
        // version yet, and this is a development-only dump.
        bitmap.Save(path);
#pragma warning restore CS0618
    }

    private static void Text(DrawingContext context, string value, Point at, Color colour, double size) =>
        context.DrawText(
            new FormattedText(
                value,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable Text, Segoe UI, sans-serif"),
                size,
                new SolidColorBrush(colour)),
            at);
}
