using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Maxwell.Core.Devices;

namespace Maxwell.App.Imaging;

/// <summary>
/// Renders every device tile — the bundled photographs and the class silhouettes — into
/// one picture, on both a light and a dark ground.
///
/// It renders offscreen rather than capturing a window, and that is the whole point.
/// Screen capture asks Windows to bring a background process to the foreground, which
/// Windows refuses; what comes back is whatever the person at the keyboard had on screen
/// at those coordinates. Rendering to a bitmap needs no window, no focus, and no part of
/// anyone else's desktop.
/// </summary>
internal static class DeviceImagePreview
{
    private const int Cell = 96;
    private const int Gap = 10;
    private const int LabelHeight = 18;

    public static void Dump(string path)
    {
        List<(string Label, Bitmap? Photo, Geometry? Glyph)> tiles =
        [
            .. Photographs().Select(p => (p.Key, (Bitmap?)p.Value, (Geometry?)null)),
            .. Enum.GetValues<DeviceClass>()
                .Select(c => (c.ToString(), (Bitmap?)null, DeviceImages.Glyph(c))),
        ];

        int columns = Math.Min(tiles.Count, 8);
        int rows = (tiles.Count + columns - 1) / columns;
        int bandHeight = (rows * (Cell + LabelHeight + Gap)) + Gap;

        var size = new PixelSize(
            (columns * (Cell + Gap)) + Gap,
            bandHeight * 2);

        var target = new RenderTargetBitmap(size, new Vector(96, 96));

        using (DrawingContext context = target.CreateDrawingContext())
        {
            // Light ground on top, dark below. A silhouette that reads on one and
            // disappears on the other is a common way to get this wrong, and the only
            // way to see it is to put the two next to each other.
            Draw(context, tiles, columns, offsetY: 0, Colors.White, Color.FromRgb(0x5C, 0x5C, 0x5C));
            Draw(context, tiles, columns, offsetY: bandHeight,
                Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xA8, 0xBF, 0xD6));
        }

        using FileStream file = File.Create(path);

#pragma warning disable CS0618 // The encoder-options overload is not in this Avalonia
        target.Save(file);
#pragma warning restore CS0618
    }

    private static void Draw(
        DrawingContext context,
        List<(string Label, Bitmap? Photo, Geometry? Glyph)> tiles,
        int columns,
        int offsetY,
        Color ground,
        Color ink)
    {
        int bandHeight = (((tiles.Count + columns - 1) / columns) * (Cell + LabelHeight + Gap)) + Gap;

        context.FillRectangle(
            new SolidColorBrush(ground),
            new Rect(0, offsetY, (columns * (Cell + Gap)) + Gap, bandHeight));

        var inkBrush = new SolidColorBrush(ink);
        var pen = new Pen(inkBrush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var typeface = new Typeface("Segoe UI");

        for (int i = 0; i < tiles.Count; i++)
        {
            (string label, Bitmap? photo, Geometry? glyph) = tiles[i];

            double x = Gap + ((i % columns) * (Cell + Gap));
            double y = offsetY + Gap + ((i / columns) * (Cell + LabelHeight + Gap));

            if (photo is not null)
            {
                // The light plate the control draws under a photograph, reproduced here
                // so the preview shows what the interface shows rather than a black
                // headset dissolving into a black background.
                context.FillRectangle(Brushes.WhiteSmoke, new Rect(x, y, Cell, Cell));
                context.DrawImage(photo, new Rect(x + 3, y + 3, Cell - 6, Cell - 6));
            }
            else if (glyph is not null)
            {
                // The glyphs are authored on a 24 x 24 grid, so they are scaled here the
                // same way the control scales them rather than drawn at their own size.
                const double source = 24;
                double scale = (Cell - 24) / source;

                using (context.PushTransform(
                    Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x + 12, y + 12)))
                {
                    context.DrawGeometry(null, pen, glyph);
                }
            }

            context.DrawText(
                new FormattedText(
                    label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, inkBrush),
                new Point(x, y + Cell + 2));
        }
    }

    /// <summary>Every photograph shipped in the build, keyed by its catalogue slug.</summary>
    private static IEnumerable<KeyValuePair<string, Bitmap>> Photographs()
    {
        foreach (string key in DeviceImageCatalogue.BundledKeys)
        {
            if (DeviceImages.Photo(key) is { } bitmap) yield return new(key, bitmap);
        }
    }
}
