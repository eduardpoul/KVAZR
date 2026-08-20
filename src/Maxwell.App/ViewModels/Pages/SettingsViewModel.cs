using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Services;
using Maxwell.Core.Settings;

namespace Maxwell.App.ViewModels.Pages;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IAutostart _autostart;

    private bool _loading;

    public override string Title => "Параметры";

    public override bool RequiresControl => false;

    [ObservableProperty] public partial AppTheme Theme { get; set; }
    [ObservableProperty] public partial bool RunAtLogin { get; set; }
    [ObservableProperty] public partial bool StartMinimised { get; set; }
    [ObservableProperty] public partial bool ShowPercentageInTray { get; set; }
    [ObservableProperty] public partial bool ShowConnectPopup { get; set; }
    [ObservableProperty] public partial bool NotifyLowBattery { get; set; }
    [ObservableProperty] public partial bool NotifyChargeComplete { get; set; }
    [ObservableProperty] public partial double LowBatteryThreshold { get; set; }

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public string LowBatteryThresholdText =>
        ((int)Math.Round(LowBatteryThreshold)).ToString(CultureInfo.InvariantCulture) + " %";

    public SettingsViewModel(IDeviceService device, ISettingsStore settings, IAutostart autostart)
        : base(device)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(autostart);

        _settings = settings;
        _autostart = autostart;

        _settings.Changed += OnSettingsChanged;
        Load(settings.Current);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => Load(settings);

    private void Load(AppSettings settings)
    {
        // Loading must not write back, or every load would queue a redundant save.
        _loading = true;
        try
        {
            Theme = settings.Theme;
            StartMinimised = settings.StartMinimised;
            ShowPercentageInTray = settings.ShowPercentageInTray;
            ShowConnectPopup = settings.ShowConnectPopup;
            NotifyLowBattery = settings.NotifyLowBattery;
            NotifyChargeComplete = settings.NotifyChargeComplete;
            LowBatteryThreshold = settings.LowBatteryThreshold;

            // The registry is the truth for autostart, not our own file: the user may
            // have removed the entry with another tool since we last wrote it.
            RunAtLogin = _autostart.IsEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnThemeChanged(AppTheme value) => Persist(s => s.Theme = value);
    partial void OnShowPercentageInTrayChanged(bool value) => Persist(s => s.ShowPercentageInTray = value);
    partial void OnShowConnectPopupChanged(bool value) => Persist(s => s.ShowConnectPopup = value);
    partial void OnNotifyLowBatteryChanged(bool value) => Persist(s => s.NotifyLowBattery = value);
    partial void OnNotifyChargeCompleteChanged(bool value) => Persist(s => s.NotifyChargeComplete = value);

    partial void OnLowBatteryThresholdChanged(double value)
    {
        OnPropertyChanged(nameof(LowBatteryThresholdText));
        Persist(s => s.LowBatteryThreshold = (int)Math.Round(value));
    }

    partial void OnRunAtLoginChanged(bool value)
    {
        if (_loading) return;

        if (value) _autostart.Enable(StartMinimised);
        else _autostart.Disable();

        Persist(s => s.RunAtLogin = value);
    }

    partial void OnStartMinimisedChanged(bool value)
    {
        if (_loading) return;

        // The flag lives in the registry command line, so a change has to be rewritten.
        if (RunAtLogin) _autostart.Enable(value);

        Persist(s => s.StartMinimised = value);
    }

    private void Persist(Action<AppSettings> change)
    {
        if (_loading) return;

        change(_settings.Current);
        _ = _settings.SaveAsync();
    }

    public override void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        base.Dispose();
    }
}
