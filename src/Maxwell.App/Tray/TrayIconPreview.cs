using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Maxwell.App.Branding;

namespace Maxwell.App.Tray;

/// <summary>
/// Dumps every icon variant into one picture so it can be looked at instead of
/// argued about.
///
/// A 16-pixel glyph is the one thing in this application that cannot be judged from
/// source: whether a 55-degree gap survives at that size, or whether two digits read,
/// are questions about pixels, and the only way to answer them is to render and look.
/// Everything here is drawn at 4× for that reason. A previous round of this work
/// shipped a charging dot that covered the digits, and only the picture caught it.
/// </summary>
internal static class TrayIconPreview
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    private static readonly (string Label, KvazrMarkState State)[] States =
    [
        (Maxwell.App.Localization.Localizer.Current.Say("all connected"), KvazrMarkState.AllConnected),
        (Maxwell.App.Localization.Localizer.Current.Say("searching"), KvazrMarkState.Searching),
        (Maxwell.App.Localization.Localizer.Current.Say("firmware update"), KvazrMarkState.FirmwareUpdating),
        (Maxwell.App.Localization.Localizer.Current.Say("no devices"), KvazrMarkState.NoDevices),
        (Maxwell.App.Localization.Localizer.Current.Say("low battery"), KvazrMarkState.LowBattery),
        (Maxwell.App.Localization.Localizer.Current.Say("connection error"), KvazrMarkState.LinkError),
    ];

    /// <summary>Writes the grid to <paramref name="path"/>, magnified so pixels are visible.</summary>
    public static void Dump(string path, int magnify = 4)
    {
        const int gutter = 16;
        const int labelWidth = 190;
        const int rowGap = 14;
        const int captionHeight = 26;

        int cell = Sizes[^1] * magnify;
        int rowHeight = cell + rowGap;

        int width = labelWidth + (Sizes.Length * (cell + gutter)) + gutter;

        // Two panels for the mark, one row of tiles, and a caption above each block.
        int height = (States.Length * rowHeight * 2)
            + (KvazrAppIcon.Sizes.Length > 0 ? rowHeight * 2 : 0)
            + (captionHeight * 4)
            + 80;

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            int y = 10;

            foreach (TrayPanel panel in new[] { TrayPanel.Dark, TrayPanel.Light })
            {
                y = MarkBlock(context, panel, width, y, cell, rowHeight, labelWidth, gutter, magnify, captionHeight);
            }

            TileBlock(context, width, y, cell, rowHeight, labelWidth, gutter, magnify, captionHeight);
        }

#pragma warning disable CS0618 // The encoder-options overload is not in this Avalonia
        // version yet, and this is a development-only dump.
        bitmap.Save(path);
#pragma warning restore CS0618
    }

    private static int MarkBlock(
        DrawingContext context,
        TrayPanel panel,
        int width,
        int y,
        int cell,
        int rowHeight,
        int labelWidth,
        int gutter,
        int magnify,
        int captionHeight)
    {
        (Color background, Color ink) = Shades(panel);

        int blockHeight = (States.Length * rowHeight) + captionHeight + 16;
        context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, y, width, blockHeight));

        Text(context, $"MARK · {(panel == TrayPanel.Dark ? "dark panel" : "light panel")}",
            new Point(gutter, y + 8), ink, 13);

        int rowY = y + captionHeight + 8;

        foreach ((string label, KvazrMarkState state) in States)
        {
            Text(context, label, new Point(gutter, rowY + (cell / 2.0) - 8), ink, 13);

            int x = labelWidth;

            foreach (int size in Sizes)
            {
                using Bitmap icon = TrayIconRenderer.DrawMark(state, size, panel);

                double drawn = size * magnify;
                double offset = (cell - drawn) / 2;

                context.DrawImage(icon, new Rect(x + offset, rowY + offset, drawn, drawn));
                x += cell + gutter;
            }

            rowY += rowHeight;
        }

        return y + blockHeight + 10;
    }

    /// <summary>
    /// The application tile at the four sizes Windows asks for, on a mid grey so
    /// neither tile is flattered by the background it is judged on.
    /// </summary>
    private static void TileBlock(
        DrawingContext context,
        int width,
        int y,
        int cell,
        int rowHeight,
        int labelWidth,
        int gutter,
        int magnify,
        int captionHeight)
    {
        var background = Color.FromRgb(0x80, 0x80, 0x80);
        Color ink = Colors.White;

        int blockHeight = (2 * rowHeight) + captionHeight + 16;
        context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, y, width, blockHeight));

        Text(context, Maxwell.App.Localization.Localizer.Current.Say("TILE · 128 / 48 / 32 / 16"), new Point(gutter, y + 8), ink, 13);

        int rowY = y + captionHeight + 8;

        foreach (KvazrSurface surface in new[] { KvazrSurface.Dark, KvazrSurface.Light })
        {
            Text(context, surface == KvazrSurface.Dark ? Maxwell.App.Localization.Localizer.Current.Say("dark tile") : Maxwell.App.Localization.Localizer.Current.Say("light tile"),
                new Point(gutter, rowY + (cell / 2.0) - 8), ink, 13);

            int x = labelWidth;

            foreach (int size in KvazrAppIcon.Sizes)
            {
                using RenderTargetBitmap tile = KvazrAppIcon.Render(size, surface);

                // 128 at 4× would not fit the sheet, so the big tile is shown at 1:1 and
                // capped to the cell; the small ones are the ones that need magnifying.
                double drawn = Math.Min(size * magnify, cell);
                double offset = (cell - drawn) / 2;

                context.DrawImage(tile, new Rect(x + offset, rowY + offset, drawn, drawn));
                x += cell + gutter;
            }

            rowY += rowHeight;
        }
    }

    private static (Color Background, Color Ink) Shades(TrayPanel panel) => panel == TrayPanel.Dark
        ? (Color.FromRgb(0x20, 0x20, 0x20), Colors.White)
        : (Color.FromRgb(0xF3, 0xF3, 0xF3), Color.FromRgb(0x1B, 0x1B, 0x1B));

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
