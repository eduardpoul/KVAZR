using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Kvazr.App.ViewModels;

namespace Kvazr.App.Views;

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

        AttachWebPane(shell);
    }

    /// <summary>
    /// Puts the web content pane in, once, for the lifetime of the window.
    ///
    /// One WebView rather than one per page: it takes a couple of seconds to boot, and
    /// paying that on every click in the sidebar would make navigation feel broken. The
    /// root component listens to the shell and swaps pages itself; this only decides
    /// whether the web pane or the native scroller is the one on screen — the native
    /// side keeps the internal drawings (identity, the capability matrix) and remains
    /// the whole story under --native and off Windows.
    /// </summary>
    private void AttachWebPane(ShellViewModel shell)
    {
#if WINDOWS
        if (!Web.WebUi.Enabled || !OperatingSystem.IsWindows()) return;

        var host = new Web.PaneHost(shell);
        WebPane.Content = host;

        void Sync()
        {
            bool web = shell.CurrentPage
                is ViewModels.DashboardViewModel
                or ViewModels.Devices.DevicePageViewModel
                or ViewModels.Pages.SettingsViewModel
                or ViewModels.Pages.AboutViewModel
                or ViewModels.Pages.HotkeysViewModel;

            WebPane.IsVisible = web;
            ContentPane.IsVisible = !web;
        }

        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.CurrentPage)) Sync();
        };

        ActualThemeVariantChanged += (_, _) =>
            Web.WebUi.Theme.IsDark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

        Web.WebUi.Theme.IsDark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        Sync();
#endif
    }


    /// <summary>
    /// Development affordance, alongside --open and --lang. A page taller than the
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

    /// <summary>
    /// The window went away, by whichever of the several routes there are: the close
    /// button, the tray menu, a programmatic hide.
    ///
    /// Watched as a property rather than hooked onto each of those routes, because there
    /// was no counterpart to <see cref="OnOpened"/> at all and the omission was invisible
    /// until a page could cost something. A hidden window left the scheduler believing a
    /// device page was open, which used to mean one wasted reading a second — and now
    /// means a microphone held open, with the in-use light on, over a window nobody can
    /// see.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && !change.GetNewValue<bool>())
        {
            _shell?.OnWindowHidden();
        }
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
