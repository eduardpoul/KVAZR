using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvazr.App.Infrastructure;
using Kvazr.App.Platform;
using Kvazr.App.ViewModels.Devices;
using Kvazr.Core.Devices;
using Kvazr.Core.Polling;

namespace Kvazr.App.ViewModels;

/// <summary>
/// The panel that drops out of the tray.
///
/// Every device is the same 56-pixel row — picture, name, link, battery — and none of
/// them starts open. Turn 12 is a correction to the earlier arrangement, where one
/// device sat at the top with a picture and controls while everything else was a bare
/// row: the trouble was not how it looked but that the panel was choosing which device
/// mattered, by a rule that is right about as often as it is wrong.
///
/// Opening one row closes the others. Not to save space, though it does: the panel
/// answers two questions — "what is my battery" at a glance, and "nudge this one thing"
/// in a click — and two open rows means neither is being answered.
/// </summary>
public sealed partial class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceRegistry _registry;
    private readonly HiddenDevices _hidden;
    private readonly IPollingCoordinator _polling;
    private readonly TrayExtras _extras;

    private bool _disposed;

    public FlyoutViewModel(
        IDeviceRegistry registry,
        IPollingCoordinator polling,
        HiddenDevices hidden,
        FirstSweep sweep,
        TrayExtras extras)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(polling);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(extras);

        _registry = registry;
        _polling = polling;
        _hidden = hidden;
        _extras = extras;

        // Its own gate rather than the shell's: the panel can be opened before the
        // window has ever been built, and a gate that starts when the window is
        // constructed would already have expired by then.
        Loading.Begin();
        sweep.WhenComplete(Loading.Resolve);

        _registry.DevicesChanged += OnDevicesChanged;
        _hidden.Changed += OnDevicesChanged;

        // The switch that decides this is on a device's page, and the panel is a different
        // surface entirely. Without this an open row would go on showing what the setting
        // said when it was opened.
        _extras.Changed += OnExtrasChanged;
        Sync();
    }

    /// <summary>Raised when the user asks for the full window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised with a device key when the user asks for that device's page.</summary>
    public event EventHandler<string>? DeviceRequested;

    /// <summary>
    /// Raised with the row that has just opened, so the window can bring it into view.
    ///
    /// The list is five rows tall and the panel does not grow, so opening the fourth row
    /// puts its controls under the footer — visible enough to know something happened and
    /// not enough to use. Only the window can scroll; only the panel knows what opened.
    /// </summary>
    public event EventHandler<FlyoutRowViewModel>? RowExpanded;

    /// <summary>Every visible device, in the order the registry gives them.</summary>
    public ObservableCollection<FlyoutRowViewModel> Devices { get; } = [];

    /// <summary>
    /// A row to open by a piece of its name, held until it exists.
    ///
    /// The development flag arrives moments after start-up and the list arrives a minute
    /// later, once discovery has finished; opening whatever was there at the time meant
    /// opening nothing at all. Same reason the shell holds a pending device page.
    /// </summary>
    public string? PendingOpenName { get; set; }

    /// <summary>Whether the first sweep has answered. Drives the placeholder rows.</summary>
    internal LoadingState Loading { get; } = new();

    public bool HasDevices => Devices.Count > 0;

    /// <summary>
    /// Whether any row is open, which is what the list's height turns on.
    ///
    /// An opened row is taller than the five-row list the panel is otherwise held to, and
    /// the part that fell off the bottom was the row's own way to the device page.
    /// </summary>
    public bool HasOpenRow => Devices.Any(d => d.IsOpen);

    public string Heading => Devices.Count switch
    {
        0 or 1 => Kvazr.App.Localization.Localizer.Current.Say("Devices"),
        int n => Kvazr.App.Localization.Localizer.Current.Say("Devices · {0}", n),
    };

    // The panel's three fixed lines. Properties rather than words in the markup, because
    // markup literals are the one place in this application the catalogue cannot reach:
    // XAML has no way to call a method, so a sentence written there is a sentence in one
    // language for good.

    /// <summary>The pinned row at the foot: the way from the panel into the window.</summary>
    public string SettingsLabel => Kvazr.App.Localization.Localizer.Current.Say("All settings");

    /// <summary>Ten seconds with no answer from the poll.</summary>
    public string StalledLabel => Kvazr.App.Localization.Localizer.Current.Say("The poll is not answering");

    /// <summary>Nothing attached. Short: the tray is not the place to explain.</summary>
    public string EmptyLabel => Kvazr.App.Localization.Localizer.Current.Say("No devices found");

    /// <summary>
    /// The flyout is open, so every device's battery is worth keeping fresh — and this is
    /// the moment to read each device once in full.
    ///
    /// In the tray only the battery is ever polled, deliberately: keeping every
    /// capability of every device hot means constant HID traffic, contention with the
    /// vendors' own software and radios woken on devices running off a cell. The cost of
    /// that policy landed on the first row anybody opened — it had never been read, so it
    /// came up with names and no values and filled in a second later, which reads as a
    /// broken panel rather than as a considered trade.
    ///
    /// One full read per device per session settles it. The values are kept in the
    /// snapshot afterwards — kilobytes, never evicted — so every later opening draws
    /// immediately from memory and the refresh merely corrects it.
    /// </summary>
    public void Opened()
    {
        _polling.SetAttention(PollingAttention.Glance);

        foreach (FlyoutRowViewModel row in Devices)
        {
            if (!_warmed.Add(row.Key)) continue;

            _polling.RequestRefresh(row.Key);
        }
    }

    /// <summary>Devices read in full at least once, so the warming happens only once each.</summary>
    private readonly HashSet<string> _warmed = new(StringComparer.Ordinal);

    public void Closed()
    {
        _polling.SetAttention(PollingAttention.Idle);

        // Closing the panel closes whatever was open inside it. Otherwise a row keeps
        // holding controls nobody can see, and the panel reopens in a state the user
        // left minutes ago and has since forgotten about.
        foreach (FlyoutRowViewModel row in Devices) row.Close();
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void TryOpenPending()
    {
        if (PendingOpenName is not { Length: > 0 } wanted) return;

        FlyoutRowViewModel? row = Devices.FirstOrDefault(
            d => d.Device.DisplayName.Contains(wanted, StringComparison.CurrentCultureIgnoreCase));

        // A device turns up in the list before it has been described: discovery publishes
        // it, the session connects a moment later and publishes again with what it can
        // do. Opening on the first of those gives a row with nothing in it, which is what
        // the flag was written to look at.
        if (row is null || row.Device.Snapshot.Capabilities.Count == 0) return;

        PendingOpenName = null;
        row.ToggleCommand.Execute(null);
    }

    /// <summary>An open row is rebuilt by closing it: its controls are made when it opens.</summary>
    private void OnExtrasChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        foreach (FlyoutRowViewModel row in Devices) row.Close();
    });

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        Loading.Resolve();
        Sync();
    });

    private void Sync()
    {
        List<DeviceSnapshot> visible = [.. _registry.Devices.Where(_hidden.IsVisible)];

        foreach (DeviceSnapshot snapshot in visible)
        {
            FlyoutRowViewModel? existing = Devices.FirstOrDefault(d => d.Key == snapshot.Info.Key);

            if (existing is not null)
            {
                existing.Update(snapshot);
                continue;
            }

            var row = new FlyoutRowViewModel(snapshot, _registry, _extras);
            row.Expanded += OnRowExpanded;
            row.PageRequested += OnPageRequested;

            // Opening raises Expanded; closing raises nothing, and a row closes itself
            // when it is clicked a second time. Watching the property catches both ends.
            row.PropertyChanged += OnRowChanged;
            Devices.Add(row);
        }

        foreach (FlyoutRowViewModel gone in Devices.Where(d => visible.All(s => s.Info.Key != d.Key)).ToList())
        {
            Retire(gone);
            Devices.Remove(gone);
        }

        Reorder(visible);
        TryOpenPending();

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(Heading));
    }

    /// <summary>
    /// Moves rows into the registry's order in place rather than rebuilding the list, so
    /// an open row does not shut every time a poll lands.
    /// </summary>
    private void Reorder(List<DeviceSnapshot> order)
    {
        for (int target = 0; target < order.Count; target++)
        {
            int current = IndexOf(order[target].Info.Key);
            if (current >= 0 && current != target) Devices.Move(current, target);
        }
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < Devices.Count; i++)
        {
            if (Devices[i].Key == key) return i;
        }

        return -1;
    }

    /// <summary>
    /// One open row at a time, and the device just opened is the one worth reading in
    /// full: the panel's usual cadence only keeps batteries warm, so the controls that
    /// have this moment appeared would otherwise be showing values from minutes ago.
    /// </summary>
    private void OnRowExpanded(object? sender, FlyoutRowViewModel opened)
    {
        foreach (FlyoutRowViewModel row in Devices)
        {
            if (!ReferenceEquals(row, opened)) row.Close();
        }

        _polling.RequestRefresh(opened.Key);
        RowExpanded?.Invoke(this, opened);
    }

    private void OnPageRequested(object? sender, string key) => DeviceRequested?.Invoke(this, key);

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FlyoutRowViewModel.IsOpen)) OnPropertyChanged(nameof(HasOpenRow));
    }

    private void Retire(FlyoutRowViewModel row)
    {
        row.Expanded -= OnRowExpanded;
        row.PageRequested -= OnPageRequested;
        row.PropertyChanged -= OnRowChanged;
        row.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DevicesChanged -= OnDevicesChanged;
        _hidden.Changed -= OnDevicesChanged;
        _extras.Changed -= OnExtrasChanged;
        Loading.Dispose();

        foreach (FlyoutRowViewModel row in Devices) Retire(row);
        Devices.Clear();
    }
}
