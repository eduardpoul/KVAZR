using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Kvazr.App.Branding;

namespace Kvazr.App.Controls;

/// <summary>
/// One placeholder block.
///
/// The identity states these rules precisely and they are requirements, not
/// decoration:
///
/// <list type="bullet">
///   <item>Drawn in the orbit colour — <c>#16324D</c> on a light surface, <c>#A8BFD6</c>
///   on a dark one. Never grey: grey placeholders belong to some other product.</item>
///   <item>Corner radius is half the height, so a block is a lozenge at any size and
///   never a rounded rectangle with a radius chosen by hand.</item>
///   <item>Opacity cycles 30 → 70 % over 1.4 s.</item>
///   <item>Adjacent blocks are phase-shifted by 0.1–0.15 s, which is what makes a row
///   read as one object rather than as a set of bars blinking at each other.</item>
/// </list>
///
/// And the rule that is not about drawing at all: a skeleton holds exactly the height
/// the real element will take. Getting that wrong is what makes a screen jump when the
/// data lands, and it is the only reason to prefer a skeleton to a spinner in the
/// first place. Callers set <see cref="Layoutable.Height"/> from the real row, and
/// there is deliberately no default.
/// </summary>
#pragma warning disable CA1001
// See KvazrMarkControl: the token source lives from attach to detach, not for the
// lifetime of the object, and an Avalonia control is never disposed by anyone.
internal sealed class Skeleton : Control
{
    public static readonly StyledProperty<KvazrSurface> SurfaceProperty =
        AvaloniaProperty.Register<Skeleton, KvazrSurface>(nameof(Surface));

    /// <summary>
    /// Position of this block within its group, counting from zero. Turns into the
    /// phase shift; adjacent blocks pass 0, 1, 2.
    /// </summary>
    public static readonly StyledProperty<int> PhaseProperty =
        AvaloniaProperty.Register<Skeleton, int>(nameof(Phase));

    /// <summary>
    /// Corner radius, or NaN to take half the height.
    ///
    /// Half the height is the rule and the default, and it is right for every bar. It
    /// is wrong for a placeholder standing in for a square tile: half of 44 is a circle,
    /// and the identity's own artboard gives that block the 6 px the real tile has. The
    /// deeper rule — a skeleton holds the shape the real element will take — wins, and
    /// this is how a caller says so.
    /// </summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<Skeleton, double>(nameof(Radius), double.NaN);

    private static readonly StyledProperty<double> PulseProperty =
        AvaloniaProperty.Register<Skeleton, double>(nameof(Pulse), 0.3);

    private CancellationTokenSource? _running;

    public Skeleton()
    {
        // See KvazrMarkControl: the orbit colour is chosen at draw time from the theme.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    static Skeleton()
    {
        AffectsRender<Skeleton>(SurfaceProperty, RadiusProperty, PulseProperty);
    }

    public KvazrSurface Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public int Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    private double Pulse => GetValue(PulseProperty);

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        bool dark = IsSet(SurfaceProperty)
            ? Surface == KvazrSurface.Dark
            : ActualThemeVariant == ThemeVariant.Dark;

        Color colour = dark ? KvazrPalettes.OrbitOnDark : KvazrPalettes.Orbit;
        double radius = double.IsNaN(Radius) ? Bounds.Height / 2 : Radius;

        using (context.PushOpacity(Pulse))
        {
            context.DrawRectangle(
                new SolidColorBrush(colour),
                null,
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                radius,
                radius);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _running = new CancellationTokenSource();

        var animation = new Animation
        {
            Duration = KvazrTimings.SkeletonPulse,
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut(),

            // Applied once, before the first iteration — which is exactly what a phase
            // shift is. A delay repeated every iteration would be a pause, not a shift.
            Delay = KvazrTimings.SkeletonPhaseStep * Phase,
            Children =
            {
                Keyframe(0, 0.3),
                Keyframe(0.5, 0.7),
                Keyframe(1, 0.3),
            },
        };

        _ = animation.RunAsync(this, _running.Token);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _running?.Cancel();
        _running?.Dispose();
        _running = null;
    }

    private static KeyFrame Keyframe(double cue, double opacity) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(PulseProperty, opacity) },
    };
}
#pragma warning restore CA1001
