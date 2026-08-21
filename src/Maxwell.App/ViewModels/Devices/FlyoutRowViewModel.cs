using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>
/// One device in the tray panel: the same row as every other device, plus whatever it
/// offers once someone opens it.
///
/// Turn 12 replaced an earlier arrangement where one device sat at the top with a
/// picture and two controls while the rest were bare rows. The trouble with that was not
/// how it looked — it was that the panel decided for the user which device mattered, and
/// it decided by a rule ("the first one that can be driven") that is right about as often
/// as it is wrong. Equal rows make no claim, and opening one is a click.
///
/// The controls are built when the row opens and thrown away when it closes. Holding a
/// live control for every device would mean holding a session for every device, and a
/// session is the sole owner of its hardware — nine of them, kept open because a panel
/// might be looked at, is a real cost paid for nothing.
/// </summary>
public sealed partial class FlyoutRowViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// What earns a control here, most wanted first. Ordered by how often somebody
    /// actually reaches for it rather than by domain: ChatMix moves during a game, an EQ
    /// preset moves weekly, a sidetone level almost never.
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
        CapabilityIds.LinkHosts,
    ];

    /// <summary>
    /// Three at most, and the ceiling is the panel's width rather than an opinion: at
    /// 360 px a fourth slider pushes the row that opened it past the height that fits.
    /// </summary>
    private const int QuickCount = 3;

    private readonly IDeviceRegistry _registry;
    private bool _disposed;

    public FlyoutRowViewModel(DeviceSnapshot snapshot, IDeviceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        Device = new DeviceListItemViewModel(snapshot);
    }

    /// <summary>Raised when this row wants the full window opened on its device.</summary>
    public event EventHandler<string>? PageRequested;

    /// <summary>Raised when this row opens, so the panel can close whichever was open.</summary>
    public event EventHandler<FlyoutRowViewModel>? Expanded;

    /// <summary>The row itself — picture, name, link, battery. Identical for every device.</summary>
    public DeviceListItemViewModel Device { get; }

    public string Key => Device.Key;

    /// <summary>Controls for this device, present only while the row is open.</summary>
    public ObservableCollection<CapabilityViewModel> Quick { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>
    /// True when the device offers nothing to change. Saying so is the point rather than
    /// a fallback: "connected, and there is nothing here to set" is something a person
    /// can act on, while an empty space under an opened row looks like a fault.
    /// </summary>
    public bool HasNothing => Quick.Count == 0;

    public void Update(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Device.Update(snapshot);

        // Readings are fed to the existing controls rather than rebuilding them, because
        // rebuilding on every poll would take the control out from under a dragging
        // finger every few seconds.
        foreach (CapabilityViewModel vm in Quick)
        {
            if (snapshot.Get(vm.Id) is { } state) vm.Apply(state);
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Open();
        Expanded?.Invoke(this, this);
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        ClearQuick();
        OnPropertyChanged(nameof(HasNothing));
    }

    private void Open()
    {
        IsOpen = true;

        DeviceSnapshot snapshot = Device.Snapshot;
        IDeviceSession? session = _registry.Session(Key);

        foreach (string id in QuickOrder)
        {
            if (Quick.Count == QuickCount) break;

            // Read-only capabilities are already on the row or on the page. A control
            // that cannot be moved earns nothing here and costs a line.
            if (snapshot.Get(id) is not { Descriptor.Access: not CapabilityAccess.Read } state) continue;

            Quick.Add(CapabilityViewModelFactory.Create(state, session, HintDetail.Terse));
        }

        OnPropertyChanged(nameof(HasNothing));
    }

    [RelayCommand]
    private void OpenPage() => PageRequested?.Invoke(this, Key);

    private void ClearQuick()
    {
        foreach (CapabilityViewModel vm in Quick) vm.Dispose();
        Quick.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClearQuick();
    }
}
