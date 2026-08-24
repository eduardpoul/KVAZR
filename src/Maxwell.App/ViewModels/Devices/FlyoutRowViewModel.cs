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
    /// What an opened row may spend, in lines.
    ///
    /// Counting controls was the rule before, and it stopped at three because three
    /// sliders is what fits in a 360-pixel panel. But a slider is two lines — its caption
    /// and the track under it — while a dropdown or a segmented switch is one, and
    /// refusing those on the same count left the panel with room it would not use. Six
    /// lines is the same height, spent on whatever the device actually offers.
    /// </summary>
    private const int QuickLines = 6;

    /// <summary>Two lines for a control with a track under it, one for everything else.</summary>
    private static int LinesFor(CapabilityPrimitive primitive) =>
        primitive is CapabilityPrimitive.Range or CapabilityPrimitive.RangeGroup ? 2 : 1;

    private readonly IDeviceRegistry _registry;
    private readonly Platform.TrayExtras? _extras;
    private bool _disposed;

    public FlyoutRowViewModel(
        DeviceSnapshot snapshot,
        IDeviceRegistry registry,
        Platform.TrayExtras? extras = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        _extras = extras;
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

    /// <summary>
    /// What the empty row says. "Nothing to set" and "not over this link" are different
    /// facts and the second one is actionable: a Maxwell on its dongle is not a device
    /// without settings, it is a device whose settings are one cable away. The reason
    /// comes from the capability that was skipped rather than being written here, because
    /// the driver is what knows why.
    /// </summary>
    public string NothingSaid => _blocked is { Length: > 0 } reason
        ? reason
        : Maxwell.App.Localization.Localizer.Current.Say("Nothing to set: the device gives only what the row already shows.");

    /// <summary>Why the quick controls were left out, if they were.</summary>
    private string? _blocked;

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
        _blocked = null;

        DeviceSnapshot snapshot = Device.Snapshot;
        IDeviceSession? session = _registry.Session(Key);

        int lines = 0;
        var already = new HashSet<string>(StringComparer.Ordinal);

        foreach (string id in Wanted(snapshot))
        {
            // The wanted list is built from two halves — what this kind of device leads
            // with, and the shared order — and a control can honestly be in both. Drawing
            // it twice is what that looks like, and a test caught a microphone with two
            // identical switches in its row.
            if (!already.Add(id)) continue;

            // Read-only capabilities are already on the row or on the page. A control
            // that cannot be moved earns nothing here and costs a line.
            if (snapshot.Get(id) is not { Descriptor.Access: not CapabilityAccess.Read } state) continue;

            if (lines + LinesFor(state.Descriptor.Primitive) > QuickLines) break;

            // Out of reach. A control that cannot be drawn leaves its name standing
            // alone, which is what made an opened row read as a column of dead labels;
            // the panel shows what the device gives, and the reason goes in the sentence
            // underneath instead.
            //
            // This comment used to say a Maxwell on its dongle could not move one control
            // in its audio section without a cable. That was never measured and is not
            // true — the same sub-commands answer over both leads.
            if (!state.IsAvailable)
            {
                _blocked ??= state.Descriptor.RequirementHint;
                continue;
            }

            Quick.Add(CapabilityViewModelFactory.Create(state, session, HintDetail.Terse));
            lines += LinesFor(state.Descriptor.Primitive);
        }

        OnPropertyChanged(nameof(HasNothing));
        OnPropertyChanged(nameof(NothingSaid));
    }

    /// <summary>
    /// What this device would put in the panel, most wanted first, before the line budget
    /// has its say.
    ///
    /// The shared order is the list below; what changes per device is what leads it. On a
    /// microphone that is the capsule's own switch and level, because they are the whole
    /// reason its row would be opened — the same argument that puts the microphone section
    /// first on its page. Everything else keeps the order it had, and picks up the switch
    /// at the end where there is room for it: a headset's row is opened for ChatMix.
    /// </summary>
    private IEnumerable<string> Wanted(DeviceSnapshot device)
    {
        if (device.Info.Class == DeviceClass.Microphone)
        {
            yield return CapabilityIds.MicSystemOn;
            yield return CapabilityIds.MicSystemLevel;

            // The headphone socket, when this device was asked for it. Off by default:
            // the panel is 360 pixels wide, and these two rows are half of what it can
            // hold.
            if (_extras?.ShowsMonitoring(Key) == true)
            {
                yield return CapabilityIds.AudioSystemOn;
                yield return CapabilityIds.AudioSystemVolume;
            }
        }

        foreach (string id in QuickOrder) yield return id;

        yield return CapabilityIds.MicSystemOn;
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
