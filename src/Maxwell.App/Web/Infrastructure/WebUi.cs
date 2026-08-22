using CommunityToolkit.Mvvm.ComponentModel;

namespace Maxwell.App.Web;

/// <summary>
/// The switch and the shared state of the web-rendered content pane.
///
/// The pane is the default on Windows because the design is authored in HTML and the
/// web layout reproduces it directly; --native falls back to the Avalonia views, which
/// remain the whole story on the other platforms until a WebKit host exists.
/// </summary>
public static class WebUi
{
    /// <summary>Web content pane on Windows unless --native said otherwise.</summary>
    public static bool Enabled { get; set; } = OperatingSystem.IsWindows();

    /// <summary>The theme, observable, so the page can switch its palette live.</summary>
    public static WebTheme Theme { get; } = new();
}

/// <summary>Dark or not. A class because the page needs to hear it change.</summary>
public sealed partial class WebTheme : ObservableObject
{
    [ObservableProperty]
    public partial bool IsDark { get; set; }
}
