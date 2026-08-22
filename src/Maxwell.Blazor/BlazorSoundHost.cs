using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;

using WinFormsControl = System.Windows.Forms.Control;

namespace Maxwell.Blazor;

/// <summary>
/// The WinForms BlazorWebView, parented into the Avalonia window by its HWND.
///
/// Avalonia positions the returned handle itself, and WinForms picks the resulting
/// WM_WINDOWPOSCHANGED up as its own bounds, so no size plumbing is needed between the
/// two — the one thing that is needed is forcing the handle to exist before it is
/// handed over, because a WinForms control creates its window lazily.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BlazorSoundHost : NativeControlHost, IDisposable
{
    private readonly ISoundSectionSource _source;
    private BlazorWebView? _view;

    public BlazorSoundHost(ISoundSectionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();

        _view = new BlazorWebView
        {
            // Absolute, because the WinForms host resolves a relative page against the
            // process working directory, and this application is started from wherever.
            HostPage = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
            Services = services.BuildServiceProvider(),
        };

        _view.RootComponents.Add(new RootComponent(
            "#app",
            typeof(SoundSection),
            new Dictionary<string, object?> { ["Source"] = _source }));

        // Forces window creation; the property is the point, not the value.
        _ = ((WinFormsControl)_view).Handle;

        return new PlatformHandle(((WinFormsControl)_view).Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control) => Dispose();

    public void Dispose()
    {
        _view?.Dispose();
        _view = null;
    }
}
