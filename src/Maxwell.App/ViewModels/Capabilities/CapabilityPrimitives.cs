using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>A value the user reads and cannot change: battery, firmware, mute state.</summary>
public sealed partial class ReadoutCapabilityViewModel : CapabilityViewModel
{
    public ReadoutCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
        Display = Format(state);
        Fill = Position(state);
    }

    [ObservableProperty]
    public partial string Display { get; private set; }

    /// <summary>
    /// A reading that moves too fast to read as a number, and is drawn as a bar instead.
    ///
    /// The distinction is the driver's <see cref="Capability.IsLive"/> rather than a
    /// second primitive, because it is the same thing being said twice: a value only
    /// worth reading while somebody watches is a value only worth drawing as movement.
    /// </summary>
    public bool IsMeter => Descriptor.IsLive;

    /// <summary>
    /// How much of the bar is lit, 0..1.
    ///
    /// Straight off the declared range, and for a level that range is in decibels — which
    /// is what makes the bar readable. On amplitude, speech sits in the first tenth and a
    /// quiet room is indistinguishable from silence; on decibels the same two are a
    /// third of the bar apart.
    /// </summary>
    [ObservableProperty]
    public partial double Fill { get; private set; }

    protected override void OnStateApplied(CapabilityState state)
    {
        Display = Format(state);
        Fill = Position(state);
    }

    /// <summary>
    /// Where the reading sits between the ends of its range. Absent reads as the floor
    /// rather than as nothing: an unlit meter is the honest picture of silence, and a
    /// meter that vanished between frames would be a flicker.
    /// </summary>
    private static double Position(CapabilityState state)
    {
        if (state.Range is not { } range || range.Max <= range.Min) return 0;
        if (state.Value is not NumberValue number) return 0;

        return Math.Clamp((number.Value - range.Min) / (double)(range.Max - range.Min), 0, 1);
    }

    private static string Format(CapabilityState state) => state.Value switch
    {
        NumberValue n => WithUnit(n.Value.ToString(), state.Range?.Unit),
        FlagValue f => f.Value ? Maxwell.App.Localization.Localizer.Current.Say("yes") : Maxwell.App.Localization.Localizer.Current.Say("no"),
        TextValue t => t.Value,
        ChoiceValue c => state.Descriptor.Choices.FirstOrDefault(o => o.Index == c.Index)?.Label ?? $"#{c.Index}",

        // Never a zero. A value that has not been read and a value that is zero look
        // identical in an int and completely different to a person.
        _ => "—",
    };
}

/// <summary>An on/off switch: sidetone, lighting, Fn inversion.</summary>
public sealed partial class ToggleCapabilityViewModel : CapabilityViewModel
{
    public ToggleCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session) => IsOn = state.Value.AsFlag();

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>
    /// A reading that arrives while our own write is still travelling is older than the
    /// hand on the control — the same rule the slider follows, and it was missing here.
    ///
    /// It matters more on a switch than on a slider, because the session publishes the
    /// value from before the write the instant the write begins, and it does so on this
    /// very thread. The option therefore flipped back to the old one inside the same
    /// dispatcher turn that set the new one, before the page had drawn either — so the
    /// control did not move at all until the headset answered. Measured on the noise
    /// reduction: clicked at 0 ms, moved at 810 ms.
    /// </summary>
    protected override void OnStateApplied(CapabilityState state)
    {
        if (WriteInFlight) return;

        IsOn = state.Value.AsFlag();
    }

    partial void OnIsOnChanged(bool value) => WriteNow(CapabilityValue.Of(value));
}

/// <summary>A numeric slider: volume, ChatMix, sidetone level, DPI.</summary>
public sealed partial class RangeCapabilityViewModel : CapabilityViewModel
{
    public RangeCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
        // The reading has to be seeded here as well as on update: a page builds its
        // controls straight from a snapshot without going through Apply, and a settle
        // that finds no reading would drag the value back to zero.
        _reading = state.Value.AsNumber(state.Range?.Min ?? 0);

