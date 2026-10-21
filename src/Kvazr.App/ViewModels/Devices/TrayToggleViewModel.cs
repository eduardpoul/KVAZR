using CommunityToolkit.Mvvm.ComponentModel;

using Kvazr.App.Platform;

namespace Kvazr.App.ViewModels.Devices;

/// <summary>
/// The last row of a section: whether the tray panel carries these controls too.
///
/// A row that looks like the others and is not one of them. Every other row on the page
/// is a capability — something the device declares and answers for — and this is a
/// preference about the application, which is why it is a view model of its own rather
/// than an invented capability. Putting it in the section anyway is deliberate: it is
/// about these controls, and a switch about them belongs under them rather than three
/// screens away in the settings.
///
/// It exists at all because the panel is 360 pixels wide. Anything it gains, something
/// else loses, so which of a device's controls are worth that is the user's call — and
/// the only place they can weigh it is looking at the controls themselves.
/// </summary>
public sealed partial class TrayToggleViewModel : ObservableObject
{
    private readonly TrayExtras _extras;
    private readonly string _deviceKey;

    private bool _applying;

    public TrayToggleViewModel(TrayExtras extras, string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(deviceKey);

        _extras = extras;
        _deviceKey = deviceKey;

        _applying = true;
        IsOn = extras.ShowsMonitoring(deviceKey);
        _applying = false;
    }

    public string Title => Kvazr.App.Localization.Localizer.Current.Say("Show in the tray panel");

    public string Subtitle =>
        Kvazr.App.Localization.Localizer.Current.Say(
            "Two more rows in a panel that holds about six");

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (_applying) return;

        // Fire and forget, like every other setting on this page: the file is written on
        // a background thread and nothing here waits for it. A failure to save is a
        // failure to remember, not a failure to apply — the panel reads the same object
        // this just changed.
        _ = _extras.SetMonitoringAsync(_deviceKey, value);
    }
}
