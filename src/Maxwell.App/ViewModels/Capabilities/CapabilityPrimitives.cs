using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>A value the user reads and cannot change: battery, firmware, mute state.</summary>
public sealed partial class ReadoutCapabilityViewModel : CapabilityViewModel
{
    public ReadoutCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session) => Display = Format(state);

    [ObservableProperty]
    public partial string Display { get; private set; }

    protected override void OnStateApplied(CapabilityState state) => Display = Format(state);

    private static string Format(CapabilityState state) => state.Value switch
    {
        NumberValue n => state.Range?.Unit is { } unit ? $"{n.Value} {unit}" : n.Value.ToString(),
        FlagValue f => f.Value ? "да" : "нет",
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

    protected override void OnStateApplied(CapabilityState state) => IsOn = state.Value.AsFlag();

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
        ? Unit is { } unit ? $"{Value:0} {unit}"
        : Minimum <= 0 ? $"{Value:0} / {Maximum:0}"
        : $"{Value:0}"
        : "—";

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
        Value = _reading;

        OnPropertyChanged(nameof(Display));
    }

    /// <summary>
    /// The slider has laid out and may have clamped the value against bounds it had not
    /// received yet. The reading wins.
    /// </summary>
    protected override void OnSettled()
    {
        if (Math.Abs(Value - _reading) < 0.5) return;

        Value = _reading;
        OnPropertyChanged(nameof(Display));
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Display));
        Write(CapabilityValue.Of((int)Math.Round(value)));
    }
}

/// <summary>A pick from a named list: EQ preset, noise reduction, report rate.</summary>
public sealed partial class ChoiceCapabilityViewModel : CapabilityViewModel
{
    public ChoiceCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
        Options = [.. state.Descriptor.Choices];
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

    protected override void OnStateApplied(CapabilityState state) => Selected = Find(state);

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
    public ActionCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
    }

    /// <summary>Raised when the action needs a yes before it can run.</summary>
    public event EventHandler<ActionCapabilityViewModel>? ConfirmationRequested;

    /// <summary>The ellipsis is the promise that a question is coming.</summary>
    public string Label => NeedsConfirmation ? "Выполнить…" : "Выполнить";

    [RelayCommand]
    private void Invoke()
    {
        if (!CanEdit) return;

        if (NeedsConfirmation)
        {
            ConfirmationRequested?.Invoke(this, this);
            return;
        }

        Execute();
    }

    /// <summary>Runs the command. Called by the shell once the user has confirmed.</summary>
    public void Execute() => WriteNow(CapabilityValue.Of(true));
}
