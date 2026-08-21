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

    public string DisplayName => Snapshot.Info.DisplayName;

    public DeviceClass Class => Snapshot.Info.Class;

    public string? ImageKey => Snapshot.Info.ImageKey;

    /// <summary>
    /// The curated photograph for this model, or null when there is none — which is the
    /// ordinary case. The tile falls back to <see cref="Glyph"/>, not to an empty box.
    /// </summary>
    public Bitmap? Photo => DeviceImages.Photo(Snapshot.Info.ImageKey);

    /// <summary>The silhouette for this device's class. Every class has one.</summary>
    public Geometry? Glyph => DeviceImages.Glyph(Snapshot.Info.Class);

    public bool IsPresent => Snapshot.IsPresent;

    public string Battery => Snapshot.BatteryPercent is { } percent ? $"{percent}%" : "—";

    public bool IsCharging => Snapshot.IsCharging;

    /// <summary>What the row says under the name. Never an error for a normal mode of use.</summary>
    public string Status => Snapshot.Availability switch
    {
        DeviceAvailability.Active => Link,
        DeviceAvailability.Unreachable => "Не отвечает",
        _ => Snapshot.LastSeenAt is { } seen ? $"последний раз — {seen:HH:mm}" : "Не подключено",
    };

    /// <summary>
    /// How the device is attached. The channel that carries settings is named first
    /// because it is the one that decides what the page can do, but a device with no
    /// such channel still says how it is connected: falling back to the bare word
    /// "Подключено" told the user nothing they could not already see from the row.
    /// </summary>
    private string Link =>
        Snapshot.Info.ControlTransport?.Label
        ?? Snapshot.Info.Transports.FirstOrDefault(t => t.IsPresent)?.Label
        ?? "Подключено";

    public void Update(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Snapshot = snapshot;

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsPresent));
        OnPropertyChanged(nameof(Battery));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Status));
    }
}
