using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Kvazr.App.Branding;

/// <summary>
/// The application tile: the mark on a rounded square, for the installer, the shortcut
/// and the taskbar.
///
/// The tile is not the mark with a background behind it. The orbit is squeezed from 26
/// to 20 so the glyph keeps a margin inside the rounded corner, which is why
/// <see cref="KvazrMark.Tile"/> exists as a second set of metrics rather than a scale
/// factor applied at the call site.
/// </summary>
internal static class KvazrAppIcon
{
    /// <summary>
    /// The sizes Windows asks for. 16 is the floor the identity sets and also what
    /// Explorer uses in a list view, so it is the one that has to be looked at.
    /// </summary>
    public static int[] Sizes { get; } = [128, 48, 32, 16];

    /// <summary>Renders one tile. <paramref name="surface"/> picks the dark or the light tile.</summary>
    public static RenderTargetBitmap Render(int size, KvazrSurface surface)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, KvazrMark.MinimumPixels);

        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            double scale = size / KvazrMark.ViewBox;
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                Draw(context, surface, KvazrMark.ViewBox / size);
            }
        }

        return bitmap;
    }

    /// <summary>Draws the tile in the mark's own 64-unit coordinates.</summary>
    public static void Draw(DrawingContext context, KvazrSurface surface, double pixel = 0)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Never the accent blue: the core is accent-coloured and vanishes into it.
        // One of the five prohibitions, and the reason there are only two tiles.
        if (surface == KvazrSurface.Dark)
        {
            context.DrawRectangle(
                new SolidColorBrush(KvazrPalettes.SplashBackground),
                null,
                new Rect(0, 0, KvazrMark.ViewBox, KvazrMark.ViewBox),
                14,
                14);
        }
        else
        {
            context.DrawRectangle(
                new SolidColorBrush(KvazrPalettes.TileLight),
                new Pen(new SolidColorBrush(KvazrPalettes.TileLightBorder), 1),
                new Rect(0.5, 0.5, KvazrMark.ViewBox - 1, KvazrMark.ViewBox - 1),
                13.5,
                13.5);
        }

        // The tile is the product, not a status readout, so it is always the resting
        // state. A tile that changed colour with the battery would be a different icon
        // in the taskbar every hour.
        KvazrMark.Draw(
            context,
            KvazrMark.Tile,
            KvazrPalettes.For(KvazrMarkState.AllConnected, surface),
            pixel: pixel);
    }

    /// <summary>
    /// Writes a Windows .ico holding every size in <see cref="Sizes"/>.
    ///
    /// Frames are stored as PNG rather than as the older DIB layout. Windows has
    /// accepted PNG frames at any size since Vista, this application requires 10, and
    /// the DIB layout needs a hand-built AND mask that is one more thing to get wrong
    /// in a file nobody will open in a debugger.
    /// </summary>
    public static void WriteIco(string path, KvazrSurface surface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<byte[]> frames = [];

        foreach (int size in Sizes)
        {
            using RenderTargetBitmap tile = Render(size, surface);
            using var buffer = new MemoryStream();

#pragma warning disable CS0618 // The encoder-options overload is not in this Avalonia
            // version yet, and the default PNG encoding is what an .ico frame wants anyway.
            tile.Save(buffer);
#pragma warning restore CS0618

            frames.Add(buffer.ToArray());
        }

        using FileStream file = File.Create(path);
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)frames.Count);
        file.Write(header);

        int offset = 6 + (16 * frames.Count);
        Span<byte> entry = stackalloc byte[16];

        for (int i = 0; i < frames.Count; i++)
        {
            entry.Clear();

            // 256 is written as 0 in one byte; none of our sizes reach it, but the
            // encoding rule is why this field is a byte and not a short.
            entry[0] = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
            entry[1] = entry[0];
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)frames[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);

            file.Write(entry);
            offset += frames[i].Length;
        }

        foreach (byte[] frame in frames) file.Write(frame);
    }
}
