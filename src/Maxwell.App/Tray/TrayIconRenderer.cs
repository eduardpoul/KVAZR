using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Maxwell.App.Branding;
using Microsoft.Win32;

namespace Maxwell.App.Tray;

/// <summary>Which shade the taskbar is currently using.</summary>
public enum TrayPanel
{
    Dark,
    Light,
}

/// <summary>
/// Draws the mark at the size the notification area asks for.
///
/// It used to draw a battery percentage instead, and that was right while the application
/// was about one headset. With eight devices attached there is no number the icon can
/// show that is not the wrong one most of the time — and choosing between "the primary
/// device" and "whichever is nearest flat" only moves the wrongness around rather than
/// removing it. So the icon says whether anything wants attention, the tooltip carries the
/// numbers, and the percentages live on screens with room for all of them.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>Below this the planet turns amber. The identity's low-battery state.</summary>
    private const int LowThreshold = 20;

    private static readonly Typeface Face = new(
        new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
        FontStyle.Normal,
        FontWeight.SemiBold);

    /// <summary>
    /// Rendered at 32 rather than 16 because Windows scales the notification-area icon
    /// down for the panel it is on, and scaling a 32 down beats drawing a 16 up.
    /// </summary>
    private const int TraySize = 32;
    /// <summary>The mark, in one of its five states. The default tray presence.</summary>
    public static WindowIcon Render(KvazrMarkState state) =>
        new(DrawMark(state, TraySize, PanelOfSystem()));

    /// <summary>Renders one state at one size. Public so the whole grid can be dumped and looked at.</summary>
    public static Bitmap DrawMark(KvazrMarkState state, int size, TrayPanel panel)
    {
        // The identity's floor. Below it the orbit is not an orbit and the gap — the
        // one thing that makes this mark this mark — closes up into a smudge.
        ArgumentOutOfRangeException.ThrowIfLessThan(size, KvazrMark.MinimumPixels);

        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            double scale = size / KvazrMark.ViewBox;

            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                // A tray icon is a still frame. The states that turn on screen are drawn
                // at rest here, and the identity leans on this: searching is legible from
                // its muted planet alone, without the rotation.
                KvazrMark.Draw(
                    context,
                    KvazrMark.Standalone,
                    KvazrPalettes.For(state, Surface(panel)),
                    pixel: KvazrMark.ViewBox / size);
            }
        }

        return bitmap;
    }

    /// <summary>Which of the mark's two surfaces a taskbar of this shade is.</summary>
    public static KvazrSurface Surface(TrayPanel panel) =>
        panel == TrayPanel.Dark ? KvazrSurface.Dark : KvazrSurface.Light;

    /// <summary>The taskbar shade the operating system is currently using.</summary>
    public static TrayPanel Panel() => PanelOfSystem();


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
