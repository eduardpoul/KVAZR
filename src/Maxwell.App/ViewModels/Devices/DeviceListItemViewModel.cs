using CommunityToolkit.Mvvm.ComponentModel;
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

    public bool IsPresent => Snapshot.IsPresent;

    public string Battery => Snapshot.BatteryPercent is { } percent ? $"{percent}%" : "—";

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

    /// <summary>What the row says under the name. Never an error for a normal mode of use.</summary>
    public string Status => Snapshot.Availability switch
    {
        DeviceAvailability.Active => Snapshot.Info.ControlTransport?.Label ?? "Подключено",
        DeviceAvailability.Unreachable => "Не отвечает",
        _ => Snapshot.LastSeenAt is { } seen ? $"последний раз — {seen:HH:mm}" : "Не подключено",
    };

    public void Update(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Snapshot = snapshot;

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsPresent));
        OnPropertyChanged(nameof(Battery));
        OnPropertyChanged(nameof(IsBatteryPending));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Status));
    }
}
