using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;

namespace Maxwell.App.Views.Pages;

/// <summary>The one device page, assembled from capabilities. See the view model.</summary>
public partial class DevicePageView : UserControl
{
    public DevicePageView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Hook();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private DevicePageViewModel? _hooked;

    private void Hook()
    {
        if (_hooked is not null) _hooked.PropertyChanged -= OnPageChanged;

        _hooked = DataContext as DevicePageViewModel;
        if (_hooked is not null) _hooked.PropertyChanged += OnPageChanged;

        SwapForExperiment();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DevicePageViewModel.Section)) SwapForExperiment();
    }

    /// <summary>
    /// The --blazor experiment: the sound section drawn by a WebView instead of by this
    /// page's own list, same view models behind both. Native rendering stays the
    /// default; this exists to put the two side by side and see which is closer to the
    /// design and which was cheaper to get there.
    /// </summary>
    private void SwapForExperiment()
    {
#if WINDOWS
        var slot = this.FindControl<ContentControl>("WebSlot");
        var native = this.FindControl<ItemsControl>("NativeSections");
        if (slot is null || native is null) return;

        bool web = App.BlazorSoundOnStart
            && _hooked is { Section: { Domain: CapabilityDomain.Audio } section };

        if (web && _hooked is { Section: { } chosen })
        {
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            slot.Content = new Maxwell.Blazor.BlazorSoundHost(
                new Platform.BlazorSoundSection(chosen, dark));
        }
        else
        {
            (slot.Content as System.IDisposable)?.Dispose();
            slot.Content = null;
        }

        slot.IsVisible = web;
        native.IsVisible = !web;
#endif
    }
}