        Value = _reading;
        HasValue = !state.Value.IsAbsent;
    }

    /// <summary>The last value the device reported, which outranks anything the control coerces.</summary>
    private int _reading;

    [ObservableProperty]
    public partial double Value { get; set; }

    /// <summary>False before the first reading, so the slider can show a dash not a zero.</summary>
    [ObservableProperty]
    public partial bool HasValue { get; private set; }

    public double Minimum => State.Range?.Min ?? 0;

    /// <summary>
    /// Resolved by the session, which fills in ceilings the device reports. The declared
    /// value is only a placeholder until the device has been asked.
    /// </summary>
    public double Maximum => State.Range?.Max ?? 100;

    public double Step => State.Range?.Step ?? 1;

    public string? Unit => State.Range?.Unit;

    /// <summary>
    /// The number in the corner of the row. A zero-based scale reads as "10 / 20"
    /// because the ceiling is part of the meaning; an offset one like earcup balance,
    /// which runs 122 to 134 around a centre of 128, reads as the bare value — "128 / 134"
    /// would invite the reader to think 134 is somehow the goal.
    /// </summary>
    public string Display => HasValue
        ? Unit is { } unit ? WithUnit(Value, unit)
        : Minimum <= 0 ? $"{Value:0} / {Maximum:0}"
        : $"{Value:0}"
        : "—";

    /// <summary>
    /// Where the track draws a notch — the centre of an earcup balance, the 0/10/20 of a
    /// ChatMix. The artboards mark these so a value's meaningful rest position is visible
    /// even before the reader knows the scale; empty when the scale has no special point.
    /// </summary>
    public IReadOnlyList<int> Detents => State.Range?.Detents ?? [];

    /// <summary>
    /// A track alone says nothing about where it sits, so the tray row prints it — but
    /// only once there is something to print. Before the first reading the control hides
    /// itself, and a lone "—" against the name is a row that looks broken rather than one
    /// that is waiting.
    /// </summary>
    public override string? QuickValue => HasValue ? Display : null;

    /// <summary>
    /// Whether this scale can be silenced.
    ///
    /// A volume can: nothing is the bottom of it. An earcup balance cannot — its scale
    /// runs 122 to 134 around a centre, and zero is not a point on it at all. The test is
    /// the scale itself rather than a list of capability ids, which would need extending
    /// for every device that ever ships a volume.
    /// </summary>
    /// <summary>
    /// Never where the device has a mute of its own. This icon writes the floor and
    /// remembers the position, which is an imitation for protocols that offer nothing
    /// better; beside a real switch it would be a second mute on one row, and the two
    /// would disagree the moment either was used.
    /// </summary>
    public bool CanMute =>
        Domain is CapabilityDomain.Audio
        && Minimum <= 0
        && Descriptor.IsWritable
        && Descriptor.EnabledBy is null;

    /// <summary>
    /// Silenced by us: the floor is written and the previous position is remembered.
    ///
    /// The protocol has no mute of its own — there is a gain and that is all — so this is
    /// the application holding the position while the device sits at zero. The control
    /// goes on showing that position, greyed, because a slider that snapped to the far
    /// left would throw away the very thing being kept.
    ///
    /// It lives for the session. Close the application while muted and the headset stays
    /// at zero with nothing left to restore from, which is why unmuting also happens when
    /// the connection is torn down.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMuted { get; private set; }

    private int _beforeMute;

    [RelayCommand]
    private void ToggleMute()
    {
        if (!CanEdit || !CanMute) return;

        if (IsMuted)
        {
            IsMuted = false;
            Value = _beforeMute;
            WriteNow(CapabilityValue.Of(_beforeMute));
            return;
        }

        _beforeMute = (int)Math.Round(Value);
        IsMuted = true;

        WriteNow(CapabilityValue.Of((int)Minimum));
    }

    public override void Dispose()
    {
        // Leaving the hardware silent with the remembered level held only in a dead
        // object would be a device that stopped working for no reason the user can see.
        if (IsMuted && Session is not null)
        {
            IsMuted = false;
            WriteNow(CapabilityValue.Of(_beforeMute));
        }

        base.Dispose();
    }

    protected override void OnStateApplied(CapabilityState state)
    {
        // Bounds first, value second, and the order is not cosmetic. A Slider clamps
        // whatever it is given to the range it currently holds, so pushing 128 while it
        // still believes the maximum is 100 silently turns it into 100 — and widening
        // the range afterwards does not give the value back.
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(Step));

        HasValue = !state.Value.IsAbsent;
        _reading = state.Value.AsNumber((int)Minimum);

        // A reading that arrives while our own write is still travelling is older than
        // the hand on the control. Taking it would drag the slider back to where it was
        // and then forward again when the next poll catches up — the control visibly
        // fighting the person using it.
        //
        // While muted the device honestly reports zero, and taking that would erase the
        // level being held for the person to come back to.
        if (!WriteInFlight && !IsMuted) Value = _reading;

        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(QuickValue));
    }

    /// <summary>
    /// The slider has laid out and may have clamped the value against bounds it had not
    /// received yet. The reading wins.
    /// </summary>
    protected override void OnSettled()
    {
        // Same rule as a fresh reading: the last thing the person did outranks the last
        // thing the device said, until the write has had time to land.
        if (WriteInFlight) return;

        if (Math.Abs(Value - _reading) < 0.5) return;

        Value = _reading;
        OnPropertyChanged(nameof(Display));
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(QuickValue));
        Write(CapabilityValue.Of((int)Math.Round(value)));
    }
}

