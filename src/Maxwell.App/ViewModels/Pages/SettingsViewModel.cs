using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Maxwell.App.Platform;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// Everything that belongs to the application rather than to any device: artboard 7a,
/// plus the hidden-devices section from 13b.
///
/// Every property here writes straight through to the store and saves. There is no Apply
/// button and no Cancel, which is the convention the rest of Windows settles on and also
/// the honest one: a switch that has visibly moved but has not taken effect is a lie the
/// screen is telling, and the moment there is an Apply button somebody will close the
/// window without pressing it.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly IAutostart _autostart;
    private readonly IDeviceRegistry _registry;
    private readonly HiddenDevices _hidden;

    /// <summary>
    /// Set while a change is being written back, so the store's own change notification
    /// does not come back round and re-enter every setter. Without it, saving one value
    /// re-applies all of them, which turns a single toggle into a full rewrite of the file.
    /// </summary>
    private bool _applying;

    private bool _disposed;

    public SettingsViewModel(
        ISettingsStore store,
        IAutostart autostart,
        IDeviceRegistry registry,
        HiddenDevices hidden)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(autostart);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(hidden);

        _store = store;
        _autostart = autostart;
        _registry = registry;
        _hidden = hidden;

        _hidden.Changed += OnHiddenChanged;
        RefreshHidden();
    }

    public string Title => "Параметры";

    // ---------- appearance ----------

    public int ThemeIndex
    {
        get => (int)_store.Current.Theme;
        set => Write(s => s.Theme = (AppTheme)value);
    }

    public IReadOnlyList<string> Languages { get; } = ["Русский", "English"];

    public int LanguageIndex
    {
        get => _store.Current.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set => Write(s => s.Language = value == 1 ? "en" : "ru");
    }

    // ---------- start and tray ----------

    public bool RunAtLogin
    {
        get => _store.Current.RunAtLogin;
        set
        {
            Write(s => s.RunAtLogin = value);

            // The registry entry is the actual setting; the stored flag only remembers
            // what we asked for. Writing one without the other leaves a checkbox that
            // disagrees with what Windows will do at the next login.
            if (value) _autostart.Enable(_store.Current.StartMinimised);
            else _autostart.Disable();
        }
    }

    public bool MinimiseOnClose
    {
        get => _store.Current.MinimiseOnClose;
        set => Write(s => s.MinimiseOnClose = value);
    }

    // ---------- notifications and polling ----------

    public int LowBatteryThreshold
    {
        get => _store.Current.LowBatteryThreshold;
        set => Write(s => s.LowBatteryThreshold = value);
    }

    public string LowBatteryLabel => $"{LowBatteryThreshold}%";

    public bool ShowConnectPopup
    {
        get => _store.Current.ShowConnectPopup;
        set => Write(s => s.ShowConnectPopup = value);
    }

    /// <summary>
    /// The intervals offered. Not a free number: the difference between 15 and 17 seconds
    /// is meaningless, and a text box invites somebody to type 1 and wonder later why the
    /// battery is going flat faster than it used to.
    /// </summary>
    public IReadOnlyList<string> PollIntervals { get; } =
        ["Каждые 10 с", "Каждые 30 с", "Каждую минуту", "Каждые 5 минут"];

    private static readonly int[] PollSeconds = [10, 30, 60, 300];

    public int PollIntervalIndex
    {
        get
        {
            int seconds = _store.Current.PollingSeconds;
            int at = Array.IndexOf(PollSeconds, seconds);
            return at >= 0 ? at : 1;
        }

        set => Write(s => s.PollingSeconds = PollSeconds[Math.Clamp(value, 0, PollSeconds.Length - 1)]);
    }

    // ---------- hidden devices, artboard 13b ----------

    public ObservableCollection<HiddenDeviceViewModel> Hidden { get; } = [];

    public bool HasHidden => Hidden.Count > 0;

    public string HiddenHeading => Hidden.Count > 0 ? $"СКРЫТЫЕ · {Hidden.Count}" : "СКРЫТЫЕ";

    public bool HideSilentDevices
    {
        get => _store.Current.HideSilentDevices;
        set => Write(s => s.HideSilentDevices = value);
    }

    [RelayCommand]
    private async Task ShowAllHiddenAsync() => await _hidden.ShowAllAsync().ConfigureAwait(true);

    private void OnHiddenChanged(object? sender, EventArgs e) => RefreshHidden();

    private void RefreshHidden()
    {
        Hidden.Clear();

        foreach ((DeviceSnapshot device, DateTimeOffset at) in _hidden.List(_registry))
        {
            Hidden.Add(new HiddenDeviceViewModel(device, at, _hidden));
        }

        OnPropertyChanged(nameof(HasHidden));
        OnPropertyChanged(nameof(HiddenHeading));
        OnPropertyChanged(nameof(HideSilentDevices));
    }

    /// <summary>
    /// Applies a change and persists it. Fire-and-forget on purpose: a settings toggle
    /// that awaited a file write would block the interface on a disk, and there is
    /// nothing useful to do about a failure that the next change will not also try.
    /// </summary>
    private void Write(Action<AppSettings> change)
    {
        if (_applying) return;

        _applying = true;

        try
        {
            change(_store.Current);
            _ = _store.SaveAsync();
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hidden.Changed -= OnHiddenChanged;
    }
}

/// <summary>One row in the hidden list: what it was, when it went away, and the way back.</summary>
public sealed partial class HiddenDeviceViewModel : ObservableObject
{
    private readonly HiddenDevices _hidden;

    public HiddenDeviceViewModel(DeviceSnapshot device, DateTimeOffset hiddenAt, HiddenDevices hidden)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(hidden);

        Device = device;
        HiddenAt = hiddenAt;
        _hidden = hidden;
    }

    public DeviceSnapshot Device { get; }

    public DateTimeOffset HiddenAt { get; }

    public string DisplayName => Device.Info.DisplayName;

    public string Kind => Device.Info.Class == DeviceClass.Unknown ? "Класс не определён" : Device.Info.Class.ToString();

    /// <summary>
    /// "скрыто 18 августа". A date rather than "3 days ago", because the list is read
    /// months later and a relative time turns into arithmetic the reader has to do.
    /// </summary>
    public string When => $"скрыто {HiddenAt:d MMMM}";

    [RelayCommand]
    private async Task RestoreAsync() => await _hidden.ShowAsync(Device.Info.Key).ConfigureAwait(true);
}
