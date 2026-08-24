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
using Maxwell.App.Infrastructure;
using Maxwell.App.ViewModels;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Mock;
using Maxwell.Core.Polling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxwell.App.Views.Pages;

/// <summary>
/// Renders the application's pages straight to pictures, in both themes.
///
/// The alternative is a screenshot of the running window, and capturing a window means
/// putting it in front of whoever is using the computer — not an acceptable price for
/// looking at a control. Rendering offscreen has no such problem, and it goes through the
/// same views, view models, factory and templates the application does, so what appears in
/// the picture is what a user would get rather than a second drawing of it.
///
/// The same argument produced <c>TrayIconPreview</c> one phase earlier: some things can
/// only be judged by looking at the pixels.
/// </summary>
internal static class PagePreview
{
    /// <summary>About what the content pane holds in a comfortably sized window.</summary>
    private const double PageWidth = 900;

    /// <summary>The tray panel's own width, which the design fixes at 360.</summary>
    private const double FlyoutWidth = 360;

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

            // The first-launch screen only appears when nothing is attached, which on a
            // developer's machine is never. Rendering it on demand is the only way it
            // gets looked at more than once.
            Render($"{path}-welcome-{suffix}.png", theme, PageWidth, () => new WelcomeView());
            Render($"{path}-settings-{suffix}.png", theme, PageWidth, Settings);
            Render($"{path}-about-{suffix}.png", theme, PageWidth, About);
            // Shortcuts are shelved; the page still builds, so put this back with the
            // rail entry when they return.
            // Render($"{path}-hotkeys-{suffix}.png", theme, PageWidth, Hotkeys);
            Render($"{path}-mouse-{suffix}.png", theme, PageWidth, () => Device(MockDeviceDriver.MouseKey));
            Render($"{path}-keyboard-{suffix}.png", theme, PageWidth, () => Device(MockDeviceDriver.KeyboardKey));

            // The tray panel appears on a click and vanishes on a click elsewhere, which
            // is exactly the wrong shape for looking at it while working on it. Rendered
            // at its own 360 rather than the page width, because its whole layout is an
            // argument about what fits in that width.
            Render($"{path}-flyout-{suffix}.png", theme, FlyoutWidth, Flyout, pad: 0);
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
            DataContext = new SettingsViewModel(
                store, new PreviewAutostart(), registry, new HiddenDevices(store),
                new Maxwell.App.Localization.Localizer(store),
                new DriverSwitchboard(), [], []),
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

    // Shelved with the shortcuts page itself. Kept whole rather than deleted: what it
    // stands up — a settings store, a registry, a coordinator over a service that
    // registers nothing — is the awkward part to write again.
    /*
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
    */

    private static AboutView About()
    {
        var registry = new DeviceRegistry([], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);
        return new AboutView { DataContext = new AboutViewModel(registry) };
    }

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

    /// <summary>
    /// The tray panel over the same mock devices the pages use, with its first row opened
    /// so both states are in the one picture — a closed row is four numbers and an opened
    /// one is the whole argument about what fits in 360 pixels.
    ///
    /// The window is built and its content lifted out: the panel's layout is in its
    /// content, and a Window cannot be rendered offscreen the way a control can.
    /// </summary>
    private static Control Flyout()
    {
        var store = new PreviewSettings();
        var registry = new DeviceRegistry(
            [new MockDeviceDriver()], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        registry.DiscoverAsync().GetAwaiter().GetResult();

        // Discovery finds the devices; it does not read them. Without this every control
        // in an opened row draws its "no reading yet" face — a dash and an amber dot —
        // and the picture would be of an empty panel rather than of a working one.
        foreach (DeviceSnapshot snapshot in registry.Devices)
        {
            registry.Session(snapshot.Info.Key)?.RefreshAllAsync().GetAwaiter().GetResult();
        }

        var sweep = new FirstSweep(registry, NullLoggerFactory.Instance);
        sweep.Begin();

        var vm = new FlyoutViewModel(
            registry, new PreviewPolling(), new HiddenDevices(store), sweep, new TrayExtras(store));

        // The panel spends its first moments showing placeholders on purpose, and both
        // the sweep and the gate that ends that spell finish on the dispatcher — which an
        // offscreen render never pumps by itself. Without this the picture is always of
        // the skeletons: a real state, but not the one worth looking at.
        for (int i = 0; i < 600 && !vm.Loading.ShowContent; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        // Opened here rather than left closed: the expanded row is where the controls,
        // the readings and the link live, and a render of closed rows alone would show
        // none of them.
        if (vm.Devices.FirstOrDefault() is { } first) first.ToggleCommand.Execute(null);

        var window = new FlyoutWindow { DataContext = vm };
        var content = (Control)window.Content!;

        // Detached before it is handed on: a control may only have one parent, and the
        // window it was built in is thrown away here.
        window.Content = null;

        // The data context came from the window and left with it. Without this every
        // binding resolves against nothing, IsVisible falls back to true, and the picture
        // shows the skeletons, the error line and the empty-list line all at once — which
        // is what the first attempt at this render produced.
        content.DataContext = vm;

        return content;
    }

    /// <summary>Polling that records nothing. A render must not start timers.</summary>
    private sealed class PreviewPolling : IPollingCoordinator
    {
        public PollingAttention Attention => PollingAttention.Idle;

        public string? FocusedDeviceKey => null;

        public void SetAttention(PollingAttention attention, string? focusedDeviceKey = null)
        {
        }

        public void RequestRefresh(string deviceKey, bool full = true)
        {
        }
    }

    private static void Render(
        string path, ThemeVariant theme, double width, Func<Control> build, double pad = 24)
    {
        // The breathing space is for pages, which are drawn as a slab of content with no
        // edge of their own. A panel that is exactly 360 wide must be rendered at 360:
        // padding here silently squeezes it, and then every offset measured off the
        // picture is wrong by the padding — which is how a correctly laid out flyout came
        // to look like it was indented by 14 pixels.
        var content = new Border { Padding = new Thickness(pad), Child = build() };

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
