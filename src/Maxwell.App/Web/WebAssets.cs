using Avalonia.Platform;

namespace Maxwell.App.Web;

/// <summary>
/// Bundled device photographs, as data URIs the page can put straight into an img.
///
/// Data URIs rather than served files because the photographs live inside the assembly
/// as Avalonia assets, and one source of truth beats teaching the web host to unpack
/// them. Cached — the pages re-render on every reading and the bytes never change.
/// </summary>
public static class WebAssets
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);

    public static string? PhotoDataUri(string? imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey)) return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(imageKey, out string? cached)) return cached;

            string? uri = Load(imageKey);
            Cache[imageKey] = uri;
            return uri;
        }
    }

    private static string? Load(string imageKey)
    {
        foreach ((string extension, string mime) in
                 (ReadOnlySpan<(string, string)>)[("webp", "image/webp"), ("jpg", "image/jpeg"), ("png", "image/png")])
        {
            var uri = new Uri($"avares://Maxwell/Assets/Devices/{imageKey}.{extension}");
            if (!AssetLoader.Exists(uri)) continue;

            using Stream stream = AssetLoader.Open(uri);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return $"data:{mime};base64,{Convert.ToBase64String(memory.ToArray())}";
        }

        return null;
    }
}
