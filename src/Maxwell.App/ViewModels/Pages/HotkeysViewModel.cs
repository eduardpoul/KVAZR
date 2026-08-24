using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Maxwell.App.Platform;
using Maxwell.Core.Settings;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// The global shortcuts, one row per action.
///
/// Three things can be wrong with a binding and the screen has to tell them apart,
/// because the fix is different for each. It can be unset, which is fine and common. It
/// can collide with another row here, which is ours to resolve. Or the operating system
/// can have refused it because another application registered the combination first —
/// nothing about that is visible from inside this process except the refusal, so the row
/// has to say so rather than showing a binding that silently never fires.
/// </summary>
public sealed partial class HotkeysViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly HotkeyCoordinator _coordinator;

    private bool _disposed;

    public HotkeysViewModel(ISettingsStore store, HotkeyCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(coordinator);

        _store = store;
        _coordinator = coordinator;

        foreach (HotkeyAction action in HotkeyActions.All)
        {
            var row = new HotkeyRowViewModel(action, _store.Current.Hotkeys.GetValueOrDefault(action.Id, string.Empty));
            row.Requested += OnRowRequested;
            Rows.Add(row);
        }

        Revalidate();
    }

    public string Title => Maxwell.App.Localization.Localizer.Current["page.hotkeys", "Горячие клавиши"];

    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = [];

    /// <summary>The row currently waiting for a key press, or null when none is.</summary>
    public HotkeyRowViewModel? Recording { get; private set; }

    public bool IsRecording => Recording is not null;

    /// <summary>
    /// Accepts a captured combination for whichever row is listening.
    ///
    /// Called from the view, because key capture is a windowing concern and there is no
    /// way to observe a global-looking key press from a view model. What comes back here
    /// is already a canonical gesture string.
    /// </summary>
    public void Capture(string gesture)
    {
        if (Recording is not { } row) return;

        row.Gesture = gesture;
        Stop();
        Persist();
    }

    /// <summary>Abandons a capture without changing anything. Escape, or a click elsewhere.</summary>
    public void Stop()
    {
        if (Recording is { } row) row.IsRecording = false;

        Recording = null;
        OnPropertyChanged(nameof(IsRecording));
    }

    [RelayCommand]
    private void Clear(HotkeyRowViewModel? row)
    {
        if (row is null) return;

        row.Gesture = string.Empty;
        Persist();
    }

    private void OnRowRequested(object? sender, HotkeyRowViewModel row)
    {
        // One at a time. Two rows listening at once would both take the same key press,
        // and whichever won would be down to event order.
        Stop();

        Recording = row;
        row.IsRecording = true;
        OnPropertyChanged(nameof(IsRecording));
    }

    private void Persist()
    {
        foreach (HotkeyRowViewModel row in Rows)
        {
            _store.Current.Hotkeys[row.Action.Id] = row.Gesture;
        }

        _ = _store.SaveAsync();

        // Re-registering is what turns a refusal into something the screen can show: the
        // operating system only says no at the moment of registration.
        _coordinator.Apply();
        Revalidate();
    }

    private void Revalidate()
    {
        var seen = new Dictionary<string, HotkeyRowViewModel>(StringComparer.OrdinalIgnoreCase);
        var rejected = _coordinator.Rejected.ToHashSet(StringComparer.Ordinal);

        foreach (HotkeyRowViewModel row in Rows)
        {
            row.IsConflicting = false;
            row.IsRejected = rejected.Contains(row.Action.Id);
        }

        foreach (HotkeyRowViewModel row in Rows.Where(r => r.HasGesture))
        {
            if (seen.TryGetValue(row.Gesture, out HotkeyRowViewModel? other))
            {
                row.IsConflicting = true;
                other.IsConflicting = true;
                continue;
            }

            seen[row.Gesture] = row;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (HotkeyRowViewModel row in Rows) row.Requested -= OnRowRequested;
    }
}

/// <summary>One action and the combination bound to it.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    public HotkeyRowViewModel(HotkeyAction action, string gesture)
    {
        ArgumentNullException.ThrowIfNull(action);

        Action = action;
        _gesture = gesture;
    }

    /// <summary>Raised when this row wants to listen for a key press.</summary>
    public event EventHandler<HotkeyRowViewModel>? Requested;

    public HotkeyAction Action { get; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial bool IsConflicting { get; set; }

    /// <summary>The operating system refused this combination — another application has it.</summary>
    [ObservableProperty]
    public partial bool IsRejected { get; set; }

    private string _gesture;

    public string Gesture
    {
        get => _gesture;
        set
        {
            if (string.Equals(_gesture, value, StringComparison.Ordinal)) return;

            _gesture = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGesture));
            OnPropertyChanged(nameof(Label));
        }
    }

    public bool HasGesture => Gesture.Length > 0;

    /// <summary>
    /// What the button shows. "Не назначено" rather than an empty button, because an
    /// empty button on a row of filled ones reads as a rendering fault rather than as a
    /// deliberate absence.
    /// </summary>
    public string Label => IsRecording ? Maxwell.App.Localization.Localizer.Current.Say("Нажмите сочетание…") : HasGesture ? Gesture : Maxwell.App.Localization.Localizer.Current.Say("Не назначено");

    /// <summary>
    /// What is wrong, if anything. Empty when the binding is fine, which is most of them.
    /// </summary>
    public string Problem => (IsRejected, IsConflicting) switch
    {
        (true, _) => Maxwell.App.Localization.Localizer.Current.Say("Сочетание занято другим приложением"),
        (_, true) => Maxwell.App.Localization.Localizer.Current.Say("Это сочетание уже назначено другому действию"),
        _ => string.Empty,
    };

    public bool HasProblem => Problem.Length > 0;

    [RelayCommand]
    private void Record() => Requested?.Invoke(this, this);

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(Label));

    partial void OnIsConflictingChanged(bool value)
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
    }

    partial void OnIsRejectedChanged(bool value)
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
    }
}