/// <summary>A pick from a named list: EQ preset, noise reduction, report rate.</summary>
public sealed partial class ChoiceCapabilityViewModel : CapabilityViewModel
{
    public ChoiceCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
        // Translated here rather than in the driver: a driver is a layer below the
        // catalogue and cannot see it, and these are the words on the buttons.
        Options = [.. state.Descriptor.Choices.Select(
            c => c with { Label = Text.Say(c.Label), Hint = Text.Say(c.Hint) })];
        Selected = Find(state);
    }

    public ObservableCollection<CapabilityChoice> Options { get; }

    /// <summary>
    /// Two or three options are drawn as a segmented switch, as the design shows noise
    /// reduction; longer lists collapse into a dropdown because ten EQ presets in a row
    /// would not fit and would not read.
    /// </summary>
    public bool IsSegmented => Options.Count is > 1 and <= 3;

    [ObservableProperty]
    public partial CapabilityChoice? Selected { get; set; }

    /// <inheritdoc cref="ToggleCapabilityViewModel.OnStateApplied"/>
    protected override void OnStateApplied(CapabilityState state)
    {
        if (WriteInFlight) return;

        Selected = Find(state);
    }

    partial void OnSelectedChanged(CapabilityChoice? value)
    {
        if (value is not null) WriteNow(CapabilityValue.Choice(value.Index));
    }

    private CapabilityChoice? Find(CapabilityState state) =>
        state.Value is ChoiceValue c ? Options.FirstOrDefault(o => o.Index == c.Index) : null;
}

/// <summary>
/// A one-shot command: factory reset, pairing, identify.
///
/// Nothing here executes on its own. Anything with a risk above
/// <see cref="CapabilityRisk.Safe"/> raises <see cref="ConfirmationRequested"/> and waits
/// for the shell to come back with an answer — the confirmation is not the button's own
/// business, and burying it in the view model would make it skippable.
/// </summary>
public sealed partial class ActionCapabilityViewModel : CapabilityViewModel
{
    private readonly IConfirmationPrompt? _confirm;

    public ActionCapabilityViewModel(
        CapabilityState state,
        IDeviceSession? session,
        IConfirmationPrompt? confirm = null)
        : base(state, session) => _confirm = confirm;

    /// <summary>The ellipsis is the promise that a question is coming.</summary>
    public string Label => NeedsConfirmation ? Maxwell.App.Localization.Localizer.Current.Say("Run…") : Maxwell.App.Localization.Localizer.Current.Say("Run");

    [RelayCommand]
    private async Task InvokeAsync()
    {
        if (!CanEdit) return;

        if (NeedsConfirmation)
        {
            // No prompt wired means no confirmation is possible, and a dangerous command
            // that cannot be confirmed must not run at all.
            if (_confirm is null) return;
            if (!await _confirm.ConfirmAsync(Descriptor).ConfigureAwait(true)) return;
        }

        WriteNow(CapabilityValue.Of(true));
    }
}
