using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Maxwell.App.Infrastructure;

namespace Maxwell.App.Views;

/// <summary>Which of the two splash forms to show.</summary>
internal enum SplashForm
{
    /// <summary>640×400, mark, name, caption, version. A cold start.</summary>
    Named,

    /// <summary>400×400, mark alone. The name has been read before.</summary>
    MarkOnly,
}

/// <summary>
/// What the user looks at while the first discovery sweep runs.
///
/// The window is deliberately not tied to whether discovery has finished — the shell
/// decides that. What this owns is the promise a splash makes to the person watching
/// it: that it will not appear and vanish within a frame. It borrows the skeleton's
/// minimum-visible rule for that, because it is the same rule about the same problem.
/// </summary>
internal sealed partial class SplashWindow : Window
{
    private readonly DispatcherTimer _hold = new(DispatcherPriority.Background);
    private DateTimeOffset _shownAt;
    private bool _dismissRequested;
    private bool _pinned;

    public SplashWindow()
    {
        // InitializeComponent is generated, and takes an optional argument. Declaring a
        // parameterless one here as well compiles and wins overload resolution, and the
        // generated one — the half that assigns the x:Name fields — then never runs, so
        // every named control is null at the first touch. Do not add one back.
        InitializeComponent();

        Version.Text = ProductVersion();
        Caption.Text = Maxwell.App.Localization.Localizer.Current.Say("Looking for devices");

        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            if (_dismissRequested) Close();
        };
    }


    /// <summary>
    /// Puts the splash up. <paramref name="pinned"/> is the development case — it stays
    /// until the process ends, so it can be photographed.
    /// </summary>
    public void Present(SplashForm form, bool pinned = false)
    {
        _pinned = pinned;
        _shownAt = DateTimeOffset.UtcNow;

        if (form == SplashForm.MarkOnly)
        {
            Width = 400;
            Height = 400;
            Named.IsVisible = false;
            MarkOnly.IsVisible = true;
        }

        Show();

        if (form == SplashForm.Named) StartSweep();
    }

    /// <summary>
    /// The shell is ready. Closes now, or when the minimum has been served — whichever
    /// is later.
    /// </summary>
    public void Dismiss()
    {
        if (_pinned || _dismissRequested) return;

        _dismissRequested = true;

        TimeSpan shown = DateTimeOffset.UtcNow - _shownAt;

        if (shown >= LoadingGate.MinimumVisible)
        {
            Close();
            return;
        }

        _hold.Interval = LoadingGate.MinimumVisible - shown;
        _hold.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hold.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// The bar in the identity is a CSS keyframe on a 30 %-wide block, from −40 % to
    /// 240 % of its own width over 1.6 s. Avalonia translates in pixels, so the two
    /// percentages are resolved against the block's 192 px here rather than left as
    /// magic numbers in the markup.
    /// </summary>
    private void StartSweep()
    {
        double width = Sweep.Width;

        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.6),
            IterationCount = IterationCount.Infinite,
            Easing = new CubicEaseInOut(),
            Children =
            {
                Frame(0, width * -0.4),
                Frame(1, width * 2.4),
            },
        };

        _ = animation.RunAsync(Sweep);
    }

    /// <summary>
    /// The bar moves by its position in a Canvas rather than by a transform.
    ///
    /// Both transform routes fail from code: animating the TranslateTransform object
    /// itself is rejected because the animator wants a Visual, and animating
    /// RenderTransform is rejected because no animator is registered for that property
    /// outside XAML. Canvas.Left is an ordinary double, which every animator path
    /// handles, and the motion on screen is identical.
    /// </summary>
    private static KeyFrame Frame(double cue, double x) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(Canvas.LeftProperty, x) },
    };

    /// <summary>
    /// The version the user was given, not the assembly's four-part number. Falls back
    /// to the file version, and then to nothing at all — an empty corner is better than
    /// "0.0.0" on the first thing anyone sees.
    /// </summary>
    private static string ProductVersion()
    {
        Assembly assembly = typeof(SplashWindow).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends "+<commit>" to the informational version; nobody wants that
        // in the corner of a splash screen.
        if (informational is { Length: > 0 })
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        // Read from the attribute, not from the file on disk: a single-file bundle has
        // no file behind the assembly, and Assembly.Location is empty there.
        return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty;
    }
}
