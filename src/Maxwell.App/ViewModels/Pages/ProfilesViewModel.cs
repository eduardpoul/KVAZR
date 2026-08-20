using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.Core.Model;
using Maxwell.Core.Services;
using Maxwell.Core.Settings;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>One saved profile as the list shows it.</summary>
public sealed partial class ProfileRow : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string TriggerProcess { get; set; }

    public DeviceProfile Profile { get; }

    public string Summary => Profile.SettingCount == 0
        ? "пусто — нечего применять"
        : string.Create(CultureInfo.InvariantCulture, $"{Profile.SettingCount} настроек");

    public string TriggerText => string.IsNullOrWhiteSpace(TriggerProcess)
        ? "только вручную"
        : TriggerProcess;

    public ProfileRow(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Profile = profile;
        Name = profile.Name;
        TriggerProcess = profile.TriggerProcess;
    }

    partial void OnNameChanged(string value) => Profile.Name = value;

    partial void OnTriggerProcessChanged(string value)
    {
        Profile.TriggerProcess = value;
        OnPropertyChanged(nameof(TriggerText));
    }
}

public sealed partial class ProfilesViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IProfileService _profiles;

    public override string Title => "Профили";

    public override bool RequiresControl => false;

    public ObservableCollection<ProfileRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool AutoSwitch { get; set; }

    public ProfilesViewModel(IDeviceService device, ISettingsStore settings, IProfileService profiles)
        : base(device)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profiles);

        _settings = settings;
        _profiles = profiles;

        _settings.Changed += OnSettingsChanged;
        Rebuild();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();
        foreach (DeviceProfile profile in _settings.Current.Profiles) Rows.Add(new ProfileRow(profile));

        AutoSwitch = _settings.Current.AutoSwitchProfiles;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public bool IsEmpty => Rows.Count == 0;

    partial void OnAutoSwitchChanged(bool value)
    {
        if (_settings.Current.AutoSwitchProfiles == value) return;

        _settings.Current.AutoSwitchProfiles = value;
        _ = _settings.SaveAsync();
    }

    /// <summary>Capture whatever the headset currently reports as a new profile.</summary>
    [RelayCommand]
    private void CaptureCurrent()
    {
        if (!State.CanControl)
        {
            Status = "Нужен кабель — по донглу настройки не читаются.";
            return;
        }

        string name = string.Create(CultureInfo.InvariantCulture, $"Профиль {Rows.Count + 1}");
        DeviceProfile profile = DeviceProfile.FromState(name, State);

        _settings.Current.Profiles.Add(profile);
        _ = _settings.SaveAsync();

        Status = string.Create(CultureInfo.InvariantCulture,
            $"Сохранено {profile.SettingCount} настроек из текущего состояния.");
    }

    [RelayCommand]
    private async Task ApplyAsync(ProfileRow? row)
    {
        if (row is null) return;

        if (!State.CanControl)
        {
            Status = "Нужен кабель — по донглу настройки не пишутся.";
            return;
        }

        int applied = await _profiles.ApplyAsync(row.Profile).ConfigureAwait(true);

        Status = applied == row.Profile.SettingCount
            ? string.Create(CultureInfo.InvariantCulture, $"Профиль «{row.Name}» применён.")
            : string.Create(CultureInfo.InvariantCulture,
                $"Применено {applied} из {row.Profile.SettingCount} — остальное устройство не приняло.");
    }

    [RelayCommand]
    private void Delete(ProfileRow? row)
    {
        if (row is null) return;

        _settings.Current.Profiles.Remove(row.Profile);
        _ = _settings.SaveAsync();
        Status = string.Create(CultureInfo.InvariantCulture, $"Профиль «{row.Name}» удалён.");
    }

    [RelayCommand]
    private void Save()
    {
        // Names and triggers are edited in place on the underlying profile objects.
        _ = _settings.SaveAsync();
        Status = "Изменения сохранены.";
    }

    public override void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        base.Dispose();
    }
}
