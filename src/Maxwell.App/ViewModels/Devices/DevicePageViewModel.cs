using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.ViewModels.Capabilities;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Maxwell.App.Imaging;
using Maxwell.Core.Devices;
using Maxwell.Core.Polling;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>
/// A device page, assembled from what the device says it can do.
///
/// There is exactly one of these for every device of every vendor. No page is written
/// per model, per class or per driver — the sections are the capability domains, the
/// rows are the capabilities, and a device that lacks a domain simply has no such
/// section. This is what the whole capability model was for.
/// </summary>
public sealed partial class DevicePageViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceSession _session;
    private readonly IPollingCoordinator? _polling;
    private readonly IConfirmationPrompt? _confirm;
    private readonly Dictionary<string, CapabilityViewModel> _byId = new(StringComparer.Ordinal);

    private bool _disposed;

    public DevicePageViewModel(
        IDeviceSession session,
        IPollingCoordinator? polling = null,
        IConfirmationPrompt? confirm = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _polling = polling;
        _confirm = confirm;

        Snapshot = session.Snapshot;
        Rebuild(session.Snapshot);

        _session.SnapshotChanged += OnSnapshotChanged;
    }

    [ObservableProperty]
    public partial DeviceSnapshot Snapshot { get; private set; }

    public ObservableCollection<CapabilitySectionViewModel> Sections { get; } = [];

    public string DeviceKey => _session.DeviceKey;

    public string Title => Snapshot.Info.DisplayName;

    public IReadOnlyList<TransportInfo> Transports => Snapshot.Info.Transports;

    public string? ImageKey => Snapshot.Info.ImageKey;

    /// <summary>
    /// The curated photograph for this model, or null when there is none — which is the
    /// ordinary case. The tile falls back to <see cref="Glyph"/>, not to an empty box.
    /// </summary>
    public Bitmap? Photo => DeviceImages.Photo(Snapshot.Info.ImageKey);

    /// <summary>The silhouette for this device's class. Every class has one.</summary>
    public Geometry? Glyph => DeviceImages.Glyph(Snapshot.Info.Class);

    /// <summary>Battery as text. A dash, never a zero, when nothing has been read.</summary>
    public string Battery => Snapshot.BatteryPercent is { } percent ? $"{percent}%" : "—";

    public bool IsCharging => Snapshot.IsCharging;

    /// <summary>
    /// The one line at the top explaining a degraded device. Absent when everything
    /// works, and never phrased as an error — a headset reachable only over its dongle
    /// is being used exactly as intended.
    /// </summary>
    public string? StateNotice => Snapshot switch
    {
        // Answering, but over a link that carries no control. Over Bluetooth the Maxwell
        // publishes no HID service at all, so the short list of capabilities is the
        // truth about this link rather than something failing.
        { Availability: DeviceAvailability.Active, Info.ControlTransport: null } =>
            "Подключено по Bluetooth: доступен только заряд. Настройка работает через донгл или кабель",

        _ => Snapshot.Availability switch
        {
        DeviceAvailability.Unreachable =>
            "Наушники не отвечают. Включите их или поднесите ближе — управление работает и по донглу, и по кабелю",
        DeviceAvailability.Offline =>
            "Устройство не подключено. Показаны последние известные значения",
            _ => null,
        },
    };

    /// <summary>"обновлено 14:41", or when the device was last seen if it is gone.</summary>
    public string? Freshness => Snapshot switch
    {
        { Availability: DeviceAvailability.Offline, LastSeenAt: { } seen } => $"последний раз — {seen:HH:mm}",
        { UpdatedAt: { } updated } => $"обновлено {updated:HH:mm}",
        _ => null,
    };

    /// <summary>
    /// Tells the scheduler this page is open, so the device is read often enough and its
    /// volatile values — the wheel, the DPI button — stay in step with the hardware.
    /// </summary>
    public void Activate()
    {
        _polling?.SetAttention(PollingAttention.Focused, DeviceKey);
        _polling?.RequestRefresh(DeviceKey);
    }

    public void Deactivate() => _polling?.SetAttention(PollingAttention.Idle);

    private void OnSnapshotChanged(object? sender, DeviceSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() => Apply(snapshot));

    private void Apply(DeviceSnapshot snapshot)
    {
        Snapshot = snapshot;

        // Rebuild only when the set of capabilities actually changed — a reconnect can
        // bring a different list, a poll never does. Rebuilding on every reading would
        // throw away control state mid-drag.
        if (!SameShape(snapshot))
        {
            Rebuild(snapshot);
        }
        else
        {
            foreach ((string id, CapabilityState state) in snapshot.Capabilities)
            {
                if (_byId.TryGetValue(id, out CapabilityViewModel? vm)) vm.Apply(state);
            }
        }

        RaiseHeader();
    }

    private bool SameShape(DeviceSnapshot snapshot) =>
        snapshot.Capabilities.Count == _byId.Count && snapshot.Capabilities.Keys.All(_byId.ContainsKey);

    private void Rebuild(DeviceSnapshot snapshot)
    {
        foreach (CapabilitySectionViewModel section in Sections) section.Dispose();
        Sections.Clear();
        _byId.Clear();

        foreach (CapabilityDomain domain in snapshot.Domains())
        {
            List<CapabilityViewModel> items = [];

            foreach (CapabilityState state in snapshot.InDomain(domain))
            {
                CapabilityViewModel vm = CapabilityViewModelFactory.Create(
                    state, _session, HintDetail.Normal, _confirm);
                _byId[state.Id] = vm;
                items.Add(vm);
            }

            if (items.Count > 0) Sections.Add(new CapabilitySectionViewModel(domain, items));
        }
    }

    private void RaiseHeader()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ImageKey));
        OnPropertyChanged(nameof(Battery));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Transports));
        OnPropertyChanged(nameof(StateNotice));
        OnPropertyChanged(nameof(Freshness));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _session.SnapshotChanged -= OnSnapshotChanged;

        foreach (CapabilitySectionViewModel section in Sections) section.Dispose();
        Sections.Clear();
        _byId.Clear();
    }
}
