using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Kvazr.App.ViewModels.Capabilities;
using Kvazr.Core.Devices;

namespace Kvazr.App.ViewModels.Devices;

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
    ///
    /// The microphone's switch and level sit under the device's own microphone settings
    /// and above the ones nobody touches. They are Windows' rather than the device's,
    /// which is why they do not lead; they are the quickest way to mute yourself, which
    /// is why they are not last. Their old place was last of all, offered only if a line
    /// happened to be spare, and on a headset there never was one.
    ///
    /// The level follows the switch rather than sitting apart from it, because a switch
    /// and the level it governs are one block; the page orders them the same way and for
    /// the same reason.
    /// </summary>
    private static readonly string[] QuickOrder =
    [
        CapabilityIds.MixChat,
        CapabilityIds.AudioVolume,
        CapabilityIds.AudioEqPreset,
        CapabilityIds.MicNoiseReduction,
        CapabilityIds.MicSystemOn,
        CapabilityIds.MicSystemLevel,
        CapabilityIds.MicSidetone,
        CapabilityIds.PointerDpi,
        CapabilityIds.LightBrightness,
        CapabilityIds.LightOn,
        CapabilityIds.LinkHosts,
    ];

    /// <summary>
    /// What Windows gives any audio device and the panel still will not offer: the system
    /// playback volume.
    ///
    /// It is the system's own slider, the very one sitting two icons along in the same
    /// tray, and a copy of it here would be a control that looks like the earphones' and
    /// is not. A volume row earns its place when it moves something inside the device —
    /// which for these earphones is nothing we can reach.
    ///
    /// The microphone's two rows are the other half of what Windows gives, and they are
    /// different: they drive nothing else, they are the only settings a device we have no
    /// protocol for has at all, and there is nowhere else to reach them quickly. They are
    /// in the order above.
    /// </summary>
    /// <summary>
    /// What an opened row may spend, in lines.
    ///
    /// Counting controls was the rule before, and it stopped at three because three
    /// sliders is what fits in a 360-pixel panel. But a slider is two lines — its caption
    /// and the track under it — while a dropdown or a segmented switch is one, and
    /// refusing those on the same count left the panel with room it would not use.
    ///
    /// Nine rather than six, and the three are the microphone's. A headset spent all six
    /// on ChatMix, volume, preset and noise reduction — measured, on a Maxwell — so the
    /// switch and level arrived to find nothing left, wherever they stood in the order.
    /// The budget buys them room instead of taking it from something somebody uses.
    ///
    /// Not more than nine. The order above decides what a full panel drops, and it is
    /// meant to: a sidetone is the tenth line on that same headset, and is exactly the
    /// kind of thing that should fall off the end.
    /// </summary>
    private const int QuickLines = 9;

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
    /// Why the row is empty, when there is a reason worth giving.
    ///
    /// "Not over this link" is a fact about the device and it is actionable: a Maxwell on
    /// its dongle is not a device without settings, it is a device whose settings are one
    /// cable away. It comes from the capability that was skipped rather than being
    /// written here, because the driver is what knows why.
    ///
    /// There used to be a sentence for the other case too — "nothing to set, the device
    /// gives only what the row already shows". That one is about this application rather
    /// than about the device, and it was the whole content of an opened row: a person
    /// clicked to see more and got a paragraph explaining that there is no more. The way
    /// out of that row is the page below it, which is now offered instead.
    /// </summary>
    public string NothingSaid => _blocked ?? string.Empty;

    /// <summary>Whether there is a reason to give, as against simply having nothing.</summary>
    public bool HasReason => _blocked is { Length: > 0 };

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

        // A control that depends on a switch is joined to it here, the same way the
        // device page does it once every row exists. The panel used to skip this, so a
        // microphone's recording level sat there fully draggable under a switch that was
        // off — the page beside it greyed the same slider out, which is the tell that
        // this was an omission rather than a decision.
        //
        // Only against a switch that made the panel. One that did not is not a gate the
        // reader can see or move, and greying a control against an invisible condition is
        // worse than leaving it alone.
        foreach (CapabilityViewModel vm in Quick)
        {
            if (vm.Descriptor.EnabledBy is not { Length: > 0 } gate) continue;
            if (Quick.FirstOrDefault(q => q.Id == gate) is not ToggleCapabilityViewModel toggle) continue;

            vm.GateWith(toggle);
        }

        OnPropertyChanged(nameof(HasNothing));
        OnPropertyChanged(nameof(NothingSaid));
        OnPropertyChanged(nameof(HasReason));
    }

    /// <summary>
    /// What this device would put in the panel, most wanted first, before the line budget
    /// has its say.
    ///
    /// The shared order is the list above; what changes per device is what leads it. On a
    /// microphone that is the capsule's own switch and level, because they are the whole
    /// reason its row would be opened — the same argument that puts the microphone section
    /// first on its page. On everything else the two keep their place in the shared order,
    /// under the device's own microphone settings.
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

        foreach (string id in QuickOrder)
        {
            // Not a microphone's lighting, whatever room is left.
            //
            // The panel is for the controls somebody reaches for while doing something
            // else — muting mid-sentence, pulling the level down, turning monitoring off.
            // A microphone's lighting is chosen once and then looked at; it is how the
            // thing on the desk looks, not something anybody adjusts during a call. It
            // was only here because the capsule's two rows leave room for it, which is a
            // reason to leave the room empty rather than to fill it.
            if (device.Info.Class == DeviceClass.Microphone
                && id is CapabilityIds.LightOn or CapabilityIds.LightBrightness)
            {
                continue;
            }

            yield return id;
        }

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
