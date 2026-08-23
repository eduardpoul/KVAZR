using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using Maxwell.App.ViewModels;

namespace Maxwell.App.Views;

/// <summary>
/// The tray panel. Appears at the corner of the working area nearest the tray and
/// closes as soon as it loses focus, because a panel that has to be dismissed is a
/// window, and this is not one.
/// </summary>
public partial class FlyoutWindow : Window
{
    private readonly FlyoutViewModel? _flyout;

    /// <summary>
    /// Whether the panel has actually held focus since it was shown.
    ///
    /// Windows will not hand the foreground to a background process on demand, so a
    /// panel opened by a device connecting is shown but never activated — and the
    /// deactivation that follows would otherwise close it a frame after it appeared.
    /// </summary>
    private bool _held;

    public FlyoutWindow()
    {
        InitializeComponent();

        // The first opening is the one that lands in the wrong place. SizeToContent
        // settles the height only after the content has measured, which is a frame or
        // more after Show returns — so the placement made on the way up used a height
        // that was not yet the final one, and the panel sat away from the tray corner.
        // Every later opening reuses an already-measured window and looks right, which
        // is precisely the "wrong the first time, fine the second" this produced.
        Resized += (_, _) =>
        {
            if (IsVisible) PlaceByTray();
        };

        // Deactivation is the dismissal: clicking anywhere else puts the panel away —
        // but only once it has held focus in the first place.
        Activated += (_, _) => _held = true;
        Deactivated += (_, _) =>
        {
            if (_held) HideFlyout();
        };
    }

    public FlyoutWindow(FlyoutViewModel flyout)
        : this()
    {
        ArgumentNullException.ThrowIfNull(flyout);

        DataContext = flyout;
        _flyout = flyout;
    }

    /// <summary>Places the panel and shows it. Idempotent: a second call just refocuses.</summary>
    public void Present()
    {
        if (!IsVisible)
        {
            _held = false;
            Show();
        }

        // Placed after layout, not before: the height is not known until the content has
        // measured, and positioning against a height of zero puts the panel off-screen.
        Dispatcher.UIThread.Post(PlaceByTray, DispatcherPriority.Loaded);

        Activate();
        _flyout?.Opened();
    }

    /// <summary>
    /// Bottom right of the working area, inset by a margin. The working area already
    /// excludes the taskbar wherever the user has put it, so this lands beside the tray
    /// without asking the shell where the tray is.
    /// </summary>
    private void PlaceByTray()
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        PixelRect area = screen.WorkingArea;
        const int margin = 12;

        double scale = screen.Scaling;
        Size frame = FrameSize ?? new Size(Width, Height);

        var size = new PixelSize((int)(frame.Width * scale), (int)(frame.Height * scale));

        Position = new PixelPoint(
            area.Right - size.Width - (int)(margin * scale),
            area.Bottom - size.Height - (int)(margin * scale));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The panel outlives its dismissal: it is reused, not recreated.
        e.Cancel = true;
        HideFlyout();

        base.OnClosing(e);
    }



    private void HideFlyout()
    {
        if (!IsVisible) return;

        _held = false;

        Hide();
        _flyout?.Closed();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
