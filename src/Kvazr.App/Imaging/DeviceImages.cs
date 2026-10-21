using System;
using System.Collections.Generic;
using System.IO;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Kvazr.Core.Devices;

namespace Kvazr.App.Imaging;

/// <summary>
/// Decides what picture stands for a device, in three steps that mirror how everything
/// else here degrades: what we curated, then what the class implies, then what the user
/// chose themselves.
///
/// The middle step is the important one and it is not an apology. A device row is 56 px
/// tall and the tile inside it is under forty; at that size a good silhouette reads
/// better than a photograph, and a list where one row is a studio render and the next is
/// somebody's snapshot on a desk looks broken in a way eight identical glyphs never do.
/// Photographs earn their keep on the device page and nowhere else.
///
/// The catalogue key is a model slug rather than a product id on purpose. Bluetooth
/// devices have no product ids at all, and a model that ships under several of them is
/// still one model to the person holding it.
/// </summary>
public static class DeviceImages
{
    private static readonly Dictionary<string, Bitmap?> Photos = new(StringComparer.Ordinal);
    private static readonly Dictionary<DeviceClass, Geometry?> Glyphs = [];

    /// <summary>
    /// The curated photograph for this model, or null when there is none — which is the
    /// ordinary case and not a failure. Most devices will never have one.
    /// </summary>
    public static Bitmap? Photo(string? imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey)) return null;

        lock (Photos)
        {
            if (Photos.TryGetValue(imageKey, out Bitmap? cached)) return cached;

            Bitmap? bitmap = Load(imageKey);
            Photos[imageKey] = bitmap;
            return bitmap;
        }
    }

    /// <summary>
    /// The silhouette for a class of device. Every class has one, including
    /// <see cref="DeviceClass.Unknown"/> — a device that says nothing about itself still
    /// gets a row, and an empty tile there reads as a rendering fault rather than as a
    /// device we know nothing about.
    /// </summary>
    public static Geometry? Glyph(DeviceClass deviceClass)
    {
        lock (Glyphs)
        {
            if (Glyphs.TryGetValue(deviceClass, out Geometry? cached)) return cached;

            Geometry? geometry =
                Application.Current?.Resources.TryGetResource($"Glyph{deviceClass}", null, out object? found) == true
                    ? found as Geometry
                    : null;

            Glyphs[deviceClass] = geometry;
            return geometry;
        }
    }

    private static Bitmap? Load(string imageKey)
    {
        // WebP first because that is what the bundled set is, JPEG second because the
        // Maxwell shots predate the set and may still be referenced by an older key.
        foreach (string extension in (string[])["webp", "jpg", "png"])
        {
            try
            {
                var uri = new Uri($"avares://KVAZR/Assets/Devices/{imageKey}.{extension}");
                if (!AssetLoader.Exists(uri)) continue;

                using Stream stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch (FileNotFoundException)
            {
                // Not this extension. Try the next.
            }
        }

        return null;
    }
}
