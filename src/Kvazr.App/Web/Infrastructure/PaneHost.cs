#if WINDOWS
using System.Runtime.Versioning;

using Avalonia.Controls;
using Avalonia.Platform;

using Kvazr.App.ViewModels;

using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

using WinFormsControl = System.Windows.Forms.Control;

namespace Kvazr.App.Web;

/// <summary>
/// The WinForms BlazorWebView carrying the whole content pane, parented into the
/// Avalonia window by its HWND.
///
/// One host for the lifetime of the window, not one per page: a WebView takes a couple
/// of seconds to boot, and paying that on every navigation would make the sidebar feel
/// broken. The root component listens to the shell and swaps pages itself.
///
/// Avalonia positions the returned handle; WinForms picks the move up as its own bounds;
/// the only thing that has to happen here is forcing the handle to exist before it is
/// handed over, because a WinForms control creates its window lazily.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PaneHost : NativeControlHost, IDisposable
{
    private readonly ShellViewModel _shell;
    private BlazorWebView? _view;

    public PaneHost(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();

        _view = new EmbeddedBlazorWebView
        {
            // Only names the root page; the files themselves come from the assembly.
            // Absolute, because the WinForms host resolves a relative page against the
            // process working directory, and the app is started from wherever.
            HostPage = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
            Services = services.BuildServiceProvider(),
        };

        _view.RootComponents.Add(new RootComponent(
            "#app",
            typeof(PaneRoot),
            new Dictionary<string, object?> { ["Shell"] = _shell, ["Theme"] = WebUi.Theme }));

        // Forces window creation; the property read is the point, not the value.
        _ = ((WinFormsControl)_view).Handle;

        return new PlatformHandle(((WinFormsControl)_view).Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control) => Dispose();

    public void Dispose()
    {
        _view?.Dispose();
        _view = null;
    }

    /// <summary>
    /// Serves the pane from the files embedded in the assembly rather than from a wwwroot
    /// folder beside the executable, which a single-file publish does not have.
    /// </summary>
    private sealed class EmbeddedBlazorWebView : BlazorWebView
    {
        public override IFileProvider CreateFileProvider(string contentRootDir) =>
            new ManifestEmbeddedFileProvider(typeof(PaneHost).Assembly, "wwwroot");
    }
}
#endif
