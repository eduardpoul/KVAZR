using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// One driver or contributor in the developer section, and its switch.
///
/// Named by its own id rather than by something friendlier. This screen exists to be
/// compared against the log, where the same string appears, and against
/// <see cref="DeviceInfo.DriverId"/> on the device page; inventing a second vocabulary
/// for the debug surface would mean keeping the two in step for no gain.
/// </summary>
public sealed partial class LibrarySwitchViewModel : ObservableObject
{
    private readonly Action<string, bool> _set;

    public LibrarySwitchViewModel(string id, DriverTier? tier, bool isOn, int devices, Action<string, bool> set)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(set);

        Id = id;
        Tier = tier;
        Devices = devices;
        _set = set;
        IsOn = isOn;
    }

    public string Id { get; }

    /// <summary>Null for a contributor, which adds rows to somebody else's device and owns none.</summary>
    public DriverTier? Tier { get; }

    /// <summary>How many devices this currently owns. Zero is the usual answer and is worth showing:
    /// switching off a library that owns nothing will look like it did nothing at all.</summary>
    public int Devices { get; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>
    /// What the row says under its name: which tier it belongs to, and what it is holding.
    ///
    /// The tier is the whole reason this screen exists. Switching a vendor driver off is
    /// not turning a feature off, it is asking the same hardware to be described by the
    /// layer underneath, and the sentence says so rather than leaving it to be inferred
    /// from a dotted id.
    /// </summary>
    public string Caption
    {
        get
        {
            string what = Tier switch
            {
                DriverTier.Vendor => Localization.Localizer.Current.Say("One vendor's own protocol"),
                DriverTier.Class => Localization.Localizer.Current.Say("A protocol shared by a class of hardware"),
                DriverTier.System => Localization.Localizer.Current.Say("What Windows already knows"),
                _ => Localization.Localizer.Current.Say("Adds rows to devices another driver found"),
            };

            return Devices == 0
                ? what
                : Localization.Localizer.Current.Say("{0} · {1} device(s) now", what, Devices);
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        IsOn = !IsOn;
        _set(Id, IsOn);
    }
}
