using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Maxwell.App.Branding;

namespace Maxwell.App.Controls;

/// <summary>
/// Which orbit colour a drawing of the mark uses. The state's own is the default; the
/// other two exist because the identity draws the orbit quieter wherever the mark is a
/// loader rather than a badge, and quieter still on the splash, where a full-strength
/// orbit competes with the wordmark next to it.
/// </summary>
internal enum KvazrOrbitShade
{
    /// <summary>The colour the state itself specifies.</summary>
    State,

    /// <summary>#B7C6D4 — a loader on a light surface.</summary>
    Resting,

    /// <summary>#2E4B68 — the splash, and only the splash.</summary>
    Splash,
}

/// <summary>
/// The KVAZR mark on screen, in any state and any motion.
///
/// One control rather than a control per size or per state: the identity's first rule
/// is that the geometry never changes, so the only thing a caller may vary is how big
/// it is drawn, what it is saying, and whether it is moving. Sizes come from
/// <see cref="Control.Width"/> and <see cref="Control.Height"/> and the drawing is
/// scaled whole from the 64-unit canvas.
/// </summary>
#pragma warning disable CA1001
// The token source is not a resource this control owns for its lifetime: it is created
// when the control enters the visual tree and cancelled and released when it leaves,
// which is the only lifetime an Avalonia control actually has. Nothing ever disposes a
// control, so making one disposable would put a Dispose on the type that no caller
// would ever reach.
internal sealed class KvazrMarkControl : Control
{
    public static readonly StyledProperty<KvazrMarkState> StateProperty =
        AvaloniaProperty.Register<KvazrMarkControl, KvazrMarkState>(nameof(State));

    public static readonly StyledProperty<KvazrMotion> MotionProperty =
        AvaloniaProperty.Register<KvazrMarkControl, KvazrMotion>(nameof(Motion));

    /// <summary>
    /// Which surface the mark is standing on. Left unset it follows the theme, which is
    /// what almost every use wants; set it explicitly where the surface is fixed
    /// regardless of the theme, as on the splash.
    /// </summary>
    public static readonly StyledProperty<KvazrSurface> SurfaceProperty =
        AvaloniaProperty.Register<KvazrMarkControl, KvazrSurface>(nameof(Surface));

    /// <summary>
    /// Determinate progress, 0 to 1, or null for the ordinary mark. Setting it swaps
    /// the drawing to the closed-track form the identity uses for a firmware image
    /// whose size is known.
    /// </summary>
    public static readonly StyledProperty<double?> ProgressProperty =
        AvaloniaProperty.Register<KvazrMarkControl, double?>(nameof(Progress));

    /// <summary>
    /// Which of the identity's three orbit colours to use. Not a colour: the identity
    /// names three and a control that took a brush would grow a fourth.
    /// </summary>
    public static readonly StyledProperty<KvazrOrbitShade> OrbitShadeProperty =
        AvaloniaProperty.Register<KvazrMarkControl, KvazrOrbitShade>(nameof(OrbitShade));

    /// <summary>
    /// The animated angle. Public only because Avalonia's animation engine needs a
    /// property to drive; nothing should set it directly.
    /// </summary>
    private static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<KvazrMarkControl, double>(nameof(Angle));

    private static readonly StyledProperty<double> CoreOpacityProperty =
        AvaloniaProperty.Register<KvazrMarkControl, double>(nameof(CoreOpacity), 1.0);

    private static readonly StyledProperty<double> PlanetScaleProperty =
        AvaloniaProperty.Register<KvazrMarkControl, double>(nameof(PlanetScale), 1.0);

    private CancellationTokenSource? _running;
    private bool _attached;

