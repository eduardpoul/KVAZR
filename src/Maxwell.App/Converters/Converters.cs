using System.Globalization;
using Avalonia.Data.Converters;
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
