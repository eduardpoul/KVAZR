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
        _flyout.RowExpanded += OnRowExpanded;
    }

    /// <summary>
    /// Scrolls an opened row into view.
    ///
    /// The list is five rows tall and the panel does not grow — that is deliberate, a tray
    /// panel the height of the screen is not a tray panel — so opening a row near the
    /// bottom put its controls under the footer. The row visibly opened and there was
    /// nothing to use, which reads as a broken panel rather than as a full one.
    ///
    /// After layout, because the row is exactly one frame taller than it was when this
    /// runs and scrolling to where it used to end lands short.
    /// </summary>
    private void OnRowExpanded(object? sender, ViewModels.Devices.FlyoutRowViewModel row) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (this.FindControl<ItemsControl>("Rows") is not { } list) return;
                if (list.ContainerFromItem(row) is not Control container) return;

                // After the row has grown, not merely after the click.
                //
                // The controls appear when IsOpen turns true and the row is three times
                // taller a layout pass later; scrolling before that pass asks to see a
                // rectangle that is still 56 pixels tall, which is already on screen, so
                // nothing moves. Waiting for one pass is the difference between scrolling
                // to where the row was and scrolling to where it now ends.
                void Grown(object? sender, EventArgs e)
                {
                    container.LayoutUpdated -= Grown;

                    // The bottom edge rather than the whole row: bringing the row into
                    // view scrolls the least it can, which puts its title at the top of
                    // the viewport and leaves the last of its controls under the footer.
                    container.BringIntoView(
                        new Rect(0, container.Bounds.Height - 1, container.Bounds.Width, 1));
                }

                container.LayoutUpdated += Grown;
            },
            DispatcherPriority.Loaded);

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
