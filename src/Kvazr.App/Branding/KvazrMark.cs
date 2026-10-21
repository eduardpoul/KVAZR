using Avalonia;
using Avalonia.Media;

namespace Kvazr.App.Branding;

/// <summary>
/// The KVAZR mark: an orbit with a gap, a heavy core, and a planet standing on the
/// edge of the gap.
///
/// This type is the only place in the application that knows the shape. Everything
/// that draws the mark — the tray icon, the window icon, the splash, the loaders —
/// asks for the same geometry and scales it whole, which is the whole point of the
/// identity: the 16-pixel tray glyph and the 132-pixel splash are the same object at
/// two sizes, not two drawings that happen to resemble each other.
///
/// The numbers come from <c>Design/kvazr/KVAZR Identity.dc.html</c> and are not
/// approximations of it. The canvas ships the arc as the SVG path
/// <c>M32 6 A26 26 0 1 1 10.7 17.09</c> with the planet at <c>(20, 8.94)</c>; both fall
/// out of <see cref="Sweep"/> and <see cref="Gap"/> below to within a rounding step, so
/// the shape is derived here rather than transcribed, and stays correct at the tile's
/// smaller radius where the canvas quotes different literals.
/// </summary>
internal static class KvazrMark
{
    /// <summary>The canvas the mark is authored on. Every dimension below is in these units.</summary>
    public const double ViewBox = 64;

    /// <summary>Centre of the orbit, the core and the whole design.</summary>
    public const double Centre = ViewBox / 2;

    /// <summary>
    /// Degrees of orbit that are actually drawn, clockwise from twelve o'clock. The
    /// remainder is the gap, and the identity forbids changing either.
    /// </summary>
    public const double Sweep = 305;

    /// <summary>The opening in the orbit. 305 + 55 = 360.</summary>
    public const double Gap = 360 - Sweep;

    /// <summary>
    /// Where the planet sits in static use: the middle of the gap, on the orbit. The
    /// identity forbids lifting it off the orbit anywhere but in motion.
    /// </summary>
    public const double PlanetAngle = 360 - (Gap / 2);

    /// <summary>
    /// Below this the three-unit orbit falls into subpixels and the mark stops being a
    /// mark. One of the five prohibitions, enforced rather than documented.
    /// </summary>
    public const int MinimumPixels = 16;

    /// <summary>The mark as it stands on its own: on a splash, in a loader, in the tray.</summary>
    public static KvazrMarkMetrics Standalone { get; } = new(26, 3, 11, 5);

    /// <summary>
    /// The mark squeezed into an installer tile. The orbit drops from 26 to 20 so the
    /// tile keeps a margin, and the stroke is deliberately NOT scaled with it — a
    /// proportional 2.31 disappears against the tile's fill at 16 px, so the identity
    /// quotes 2.6 and this follows it.
    /// </summary>
    public static KvazrMarkMetrics Tile { get; } = new(20, 2.6, 8.5, 3.9);

    /// <summary>
    /// Draws the mark into <paramref name="context"/>, in the mark's own 64-unit
    /// coordinates. Callers scale, they do not re-lay-out: pass a transform for the
    /// size you want and let this draw once.
    /// </summary>
    /// <param name="rotation">
    /// Degrees to spin the orbit-and-planet assembly about the centre. The core stays
    /// put — it is the thing being orbited.
    /// </param>
    /// <param name="planetScale">
    /// Multiplier on the planet's radius, for the pulse. It grows about its own centre
    /// and stays on the orbit, so the static rule is not broken by the motion.
    /// </param>
    /// <param name="pixel">
    /// The size of one device pixel in mark units, or zero when the caller does not
    /// know. See <see cref="Thickness"/> for what it is for.
    /// </param>
    public static void Draw(
        DrawingContext context,
        KvazrMarkMetrics metrics,
        KvazrPalette palette,
        double rotation = 0,
        double coreOpacity = 1,
        double planetScale = 1,
        double pixel = 0)
    {
        ArgumentNullException.ThrowIfNull(context);

        var centre = new Point(Centre, Centre);

        // The core is drawn first and never rotated, so a spinning mark reads as one
        // object moving around a fixed centre rather than as the whole glyph turning.
        using (context.PushOpacity(coreOpacity))
        {
            context.DrawEllipse(new SolidColorBrush(palette.Core), null, centre, metrics.CoreRadius, metrics.CoreRadius);
        }

        using (context.PushTransform(RotationAbout(rotation, centre)))
        {
            context.DrawGeometry(
                null,
                new Pen(new SolidColorBrush(palette.Ring), Thickness(metrics, pixel)) { LineCap = PenLineCap.Round },
                Orbit(metrics));

            Point planet = OnOrbit(metrics.RingRadius, PlanetAngle);
            double radius = metrics.PlanetRadius * planetScale;
            context.DrawEllipse(new SolidColorBrush(palette.Planet), null, planet, radius, radius);
        }
    }

