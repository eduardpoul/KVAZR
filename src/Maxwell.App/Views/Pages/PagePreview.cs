using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Maxwell.App.ViewModels.Devices;
using Maxwell.App.Platform;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Settings;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Mock;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxwell.App.Views.Pages;

/// <summary>
/// Renders the design-system matrix and the stand-in device pages straight to pictures,
/// in both themes.
///
/// The alternative is a screenshot of the running window, and it fails here for two
/// reasons that are not going away. The matrix is twelve primitives by eight states and
/// wider than any monitor on this desk, so a screen capture shows a third of it; and
/// capturing a window means putting it in front of whoever is using the computer, which
/// is not an acceptable price for looking at a control. Rendering offscreen has neither
/// problem, and it goes through the same views, view models, factory and templates the
/// application does — the same reason the showcase page is built from live controls
/// rather than drawn a second time.
///
/// The device pages are here as well as the matrix because they are a different layout
/// path: on the showcase a control stands alone, on a device page it sits in a row
/// beside its name, and a composite that fits one does not automatically fit the other.
///
/// The same argument produced <c>TrayIconPreview</c> one phase earlier: some things can
/// only be judged by looking at the pixels.
/// </summary>
internal static class PagePreview
{
    /// <summary>Wide enough for the label column plus eight columns of the widest control.</summary>
    private const double MatrixWidth = 2960;

    /// <summary>About what the content pane holds in a comfortably sized window.</summary>
    private const double PageWidth = 900;

