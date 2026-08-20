using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.App.Platform;
using Maxwell.Core.Services;
using Maxwell.Core.Settings;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>One row on the hotkeys page.</summary>
public sealed partial class HotkeyRow : ObservableObject
{
    [ObservableProperty]
    public partial string Gesture { get; set; }

    [ObservableProperty]
    public partial bool Rejected { get; set; }

    public string ActionId { get; }

    public string Title { get; }

    public string Description { get; }

    public string GestureText => string.IsNullOrWhiteSpace(Gesture) ? "не назначено" : Gesture;

    public HotkeyRow(HotkeyAction action, string gesture)
    {
        ArgumentNullException.ThrowIfNull(action);

        ActionId = action.Id;
        Title = action.Title;
        Description = action.Description;
        Gesture = gesture;
    }

    partial void OnGestureChanged(string value) => OnPropertyChanged(nameof(GestureText));
}

public sealed partial class HotkeysViewModel : PageViewModel
{
    private readonly ISettingsStore _settings;
    private readonly HotkeyCoordinator _coordinator;

    public override string Title => "Горячие клавиши";

    public override bool RequiresControl => false;

    public ObservableCollection<HotkeyRow> Rows { get; } = [];

    public HotkeysViewModel(IDeviceService device, ISettingsStore settings, HotkeyCoordinator coordinator)
        : base(device)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(coordinator);

        _settings = settings;
        _coordinator = coordinator;

        _settings.Changed += OnSettingsChanged;
        Rebuild();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();

        foreach (HotkeyAction action in HotkeyActions.All)
        {
            _settings.Current.Hotkeys.TryGetValue(action.Id, out string? gesture);
            Rows.Add(new HotkeyRow(action, gesture ?? "")
            {
                Rejected = _coordinator.Rejected.Contains(action.Id, StringComparer.Ordinal),
            });
        }

        OnPropertyChanged(nameof(HasRejected));
    }

    /// <summary>True when at least one shortcut is already owned by another application.</summary>
    public bool HasRejected => Rows.Any(r => r.Rejected);

    /// <summary>Assign a captured gesture, or clear it when the string is empty.</summary>
    public void Assign(HotkeyRow row, string gesture)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.Gesture = gesture;
        _settings.Current.Hotkeys[row.ActionId] = gesture;
        _ = _settings.SaveAsync();
    }

    [RelayCommand]
    private void ResetAll()
    {
        var defaults = new AppSettings();
        _settings.Current.Hotkeys = defaults.Hotkeys;
        _ = _settings.SaveAsync();
    }

    public override void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        base.Dispose();
    }
}
