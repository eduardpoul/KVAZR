using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Model;
using Maxwell.Core.Settings;

namespace Maxwell.App.Converters;

/// <summary>Renders the three noise-reduction modes with the labels Audeze HQ uses.</summary>
public sealed class NoiseReductionLabelConverter : IValueConverter
{
    public static readonly NoiseReductionLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            NoiseReductionMode.Off => "Выкл",
            NoiseReductionMode.Low => "Низкое",
            NoiseReductionMode.High => "Высокое",
            _ => "—",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean, for the common "show this when that is absent" case.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>
/// Labels an EQ slot as factory or user-editable. Lives here rather than on the model
/// so the core stays free of UI language.
/// </summary>
public sealed class PresetKindConverter : IValueConverter
{
    public static readonly PresetKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Пользовательский" : "Встроенный";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Theme names for the settings picker.</summary>
public sealed class ThemeLabelConverter : IValueConverter
{
    public static readonly ThemeLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            AppTheme.System => "Системная",
            AppTheme.Light => "Светлая",
            AppTheme.Dark => "Тёмная",
            _ => "—",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Colours the hint line under a capability control. An error is the only thing that
/// goes red — a transport limitation is a fact about the hardware, not a fault, and
/// painting it red would make an ordinary mode of use look broken.
/// </summary>
public sealed class HintToneBrushConverter : IValueConverter
{
    public static readonly HintToneBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            HintKind.Error => "DangerBrush",
            HintKind.Warning => "StatusIrreversibleBrush",
            _ => "TextSecondaryBrush",
        };

        return Application.Current?.TryFindResource(key, out object? brush) == true ? brush : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Dims a device that is not attached. Dimmed rather than hidden: a keyboard in a bag
/// still belongs in the list, with the time it was last seen instead of a row of zeroes.
/// </summary>
public sealed class PresenceOpacityConverter : IValueConverter
{
    public static readonly PresenceOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.55;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
