using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Maxwell.App.Imaging;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>
/// One device as it appears in the list and on the dashboard.
///
/// Deliberately thin: image, name, kind, transport, state and battery. The design puts
/// nothing else on this level, because a list that tries to show everything about six
/// devices shows nothing about any of them.
/// </summary>
public sealed partial class DeviceListItemViewModel : ObservableObject
{
    public DeviceListItemViewModel(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    [ObservableProperty]
    public partial DeviceSnapshot Snapshot { get; private set; }

    public string Key => Snapshot.Info.Key;

    /// <summary>
    /// The device's name. Routed through the catalogue for one reason only: the
    /// development stand-ins carry "(mock)" in their model names, and that word is worth
    /// translating with the rest of the interface. A real device's name is its own and
    /// passes through untouched.
    /// </summary>
    public string DisplayName => Maxwell.App.Localization.Localizer.Current.Say(Snapshot.Info.DisplayName);

    public DeviceClass Class => Snapshot.Info.Class;

    public string? ImageKey => Snapshot.Info.ImageKey;

    /// <summary>
    /// The curated photograph for this model, or null when there is none — which is the
    /// ordinary case. The tile falls back to <see cref="Glyph"/>, not to an empty box.
    /// </summary>
    public Bitmap? Photo => DeviceImages.Photo(Snapshot.Info.ImageKey);

    /// <summary>Which coloured Fluent icon stands for this device without a photo.</summary>
    public string IconKey => Web.DeviceIcon.KeyFor(Snapshot.Info);

    public bool IsPresent => Snapshot.IsPresent;

    /// <summary>
    /// What kind of thing this is, as a person would say it. The design puts it in a
    /// pill under the name rather than in the row's prose, because at a glance the class
    /// is what separates two devices with similar names.
    /// </summary>
    /// <summary>A device class in the language in force; the English is the fallback.</summary>
    private static string Name(string key, string english) =>
        Maxwell.App.Localization.Localizer.Current[$"device.class.{key}", english];

    public string Kind => Snapshot.Info.Class switch
    {
        DeviceClass.Headset => Name("headset", Maxwell.App.Localization.Localizer.Current.Say("Headset")),
        DeviceClass.Earbuds => Name("earbuds", Maxwell.App.Localization.Localizer.Current.Say("Earbuds")),
        DeviceClass.Microphone => Name("microphone", Maxwell.App.Localization.Localizer.Current.Say("Microphone")),
        DeviceClass.Speaker => Name("speaker", Maxwell.App.Localization.Localizer.Current.Say("Speaker")),
        DeviceClass.Mouse => Name("mouse", Maxwell.App.Localization.Localizer.Current.Say("Mouse")),
        DeviceClass.Keyboard => Name("keyboard", Maxwell.App.Localization.Localizer.Current.Say("Keyboard")),
        DeviceClass.Gamepad => Name("gamepad", Maxwell.App.Localization.Localizer.Current.Say("Gamepad")),
        DeviceClass.Receiver => Name("receiver", Maxwell.App.Localization.Localizer.Current.Say("Receiver")),
        _ => Name("unknown", Maxwell.App.Localization.Localizer.Current.Say("Device")),
    };

    /// <summary>
    /// Every channel the device is currently reachable on, each its own pill. A headset
    /// on a cable and a dongle at once is the normal case rather than the exception, and
    /// naming only one of them is how the page ends up claiming the wrong thing about
    /// what it can do.
    /// </summary>
    public IReadOnlyList<TransportPill> Transports =>
        [.. Snapshot.Info.Transports
            .Where(t => t.IsPresent)
            .Select(t => new TransportPill(
                Maxwell.App.Localization.TransportNames.Brief(t),
                TransportActivity.IsLive(Snapshot.Info, t)))];

    /// <summary>
    /// The aside after the pills — what this link does or does not carry. Empty when
    /// there is nothing to add, and then the row simply has no third element.
    /// </summary>
    public string Note => Snapshot.Availability switch
    {
        DeviceAvailability.Unreachable => Maxwell.App.Localization.Localizer.Current["device.silent", "not answering"],
        DeviceAvailability.Offline =>
            Snapshot.LastSeenAt is { } seen ? LastSeen(seen) : Maxwell.App.Localization.Localizer.Current.Say("not connected"),

        // Deliberately not "read only" any more: on this machine that was most of the
        // list, and a label most rows carry stops being information. What a device
        // cannot do is the page's business, said where the missing control would be.
        _ => string.Empty,
    };

    public bool HasNote => Note.Length > 0;

    /// <summary>
    /// Whether "forget in the system" means anything for this device. A pairing is something
    /// only a wireless link has; offering it for a device on a cable would be offering to
    /// undo something that was never done.
    /// </summary>
    public bool CanForget =>
        Snapshot.Info.Transports.Any(t => t.Kind is TransportKind.Bluetooth or TransportKind.Dongle);

    public string Battery => Snapshot.BatteryPercent is { } percent ? $"{percent}%" : "—";

    /// <summary>
    /// Whether there is a reading to print at all. A dash next to every device that
    /// simply has no battery made the list look like a table of failures; nothing is
    /// the right amount of ink for nothing.
    /// </summary>
    public bool HasBattery => Snapshot.BatteryPercent is not null;

    /// <summary>
    /// The tray row's three power states, per Turn 12: a percentage for a device that
    /// reports one, the words "from the cable" for one that runs off the wire, and a dash for
    /// one that ought to report and does not. Three separate readings rather than one
    /// string because they are not the same size or colour — the number carries the row,
    /// the other two step back out of the way.
    /// </summary>
    public bool ShowsBatteryPercent => Mode == BatteryDisplay.Charge && !IsBatteryPending;

    public bool ShowsMains => Mode == BatteryDisplay.Mains && !IsBatteryPending;

    public bool ShowsNoReading => Mode == BatteryDisplay.Absent && !IsBatteryPending;

    private BatteryDisplay Mode => BatteryReadout.Mode(Snapshot);

    /// <summary>
    /// The device says it has a battery and has not yet said what is in it. This is the
    /// one place in a row where a loader is the honest answer: the row itself arrived,
    /// so a skeleton over the whole thing would be a lie, and a dash would claim the
    /// reading came back empty. A device with no battery at all — the QuadCast is the
    /// first — never reaches this, because it never declares the capability.
    /// </summary>
    public bool IsBatteryPending =>
        Snapshot.CanControl && Snapshot.Has(CapabilityIds.PowerBattery) && Snapshot.BatteryPercent is null;

    public bool IsCharging => Snapshot.IsCharging;

    /// <summary>
    /// The reading the gauge draws, or null when there is none. Deliberately not zero: a
    /// gauge drawn empty claims the device is flat, which is a very different statement
    /// from a device that does not report a level at all.
    /// </summary>
    public int? BatteryPercent => Snapshot.BatteryPercent;

    /// <summary>
    /// The name without the maker in front of it, for the navigation pane. The artboard
    /// lists "Maxwell" and "QuadCast S" there and spells them out in full only in the
    /// content: at 260 pixels the maker is the half of the name that gets trimmed away,
    /// and it is also the half that distinguishes nothing — nobody owns two QuadCasts
    /// from different companies.
    /// </summary>
    public string ShortName
    {
        get
        {
            string model = Snapshot.Info.Model;
            string vendor = Snapshot.Info.Vendor;

            // The maker is stripped first and the rest translated, not the other way
            // round: the catalogue is keyed on what the driver wrote, and "MX Master
            // (mock)" is a key while "Master (mock)" is nothing.
            if (vendor.Length > 0 && model.StartsWith(vendor, StringComparison.CurrentCultureIgnoreCase))
            {
                model = model[vendor.Length..].TrimStart();
            }

            return Maxwell.App.Localization.Localizer.Current.Say(model);
        }
    }

    /// <summary>
    /// "last seen — 14:41", said in one piece. The time is invariant and the words
    /// around it are not, which is why the key is the whole sentence.
    /// </summary>
    private static string LastSeen(DateTimeOffset seen) =>
        Maxwell.App.Localization.Localizer.Current.Say(
            "last seen — {0}",
            seen.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// What the row says under the name in the tray panel. Never an error for a normal
    /// mode of use.
    ///
    /// Channel first, then whatever else is worth saying about it, joined the way the
    /// artboard joins them: "USB-C · sound over the dongle". The channel alone left the panel
    /// repeating what the icon already showed, and the note alone left it unclear which
    /// of two links the note was about.
    /// </summary>
    public string Status => Snapshot.Availability switch
    {
        DeviceAvailability.Active => HasNote ? $"{Link} · {Note}" : Link,
        DeviceAvailability.Unreachable => Maxwell.App.Localization.Localizer.Current["device.silent.caps", "Not answering"],
        _ => Snapshot.LastSeenAt is { } seen ? LastSeen(seen) : Maxwell.App.Localization.Localizer.Current.Say("Not connected"),
    };

    /// <summary>
    /// How the device is attached. The channel that carries settings is named first
    /// because it is the one that decides what the page can do, but a device with no
    /// such channel still says how it is connected: falling back to the bare word
    /// "Connected" told the user nothing they could not already see from the row.
    /// </summary>
    private string Link =>
        Snapshot.Info.ControlTransport?.ShortLabel
        ?? Snapshot.Info.Transports.FirstOrDefault(t => t.IsPresent)?.ShortLabel
        ?? Maxwell.App.Localization.Localizer.Current["device.connected", "Connected"];

    public void Update(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Snapshot = snapshot;

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsPresent));
        OnPropertyChanged(nameof(Battery));
        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(BatteryPercent));
        OnPropertyChanged(nameof(ShowsBatteryPercent));
        OnPropertyChanged(nameof(ShowsMains));
        OnPropertyChanged(nameof(ShowsNoReading));
        OnPropertyChanged(nameof(ShortName));
        OnPropertyChanged(nameof(IsBatteryPending));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Transports));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
    }
}
