using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Maxwell.App.ViewModels;
using Maxwell.Core.Services;

namespace Maxwell.App.Views;

public partial class MainWindow : Window
{
    private readonly IDeviceService? _device;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, IDeviceService device)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(device);

        DataContext = viewModel;
        _device = device;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Opening the window is the moment a full read is worth its cost; the polling
        // loop deliberately only keeps the battery warm.
        _ = _device?.RefreshAllAsync();
    }

    /// <summary>The client area is extended over the chrome, so dragging is ours to implement.</summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimise(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// Closing hides rather than exits: this is a tray application, and the user
    /// pressing the close button means "get out of my way", not "stop monitoring".
    /// Quitting is done from the tray menu.
    /// </summary>
    private void OnHide(object? sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
