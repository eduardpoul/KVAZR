using Avalonia.Media;

namespace Kvazr.App.Branding;

/// <summary>
/// What the mark is saying. These are simultaneously the states of the mark and the
/// states of the tray icon — the identity treats them as one list, because a person
/// glancing at the notification area and a person looking at a splash are asking the
/// same question.
/// </summary>
public enum KvazrMarkState
{
    /// <summary>Everything attached answers. Planet in the accent, nothing moving.</summary>
    AllConnected,

    /// <summary>Discovery is running. The assembly turns and the planet is muted.</summary>
    Searching,

    /// <summary>A firmware image is going onto a device. Planet violet.</summary>
    FirmwareUpdating,

    /// <summary>Nothing found. The whole mark goes grey — not red; this is not a fault.</summary>
    NoDevices,

    /// <summary>Something attached is running out. Planet amber, core untouched.</summary>
    LowBattery,

    /// <summary>
    /// A device is present but does not answer. Planet red and the rotation stops,
    /// because a spinning mark says "working on it" and this is the opposite.
    /// </summary>
    LinkError,
}

/// <summary>
/// Which background the mark has to survive. Not the application theme: the taskbar
/// keeps its own, and it is the taskbar that decides whether the glyph disappears.
/// </summary>
public enum KvazrSurface
{
    Light,
    Dark,
}

/// <summary>The three colours one drawing of the mark needs.</summary>
internal readonly record struct KvazrPalette(Color Ring, Color Core, Color Planet);

/// <summary>
/// The state-to-colour table, transcribed from the <c>states</c> array in
/// <c>Design/kvazr/KVAZR Identity.dc.html</c>.
///
/// Nothing here is derived, blended or chosen. The identity picks these six planet
/// colours precisely so that the states stay distinguishable at 16 px, where hue is
/// the only channel left — amber against violet against red is a decision about
/// legibility, and re-deriving any of them from the accent would undo it.
/// </summary>
internal static class KvazrPalettes
{
    /// <summary>The accent, on a light surface. Core and planet at rest.</summary>
    public static Color Accent { get; } = Color.FromRgb(0x0F, 0x6C, 0xBD);

    /// <summary>The accent, on a dark surface.</summary>
    public static Color AccentDark { get; } = Color.FromRgb(0x60, 0xCD, 0xFF);

    /// <summary>The orbit on a light surface.</summary>
    public static Color Orbit { get; } = Color.FromRgb(0x16, 0x32, 0x4D);

    /// <summary>The orbit on a dark surface, and the colour every skeleton is drawn in there.</summary>
    public static Color OrbitOnDark { get; } = Color.FromRgb(0xA8, 0xBF, 0xD6);

    /// <summary>The orbit at rest — used where a full-strength orbit would shout, such as a loader.</summary>
    public static Color OrbitResting { get; } = Color.FromRgb(0xB7, 0xC6, 0xD4);

    /// <summary>The orbit on the splash, which is darker than the mark's own dark orbit.</summary>
    public static Color OrbitOnSplash { get; } = Color.FromRgb(0x2E, 0x4B, 0x68);

    /// <summary>The splash background, and the fill of the dark application tile.</summary>
    public static Color SplashBackground { get; } = Color.FromRgb(0x0B, 0x14, 0x20);

    /// <summary>The light application tile: fill and hairline.</summary>
    public static Color TileLight { get; } = Color.FromRgb(0xF4, 0xF7, 0xFA);

    public static Color TileLightBorder { get; } = Color.FromRgb(0xDC, 0xE4, 0xEC);

    /// <summary>The unfilled part of a determinate ring.</summary>
    public static Color ProgressTrack { get; } = Color.FromRgb(0xE4, 0xEA, 0xF0);

    /// <summary>
    /// The mark's colours for one state on one surface. Every caller goes through
    /// here; nothing else in the application writes a brand hex literal.
    /// </summary>
    public static KvazrPalette For(KvazrMarkState state, KvazrSurface surface) => surface switch
    {
        KvazrSurface.Dark => state switch
        {
            KvazrMarkState.AllConnected => new KvazrPalette(OrbitOnDark, AccentDark, AccentDark),
            KvazrMarkState.Searching => new KvazrPalette(OrbitOnDark, AccentDark, Rgb(0x5E, 0x76, 0x90)),
            KvazrMarkState.FirmwareUpdating => new KvazrPalette(OrbitOnDark, AccentDark, Rgb(0xB3, 0x9C, 0xFF)),
            KvazrMarkState.NoDevices => new KvazrPalette(Rgb(0x5A, 0x6B, 0x7C), Rgb(0x7C, 0x8B, 0x9A), Rgb(0x7C, 0x8B, 0x9A)),
            KvazrMarkState.LowBattery => new KvazrPalette(OrbitOnDark, AccentDark, Rgb(0xE8, 0xA3, 0x3D)),
            KvazrMarkState.LinkError => new KvazrPalette(OrbitOnDark, AccentDark, Rgb(0xF0, 0x62, 0x5A)),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        },
        KvazrSurface.Light => state switch
        {
            KvazrMarkState.AllConnected => new KvazrPalette(Orbit, Accent, Accent),
            KvazrMarkState.Searching => new KvazrPalette(Orbit, Accent, OrbitResting),
            KvazrMarkState.FirmwareUpdating => new KvazrPalette(Orbit, Accent, Rgb(0x7B, 0x4E, 0xC4)),
            KvazrMarkState.NoDevices => new KvazrPalette(Rgb(0x9A, 0xA7, 0xB4), Rgb(0xA8, 0xB4, 0xC0), Rgb(0xA8, 0xB4, 0xC0)),
            KvazrMarkState.LowBattery => new KvazrPalette(Orbit, Accent, Rgb(0xB2, 0x6A, 0x00)),
            KvazrMarkState.LinkError => new KvazrPalette(Orbit, Accent, Rgb(0xC4, 0x2B, 0x1C)),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>
    /// Whether the mark turns in this state. Motion is part of the meaning, not
    /// decoration: searching turns, a broken link deliberately does not.
    /// </summary>
    public static bool Turns(KvazrMarkState state) =>
        state is KvazrMarkState.Searching or KvazrMarkState.FirmwareUpdating;

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