    /// <summary>
    /// Draws the determinate form: a closed track with the orbit filling it clockwise
    /// from twelve o'clock. The planet is gone here on purpose — with the orbit
    /// closing, an object standing at the gap would be standing on the part that is
    /// about to arrive.
    /// </summary>
    public static void DrawProgress(
        DrawingContext context,
        KvazrMarkMetrics metrics,
        KvazrPalette palette,
        Color track,
        double fraction,
        double pixel = 0)
    {
        ArgumentNullException.ThrowIfNull(context);

        var centre = new Point(Centre, Centre);
        double clamped = Math.Clamp(fraction, 0, 1);
        double thickness = Thickness(metrics, pixel);

        context.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(track), thickness),
            centre,
            metrics.RingRadius,
            metrics.RingRadius);

        // A round cap on a zero-length arc still paints a dot, which reads as progress
        // where there is none. Below half a degree there is nothing honest to draw.
        if (clamped * 360 >= 0.5)
        {
            context.DrawGeometry(
                null,
                new Pen(new SolidColorBrush(palette.Planet), thickness) { LineCap = PenLineCap.Round },
                Arc(metrics.RingRadius, clamped * 360));
        }

        context.DrawEllipse(new SolidColorBrush(palette.Core), null, centre, metrics.CoreRadius, metrics.CoreRadius);
    }

    /// <summary>The gapped orbit, as a geometry. Cached per metrics — there are two.</summary>
    public static Geometry Orbit(KvazrMarkMetrics metrics) => metrics.OrbitGeometry;

    /// <summary>
    /// The orbit's stroke, never thinner than one device pixel.
    ///
    /// The identity puts the floor at 16 px with the reason attached: below it the
    /// three-unit orbit goes subpixel. At 16 the proportional stroke is 0.75 of a
    /// pixel and the tile's is 0.65, which antialiasing turns into a grey smear —
    /// exactly the failure the floor exists to prevent, one size too early. Holding
    /// the stroke at a whole pixel keeps the orbit an orbit; it is why the identity
    /// already refuses to scale the tile's stroke proportionally, applied one step
    /// further down.
    /// </summary>
    private static double Thickness(KvazrMarkMetrics metrics, double pixel) =>
        pixel > 0 ? Math.Max(metrics.RingThickness, pixel) : metrics.RingThickness;

    private static Matrix RotationAbout(double degrees, Point pivot) =>
        Matrix.CreateTranslation(-pivot.X, -pivot.Y)
        * Matrix.CreateRotation(degrees * Math.PI / 180)
        * Matrix.CreateTranslation(pivot.X, pivot.Y);

    /// <summary>An arc of <paramref name="degrees"/> clockwise from twelve o'clock.</summary>
    internal static Geometry Arc(double radius, double degrees)
    {
        // A full turn has no end point distinct from its start, and an arc segment
        // cannot express one. Two half turns can.
        if (degrees >= 360)
        {
            return new EllipseGeometry(new Rect(
                Centre - radius, Centre - radius, radius * 2, radius * 2));
        }

        var geometry = new StreamGeometry();

        using (StreamGeometryContext pen = geometry.Open())
        {
            pen.BeginFigure(OnOrbit(radius, 0), isFilled: false);
            pen.ArcTo(
                OnOrbit(radius, degrees),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: degrees > 180,
                sweepDirection: SweepDirection.Clockwise);
            pen.EndFigure(isClosed: false);
        }

        return geometry;
    }

    /// <summary>
    /// A point on the orbit, measured in degrees clockwise from twelve o'clock —
    /// the same convention the identity states its gap in, so the numbers in the
    /// document and the numbers here mean the same thing.
    /// </summary>
    internal static Point OnOrbit(double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(Centre + (radius * Math.Sin(radians)), Centre - (radius * Math.Cos(radians)));
    }
}

/// <summary>
/// One set of dimensions for the mark. Two exist — <see cref="KvazrMark.Standalone"/>
/// and <see cref="KvazrMark.Tile"/> — and no code outside this file invents a third.
/// </summary>
internal sealed class KvazrMarkMetrics
{
    private Geometry? _orbit;

    public KvazrMarkMetrics(double ringRadius, double ringThickness, double coreRadius, double planetRadius)
    {
        RingRadius = ringRadius;
        RingThickness = ringThickness;
        CoreRadius = coreRadius;
        PlanetRadius = planetRadius;
    }

    public double RingRadius { get; }

    public double RingThickness { get; }

    public double CoreRadius { get; }

    public double PlanetRadius { get; }

    /// <summary>
    /// The gapped orbit. Built once: the tray redraws this on every battery poll, and
    /// rebuilding a geometry to draw the identical curve is work nobody asked for.
    /// </summary>
    internal Geometry OrbitGeometry => _orbit ??= KvazrMark.Arc(RingRadius, KvazrMark.Sweep);
}
