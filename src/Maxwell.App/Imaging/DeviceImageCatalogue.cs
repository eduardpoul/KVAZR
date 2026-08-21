namespace Maxwell.App.Imaging;

/// <summary>
/// The models we ship a photograph for.
///
/// A list rather than a directory scan, because the build embeds assets and there is no
/// directory to scan at runtime. It is short and it is meant to stay short: the moment
/// this needs hundreds of entries it stops being a list in the binary and becomes the
/// catalogue repository, fetched over the network like the keyboard definitions.
///
/// Where each picture came from and under what terms is recorded beside the files, in
/// Assets/Devices/images.json.
/// </summary>
public static class DeviceImageCatalogue
{
    public static readonly string[] BundledKeys =
    [
        "audeze-maxwell",
        "logitech-g-pro-x-superlight-2",
        "hyperx-quadcast-s",
        "nuphy-air75-v2",
        "nuphy-halo75-v2",
        "flydigi-apex-5",
    ];
}
