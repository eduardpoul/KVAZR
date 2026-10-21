using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Kvazr.App.Controls;

/// <summary>
/// The control for one capability, with the eight states drawn on the frame around it.
/// The inner <c>ContentControl</c> resolves the primitive's own control through the
/// implicit templates declared in the XAML.
/// </summary>
public partial class CapabilityControl : UserControl
{
    /// <summary>
    /// Whether the control prints its own reading above the slider.
    ///
    /// True on a device page, where the capability's name sits in a column of its own and
    /// the reading belongs to the control. False in the tray panel, which prints the
    /// reading opposite the name instead and cannot afford the second line — the list is
    /// capped at 284 pixels and an opened row has to fit inside it. A property rather
    /// than a style class because the element lives inside a DataTemplate, where a
    /// descendant selector declared by the host window does not reach it.
    /// </summary>
    public static readonly StyledProperty<bool> ShowReadingProperty =
        AvaloniaProperty.Register<CapabilityControl, bool>(nameof(ShowReading), defaultValue: true);

    public bool ShowReading
    {
        get => GetValue(ShowReadingProperty);
        set => SetValue(ShowReadingProperty, value);
    }

    public CapabilityControl()
    {
        InitializeComponent();

        // Scrolling a page must never change a device setting. A wheel that lands on a
        // slider because it happened to be under the cursor is not an instruction, and
        // here the value on the other end is real hardware — this is how mixing desks
        // get ruined. The wheel is taken away from the slider and given to the page.
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Source is not Visual source) return;
        if (source.FindAncestorOfType<Slider>(includeSelf: true) is null) return;

        e.Handled = true;

        // Hand the gesture to the page, which is what the person meant by it.
        if (this.FindAncestorOfType<ScrollViewer>() is not { } scroller) return;

        double step = e.Delta.Y * scroller.SmallChange.Height;
        double target = Math.Clamp(
            scroller.Offset.Y - step,
            0,
            Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));

        scroller.Offset = scroller.Offset.WithY(target);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