    /// <summary>
    /// Writes one file per page per theme. Separate files rather than one tall image
    /// because each has to be looked at on its own: a control legible in one theme and
    /// lost in the other is the failure this exists to catch.
    /// </summary>
    public static void Dump(string path)
    {
        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            string suffix = theme == ThemeVariant.Light ? "light" : "dark";

            Render($"{path}-matrix-{suffix}.png", theme, MatrixWidth, Showcase);

            // The first-launch screen only appears when nothing is attached, which on a
            // developer's machine is never. Rendering it on demand is the only way it
            // gets looked at more than once.
            Render($"{path}-welcome-{suffix}.png", theme, PageWidth, () => new WelcomeView());
            Render($"{path}-settings-{suffix}.png", theme, PageWidth, Settings);
            Render($"{path}-about-{suffix}.png", theme, PageWidth, About);
            Render($"{path}-hotkeys-{suffix}.png", theme, PageWidth, Hotkeys);
            Render($"{path}-mouse-{suffix}.png", theme, PageWidth, () => Device(MockDeviceDriver.MouseKey));
            Render($"{path}-keyboard-{suffix}.png", theme, PageWidth, () => Device(MockDeviceDriver.KeyboardKey));
        }
    }

    /// <summary>
    /// The settings screen with a real store behind it, so the rendered controls show
    /// the values a first launch would. A page rendered against nulls proves the layout
    /// and nothing about whether the bindings reach anything.
    /// </summary>
    private static SettingsView Settings()
    {
        var store = new PreviewSettings();
        using var registry = new DeviceRegistry(
            [], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        return new SettingsView
        {
            DataContext = new SettingsViewModel(store, new PreviewAutostart(), registry, new HiddenDevices(store)),
        };
    }

    /// <summary>Settings that live for the length of one render and touch no file.</summary>
    private sealed class PreviewSettings : ISettingsStore
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
    }

    /// <summary>Rendering a screenshot must not write to the user's Run key.</summary>
    private sealed class PreviewAutostart : IAutostart
    {
        public bool IsEnabled => false;

        public bool StartsMinimised => true;

        public void Enable(bool startMinimised)
        {
        }

        public void Disable()
        {
        }
    }

    private static HotkeysView Hotkeys()
    {
        var store = new PreviewSettings();
        using var registry = new DeviceRegistry(
            [], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        var coordinator = new HotkeyCoordinator(
            new PreviewHotkeys(), new PrimaryDevice(registry, new HiddenDevices(store)), store);

        return new HotkeysView { DataContext = new HotkeysViewModel(store, coordinator) };
    }

    /// <summary>Registers nothing. A render must not take shortcuts off the machine.</summary>
    private sealed class PreviewHotkeys : IHotkeyService
    {
        public event EventHandler<string>? Triggered
        {
            add { }
            remove { }
        }

        public IReadOnlyList<string> Apply(IReadOnlyList<HotkeyBinding> bindings) => [];

        public void Dispose()
        {
        }
    }

    private static AboutView About()
    {
        var registry = new DeviceRegistry([], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);
        return new AboutView { DataContext = new AboutViewModel(registry) };
    }

    private static CapabilityShowcaseView Showcase() =>
        new CapabilityShowcaseView { DataContext = new CapabilityShowcaseViewModel() };

    /// <summary>
    /// A stand-in device page driven by a real session over the real mock driver, so what
    /// is rendered is what <c>MockCatalogs</c> declares and what the driver answers —
    /// not a second set of fixtures that could quietly disagree with it.
    /// </summary>
    private static DevicePageView Device(string key)
    {
        var driver = new MockDeviceDriver();

        DeviceInfo info = driver.DiscoverAsync().GetAwaiter().GetResult().First(d => d.Key == key);

        var session = new DeviceSession(
            info,
            driver,
            new InMemoryCapabilityLedger(),
            NullLogger<DeviceSession>.Instance);

        session.MarkPresentAsync(info).GetAwaiter().GetResult();
        session.RefreshAllAsync().GetAwaiter().GetResult();

        return new DevicePageView { DataContext = new DevicePageViewModel(session) };
    }

    private static void Render(string path, ThemeVariant theme, double width, Func<Control> build)
    {
        var content = new Border { Padding = new Thickness(24), Child = build() };

        // The scroller is what makes the page lay out at its natural height: it hands its
        // content unbounded space in the scrolling direction, so the content is measured
        // whole however short the window is. Rendering the content rather than the window
        // is then the difference between the whole matrix and its first screenful.
        var scroller = new ScrollViewer { Content = content };

        // Shown, but off the side of every monitor, unactivated and without a taskbar
        // button. Laying the page out by hand does not work: a content presenter builds
        // its child only once it is attached to a live layout root, so a window that is
        // never shown yields an empty presenter and a picture of nothing. It is the
        // layout engine that needs this window, not a person — nobody sees it.
        var host = new Window
        {
            // Asked for on the window as well as on the application below, and both are
            // load-bearing. FluentAvaloniaTheme is declared PreferSystemTheme, and it
            // imposes the operating system's choice on any top level that has not stated
            // one — so a window left at Default drags the whole application back to the
            // system theme every time one is opened.
            RequestedThemeVariant = theme,

            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Width = width,
            Height = 900,
            Position = new PixelPoint(-(int)width - 400, 0),
            Content = scroller,
        };

        host.Show();
        Pump();

        // Forced here, after the window is up, and on the application rather than the
        // window. Two things make that necessary: the brushes in Tokens.axaml are single
        // shared instances whose colours resolve against the application, so a window
        // asking for Light on its own still gets the dark palette; and FluentAvaloniaTheme
        // is declared PreferSystemTheme, which applies the operating system's choice when
        // the styles first attach — after any variant set beforehand, which it overwrites.
        if (Application.Current is { } app) app.RequestedThemeVariant = theme;

        // Painted on the rendered visual, not underneath it. RenderTargetBitmap.Render
        // clears its target, so a second call cannot layer over a first — and text drawn
        // onto transparency comes out with subpixel colour fringes, which turned the page
        // heading into a smear while every line inside the opaque card rendered cleanly.
        object? shell = null;
        Application.Current?.TryFindResource("LayerBackgroundBrush", theme, out shell);
        if (shell is IBrush brush) content.Background = brush;

        Pump();

        var size = new PixelSize(
            (int)Math.Max(1, Math.Ceiling(content.Bounds.Width)),
            (int)Math.Max(1, Math.Ceiling(content.Bounds.Height)));

        using (var bitmap = new RenderTargetBitmap(size, new Vector(96, 96)))
        {
            bitmap.Render(content);

#pragma warning disable CS0618 // The encoder-options overload is not in this Avalonia
            // version yet, and this is a development-only dump.
            bitmap.Save(path);
#pragma warning restore CS0618
        }

        host.Close();

        // The resolved card colour is printed, not just the variant, because those two
        // have disagreed: the application reported Light while every token still came
        // back from the dark dictionary, and the only symptom in the picture itself was
        // that it looked plausible.
        object? card = null;
        Application.Current?.TryFindResource("LayerCardBrush", out card);

        Console.WriteLine(
            $"{path} {size.Width}x{size.Height} theme={Application.Current?.ActualThemeVariant} card={card}");
    }

    /// <summary>
    /// Runs the layout and render jobs the dispatcher has queued. There is no message
    /// loop in this mode, so without draining them by hand the window would be shown and
    /// measured in the same instant, which is to say not measured at all.
    /// </summary>
    private static void Pump()
    {
        for (int i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
    }
}
