using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kvazr.App.ViewModels.Capabilities;
using Kvazr.Core.Model;
using Kvazr.Core.Settings;

namespace Kvazr.App.Converters;

/// <summary>Renders the three noise-reduction modes with the labels Audeze HQ uses.</summary>
public sealed class NoiseReductionLabelConverter : IValueConverter
{
    public static readonly NoiseReductionLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            NoiseReductionMode.Off => Kvazr.App.Localization.Localizer.Current.Say("Off"),
            NoiseReductionMode.Low => Kvazr.App.Localization.Localizer.Current.Say("Low"),
            NoiseReductionMode.High => Kvazr.App.Localization.Localizer.Current.Say("High"),
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
        value is true ? Kvazr.App.Localization.Localizer.Current.Say("User") : Kvazr.App.Localization.Localizer.Current.Say("Built-in");

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
            AppTheme.System => Kvazr.App.Localization.Localizer.Current.Say("System"),
            AppTheme.Light => Kvazr.App.Localization.Localizer.Current.Say("Light"),
            AppTheme.Dark => Kvazr.App.Localization.Localizer.Current.Say("Dark"),
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
/// Paints a lighting swatch from the packed RGB the device speaks in.
///
/// The colour is opaque on purpose. A translucent swatch would take its tint from
/// whatever is behind it, so the same reading would look like two different colours in
/// the two themes — and this is the one control whose entire job is to show a colour
/// accurately.
/// </summary>
public sealed class PackedColorBrushConverter : IValueConverter
{
    public static readonly PackedColorBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int packed || packed < 0) return null;

        return new SolidColorBrush(
            Color.FromRgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed));
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

/// <summary>
/// Resolves a device's image key to a bundled picture.
///
/// Returns null when there is no image, and the view falls back to a plain tile rather
/// than a broken-image glyph — a stand-in device or one nobody has photographed yet is
/// a normal case, not an error.
/// </summary>
public sealed class DeviceImageConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static readonly DeviceImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key)) return null;

        if (Cache.TryGetValue(key, out Bitmap? cached)) return cached;

        Bitmap? bitmap = null;

        try
        {
            var uri = new Uri($"avares://KVAZR/Assets/{key}.jpg");
            bitmap = new Bitmap(AssetLoader.Open(uri));
        }
        catch (FileNotFoundException)
        {
            // No picture for this device. The tile stays blank.
        }

        Cache[key] = bitmap;
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