    public KvazrMarkControl()
    {
        // The palette is chosen at draw time from the theme, so a theme change has to
        // reach the drawing. There is no AffectsRender for this one — the property that
        // carries it is not public — so the event does the same job.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    static KvazrMarkControl()
    {
        AffectsRender<KvazrMarkControl>(
            StateProperty,
            SurfaceProperty,
            ProgressProperty,
            OrbitShadeProperty,
            AngleProperty,
            CoreOpacityProperty,
            PlanetScaleProperty);

        AffectsMeasure<KvazrMarkControl>(WidthProperty, HeightProperty);
    }

    public KvazrMarkState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public KvazrMotion Motion
    {
        get => GetValue(MotionProperty);
        set => SetValue(MotionProperty, value);
    }

    public KvazrSurface Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public double? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public KvazrOrbitShade OrbitShade
    {
        get => GetValue(OrbitShadeProperty);
        set => SetValue(OrbitShadeProperty, value);
    }

    private double Angle => GetValue(AngleProperty);

    private double CoreOpacity => GetValue(CoreOpacityProperty);

    private double PlanetScale => GetValue(PlanetScaleProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        // Square by construction. Stretching the mark along one axis is one of the
        // five prohibitions, so it never takes a non-square box even when offered one.
        double side = double.IsFinite(Width) ? Width
            : double.IsFinite(Height) ? Height
            : Math.Min(availableSize.Width, availableSize.Height);

        if (!double.IsFinite(side) || side <= 0) side = KvazrMark.ViewBox;

        // The 16-pixel floor, enforced rather than written down. A caller asking for
        // twelve gets sixteen and a layout that visibly does not fit, which is a
        // problem someone will notice; a twelve-pixel mark is a problem nobody notices
        // until the product is shipping with a smudge for a logo.
        return new Size(Math.Max(side, KvazrMark.MinimumPixels), Math.Max(side, KvazrMark.MinimumPixels));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double side = Math.Max(Math.Min(finalSize.Width, finalSize.Height), KvazrMark.MinimumPixels);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        double side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;

        KvazrPalette palette = KvazrPalettes.For(State, OnSurface());

        palette = OrbitShade switch
        {
            KvazrOrbitShade.Resting => palette with { Ring = KvazrPalettes.OrbitResting },
            KvazrOrbitShade.Splash => palette with { Ring = KvazrPalettes.OrbitOnSplash },
            _ => palette,
        };

        double pixel = KvazrMark.ViewBox / side;

        using (context.PushTransform(Matrix.CreateScale(side / KvazrMark.ViewBox, side / KvazrMark.ViewBox)))
        {
            if (Progress is { } fraction)
            {
                KvazrMark.DrawProgress(
                    context,
                    KvazrMark.Standalone,
                    palette with { Planet = palette.Core },
                    OnSurface() == KvazrSurface.Dark ? KvazrPalettes.OrbitOnSplash : KvazrPalettes.ProgressTrack,
                    fraction,
                    pixel);
                return;
            }

            KvazrMark.Draw(
                context,
                KvazrMark.Standalone,
                palette,
                Angle,
                CoreOpacity,
                PlanetScale,
                pixel);
        }
    }

    /// <summary>
    /// The surface to draw against: whatever was asked for, or the theme when nothing
    /// was. Deriving it is the difference between one mark that is right everywhere and
    /// a Surface attribute repeated on every use of it, half of them stale.
    /// </summary>
    private KvazrSurface OnSurface() => IsSet(SurfaceProperty)
        ? Surface
        : ActualThemeVariant == ThemeVariant.Dark ? KvazrSurface.Dark : KvazrSurface.Light;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _attached = true;
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _attached = false;
        Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MotionProperty && _attached) Restart();
    }

    /// <summary>
    /// Animations are started from code rather than from a style because the identity
    /// specifies durations to a tenth of a second and a style would put six of them in
    /// six selectors, where they drift.
    /// </summary>
    private void Restart()
    {
        Stop();

        if (Motion == KvazrMotion.Still) return;

        _running = new CancellationTokenSource();
        CancellationToken token = _running.Token;

        foreach (Animation animation in AnimationsFor(Motion))
        {
            // Fire and forget: RunAsync completes when the token is cancelled, which
            // happens on detach. Nothing awaits a loader.
            _ = animation.RunAsync(this, token);
        }
    }

    private void Stop()
    {
        _running?.Cancel();
        _running?.Dispose();
        _running = null;

        SetCurrentValue(AngleProperty, 0d);
        SetCurrentValue(CoreOpacityProperty, 1d);
        SetCurrentValue(PlanetScaleProperty, 1d);
    }

    private static IEnumerable<Animation> AnimationsFor(KvazrMotion motion)
    {
        TimeSpan duration = KvazrTimings.Motion(motion);

        switch (motion)
        {
            case KvazrMotion.Loading:
            case KvazrMotion.Fast:
                yield return Spin(duration);
                break;

            case KvazrMotion.Breathing:
                yield return Spin(duration);
                yield return Cycle(CoreOpacityProperty, 1, 0.55, duration);
                break;

            case KvazrMotion.Pulse:
                // The planet swells to 7.5 units against a resting 5. Expressed as a
                // scale so the number stays tied to the geometry rather than repeated.
                yield return Cycle(PlanetScaleProperty, 1, 7.5 / 5, duration);
                break;

            case KvazrMotion.Landing:
                yield return Landing(duration);
                break;

            case KvazrMotion.Fault:
                yield return Jolt(duration);
                break;

            default:
                break;
        }
    }

    private static Animation Spin(TimeSpan duration) => new()
    {
        Duration = duration,
        IterationCount = IterationCount.Infinite,
        Easing = new LinearEasing(),
        Children =
        {
            Frame(0, (AngleProperty, 0d)),
            Frame(1, (AngleProperty, 360d)),
        },
    };

    private static Animation Cycle(AvaloniaProperty property, double from, double to, TimeSpan duration) => new()
    {
        Duration = duration,
        IterationCount = IterationCount.Infinite,
        Easing = new SineEaseInOut(),
        Children =
        {
            Frame(0, (property, from)),
            Frame(0.5, (property, to)),
            Frame(1, (property, from)),
        },
    };

    /// <summary>Two turns, braked. Played on a loop so it can be looked at; in use it runs once.</summary>
    private static Animation Landing(TimeSpan duration) => new()
    {
        Duration = duration,
        IterationCount = IterationCount.Infinite,
        Easing = new CubicEaseOut(),
        Children =
        {
            Frame(0, (AngleProperty, 0d)),
            Frame(0.55, (AngleProperty, 720d)),
            Frame(1, (AngleProperty, 720d)),
        },
    };

    /// <summary>
    /// A fault does not spin. It sits still and shivers once per cycle, which is what
    /// makes it legible as "stopped" rather than "slow".
    /// </summary>
    private static Animation Jolt(TimeSpan duration) => new()
    {
        Duration = duration,
        IterationCount = IterationCount.Infinite,
        Easing = new LinearEasing(),
        Children =
        {
            Frame(0, (AngleProperty, 0d)),
            Frame(0.05, (AngleProperty, -3d)),
            Frame(0.10, (AngleProperty, 3d)),
            Frame(0.15, (AngleProperty, -2d)),
            Frame(0.20, (AngleProperty, 0d)),
            Frame(1, (AngleProperty, 0d)),
        },
    };

    private static KeyFrame Frame(double cue, params (AvaloniaProperty Property, double Value)[] setters)
    {
        var frame = new KeyFrame { Cue = new Cue(cue) };

        foreach ((AvaloniaProperty property, double value) in setters)
        {
            frame.Setters.Add(new Setter(property, value));
        }

        return frame;
    }
}
#pragma warning restore CA1001
