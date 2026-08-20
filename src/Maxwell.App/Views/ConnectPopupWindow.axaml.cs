using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;

namespace Maxwell.App.Views;

/// <summary>
/// The connect announcement. Shows a device and its battery, then leaves.
///
/// Deliberately not interactive and deliberately not activated: an announcement that
/// takes the keyboard away from whatever the person was typing into is an interruption,
/// not an announcement.
/// </summary>
public partial class ConnectPopupWindow : Window
{
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(3.5);

    private readonly DispatcherTimer _timer;

    public ConnectPopupWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = Linger };
        _timer.Tick += (_, _) => Dismiss();
    }

    /// <summary>Shows the popup for <paramref name="device"/>, restarting the countdown.</summary>
    public void Present(DeviceSnapshot device)
    {
        ArgumentNullException.ThrowIfNull(device);

        DataContext = new DeviceListItemViewModel(device);

        if (!IsVisible)
        {
            Show();
            Dispatcher.UIThread.Post(PlaceAboveTray, DispatcherPriority.Loaded);
        }

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>
    /// Bottom right, above where the tray panel would sit, so the two never overlap if
    /// the user opens one while the other is still fading.
    /// </summary>
    private void PlaceAboveTray()
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        PixelRect area = screen.WorkingArea;
        const int margin = 12;

        double scale = screen.Scaling;
        Size frame = FrameSize ?? new Size(Width, Height);

        Position = new PixelPoint(
            area.Right - (int)(frame.Width * scale) - (int)(margin * scale),
            area.Bottom - (int)(frame.Height * scale) - (int)(margin * scale));
    }

    private void Dismiss()
    {
        _timer.Stop();
        if (IsVisible) Hide();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Reused, not recreated: this appears every time a device turns up.
        e.Cancel = true;
        Dismiss();

        base.OnClosing(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
