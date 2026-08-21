using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Maxwell.App.ViewModels;

namespace Maxwell.App.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel? _shell;

    public MainWindow()
    {
        // Generated, and it takes an optional argument. A hand-written parameterless
        // overload alongside it compiles, wins the call, and leaves every x:Name field
        // null — which is exactly what used to happen here.
        InitializeComponent();
    }

    public MainWindow(ShellViewModel shell)
        : this()
    {
        ArgumentNullException.ThrowIfNull(shell);

        DataContext = shell;
        _shell = shell;
    }


    /// <summary>
    /// Development affordance, alongside --open and --identity. A page taller than the
    /// screen can only be photographed a screenful at a time, and scrolling it any
    /// other way means driving the mouse — which takes the cursor away from whoever is
    /// sitting at the machine.
    /// </summary>
    internal void ScrollTo(double offset) =>
        ContentPane.Offset = ContentPane.Offset.WithY(offset);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Opening the window is the moment a full read is worth its cost; the polling
        // loop deliberately only keeps the battery warm.
        _shell?.OnWindowOpened();
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
