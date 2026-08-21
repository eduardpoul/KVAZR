using System.Collections.ObjectModel;
using System.Collections.Specialized;

using CommunityToolkit.Mvvm.ComponentModel;

using Maxwell.App.Infrastructure;
using Maxwell.App.ViewModels.Devices;

namespace Maxwell.App.ViewModels;

/// <summary>
/// The "Все устройства" page.
///
/// Shares the shell's list rather than copying it, so the dashboard and the navigation
/// pane can never disagree about what is attached, and splits that one list into the two
/// groups artboard 1a asks for. The split is not decoration: a device that is not here
/// right now still has a row, and putting it under its own heading is what stops it
/// reading as a device that has gone wrong.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    internal DashboardViewModel(
        ShellViewModel shell,
        ObservableCollection<DeviceListItemViewModel> devices,
        LoadingState loading)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(loading);

        Shell = shell;
        Devices = devices;
        Loading = loading;

        Devices.CollectionChanged += OnDevicesChanged;
        Regroup();
    }

    /// <summary>
    /// The shell, reachable from a row so acting on a device does not need the row to
    /// own a service. Hiding a device changes what several other surfaces show, which
    /// makes it the shell's business rather than the row's.
    /// </summary>
    public ShellViewModel Shell { get; }

    /// <summary>Every device, in the shell's order. The groups below are views onto it.</summary>
    public ObservableCollection<DeviceListItemViewModel> Devices { get; }

    public ObservableCollection<DeviceListItemViewModel> Connected { get; } = [];

    public ObservableCollection<DeviceListItemViewModel> Disconnected { get; } = [];

    /// <summary>
    /// Whether the first sweep has answered yet. The dashboard has three different
    /// nothings — still looking, looked and found none, and gave up — and the identity
    /// gives each its own screen. Collapsing them into one empty card is what makes a
    /// cold start look broken.
    /// </summary>
    internal LoadingState Loading { get; }

    /// <summary>Drives the first-launch state, which is an introduction and not an error.</summary>
    public bool IsEmpty => Devices.Count == 0;

    /// <summary>
    /// The line at the top right: when these numbers were last true.
    ///
    /// It is there because every reading on this page is a memory rather than a
    /// measurement — the devices are asked on a timer, not watched — and a page full of
    /// percentages with no timestamp invites the reader to believe otherwise. The newest
    /// answer is the one quoted, since it is the most flattering thing the page can
    /// honestly say about its own freshness.
    /// </summary>
    public string Freshness =>
        Devices.Select(d => d.Snapshot.UpdatedAt).OfType<DateTimeOffset>().DefaultIfEmpty().Max() is
            { } newest && newest != default
            ? $"обновлено {newest:HH:mm}"
            : string.Empty;

    public bool HasFreshness => Freshness.Length > 0;

    public bool HasConnected => Connected.Count > 0;

    public bool HasDisconnected => Disconnected.Count > 0;

    /// <summary>
    /// The counts sit in the headings, as the artboard has them. A heading that says
    /// only "ПОДКЛЮЧЕНЫ" makes the reader count the rows to answer a question the screen
    /// could have answered.
    /// </summary>
    public string ConnectedHeading => $"ПОДКЛЮЧЕНЫ · {Connected.Count}";

    public string DisconnectedHeading => $"ОТКЛЮЧЕНЫ · {Disconnected.Count}";

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Regroup();

    private void Regroup()
    {
        Fill(Connected, Devices.Where(d => d.IsPresent));
        Fill(Disconnected, Devices.Where(d => !d.IsPresent));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasConnected));
        OnPropertyChanged(nameof(HasDisconnected));
        OnPropertyChanged(nameof(ConnectedHeading));
        OnPropertyChanged(nameof(DisconnectedHeading));
        OnPropertyChanged(nameof(Freshness));
        OnPropertyChanged(nameof(HasFreshness));
    }

    /// <summary>
    /// Reconciles in place rather than clearing and refilling. Clearing drops the row
    /// under a pointer that is hovering it and restarts every animation on the screen,
    /// several times a minute, for a list that usually has not changed at all.
    /// </summary>
    private static void Fill(
        ObservableCollection<DeviceListItemViewModel> target,
        IEnumerable<DeviceListItemViewModel> source)
    {
        List<DeviceListItemViewModel> wanted = [.. source];

        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            int at = target.IndexOf(wanted[i]);

            if (at < 0) target.Insert(i, wanted[i]);
            else if (at != i) target.Move(at, i);
        }
    }
}
