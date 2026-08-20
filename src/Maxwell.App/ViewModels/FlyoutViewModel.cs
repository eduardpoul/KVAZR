using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.App.Platform;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;
using Maxwell.Core.Polling;

namespace Maxwell.App.ViewModels;

/// <summary>
/// The panel that drops out of the tray.
///
/// The primary device is expanded with a few controls; everything else is one row. That
/// split is the point: the flyout answers "what is my battery" in a glance and "nudge
/// this one thing" in a click, and anything more belongs in the window.
/// </summary>
public sealed partial class FlyoutViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// What counts as worth a control here, most wanted first. Ordered by how often a
    /// person actually reaches for it rather than by domain — ChatMix moves during a
    /// game, an EQ preset moves weekly, a sidetone level almost never.
    /// </summary>
    private static readonly string[] QuickOrder =
    [
        CapabilityIds.MixChat,
        CapabilityIds.AudioVolume,
        CapabilityIds.AudioEqPreset,
        CapabilityIds.MicNoiseReduction,
        CapabilityIds.MicSidetone,
        CapabilityIds.PointerDpi,
        CapabilityIds.LightBrightness,
        CapabilityIds.LightOn,
    ];

    private const int QuickCount = 3;

    private readonly IDeviceRegistry _registry;
    private readonly PrimaryDevice _primary;
    private readonly IPollingCoordinator _polling;

    private string? _quickDeviceKey;
    private bool _disposed;

    public FlyoutViewModel(IDeviceRegistry registry, PrimaryDevice primary, IPollingCoordinator polling)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(polling);

        _registry = registry;
        _primary = primary;
        _polling = polling;

        _registry.DevicesChanged += OnDevicesChanged;
        Sync();
    }

    /// <summary>Raised when the user asks for the full window.</summary>
    public event EventHandler? SettingsRequested;

    [ObservableProperty]
    public partial DeviceListItemViewModel? Primary { get; private set; }

    /// <summary>Everything that is not the primary device, one row each.</summary>
    public ObservableCollection<DeviceListItemViewModel> Others { get; } = [];

    /// <summary>Two or three controls for the primary device. Never more.</summary>
    public ObservableCollection<CapabilityViewModel> Quick { get; } = [];

    public bool HasOthers => Others.Count > 0;

    public bool HasDevices => Primary is not null;

    public string Heading => _registry.Devices.Count switch
    {
        0 => "Устройства",
        1 => "Устройства",
        int n => $"Устройства · {n}",
    };

    /// <summary>
    /// The flyout is open, so every device's battery is worth keeping fresh — but none
    /// of their details are.
    /// </summary>
    public void Opened()
    {
        _polling.SetAttention(PollingAttention.Glance);

        // The quick controls need real values, and Glance only keeps batteries warm.
        // Opening the panel is exactly when reading the rest of the primary device is
        // worth its cost.
        if (Primary is { } primary) _polling.RequestRefresh(primary.Key);
    }

    public void Closed() => _polling.SetAttention(PollingAttention.Idle);

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnDevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Sync);

    private void Sync()
    {
        DeviceSnapshot? primary = _primary.Snapshot;

        if (primary is null)
        {
            Primary = null;
            ClearQuick();
            Others.Clear();
        }
        else
        {
            if (Primary?.Key == primary.Info.Key) Primary.Update(primary);
            else Primary = new DeviceListItemViewModel(primary);

            SyncQuick(primary);
            SyncOthers(primary.Info.Key);
        }

        OnPropertyChanged(nameof(HasOthers));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(Heading));
    }

    private void SyncOthers(string primaryKey)
    {
        List<DeviceSnapshot> rest = [.. _registry.Devices.Where(d => d.Info.Key != primaryKey)];

        foreach (DeviceSnapshot snapshot in rest)
        {
            DeviceListItemViewModel? existing = Others.FirstOrDefault(d => d.Key == snapshot.Info.Key);

            if (existing is null) Others.Add(new DeviceListItemViewModel(snapshot));
            else existing.Update(snapshot);
        }

        foreach (DeviceListItemViewModel gone in Others.Where(d => rest.All(s => s.Info.Key != d.Key)).ToList())
        {
            Others.Remove(gone);
        }
    }

    private void SyncQuick(DeviceSnapshot device)
    {
        // Rebuilding these on every poll would fight the user mid-drag, so the controls
        // are made once per device and fed readings after that.
        if (_quickDeviceKey == device.Info.Key)
        {
            foreach (CapabilityViewModel vm in Quick)
            {
                if (device.Get(vm.Id) is { } state) vm.Apply(state);
            }

            return;
        }

        ClearQuick();
        _quickDeviceKey = device.Info.Key;

        IDeviceSession? session = _registry.Session(device.Info.Key);

        foreach (string id in QuickOrder)
        {
            if (Quick.Count == QuickCount) break;
            if (device.Get(id) is not { Descriptor.Access: not CapabilityAccess.Read } state) continue;

            Quick.Add(CapabilityViewModelFactory.Create(state, session, HintDetail.Terse));
        }
    }

    private void ClearQuick()
    {
        foreach (CapabilityViewModel vm in Quick) vm.Dispose();

        Quick.Clear();
        _quickDeviceKey = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DevicesChanged -= OnDevicesChanged;
        ClearQuick();
    }
}
