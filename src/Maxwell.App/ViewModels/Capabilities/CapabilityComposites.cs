using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>
/// The primitives whose reading is a <see cref="VectorValue"/>: a channel pair, an
/// equaliser curve, a lighting rig, a button map, a keyboard layout.
///
/// They share a base rather than each keeping their own parts, and the reason is the
/// write guard. A composite draws one child control per part, and every one of those
/// children coerces itself against bounds during layout exactly the way a lone Slider
/// does — so every one of them can push back a value nobody asked for. Funnelling all
/// of them through this class's single call to <see cref="CapabilityViewModel.Write"/>
/// puts them behind the guard that already exists instead of giving each primitive its
/// own opinion about when a change is real. The remarks on the settling flag in
/// <see cref="CapabilityViewModel"/> say what that guard cost to learn.
/// </summary>
public abstract class CompositeCapabilityViewModel : CapabilityViewModel
{
    private CapabilityValue[] _readings;

    /// <param name="slots">
    /// How many entries the reading vector has. For every primitive but one this is the
    /// member count, because a slot is a member. A keyboard layout is the exception: its
    /// outer dimension is layers, each holding a keycode per member, so it passes its
    /// layer count and each slot is a whole layer.
    /// </param>
    protected CompositeCapabilityViewModel(CapabilityState state, IDeviceSession? session, int slots)
        : base(state, session)
    {
        Members = state.Descriptor.Members;
        _readings = Split(state.Value, slots);
    }

    public IReadOnlyList<CapabilityMember> Members { get; }

    /// <summary>
    /// The last reading of one slot, which outranks anything a child control coerced.
    /// Absent when the driver answered with fewer slots than the descriptor declares,
    /// which the children have to draw as a blank rather than as a zero.
    /// </summary>
    protected CapabilityValue Reading(int slot) =>
        slot >= 0 && slot < _readings.Length ? _readings[slot] : CapabilityValue.Absent;

    /// <summary>A slider or a dragged curve point moved. Debounced, like any range.</summary>
    protected void WriteSlot(int slot, CapabilityValue value)
    {
        if (Record(slot, value)) Write(CapabilityValue.Vector([.. _readings]));
    }

    /// <summary>
    /// A discrete pick changed — a button's action, a zone's colour. Immediate, because
    /// a dropdown that waits a third of a second before doing anything reads as broken.
    /// </summary>
    protected void WriteSlotNow(int slot, CapabilityValue value)
    {
        if (Record(slot, value)) WriteNow(CapabilityValue.Vector([.. _readings]));
    }

    protected override void OnStateApplied(CapabilityState state)
    {
        _readings = Split(state.Value, _readings.Length);
        PushReadings();
    }

    /// <summary>
    /// The children have laid out and may have clamped themselves. Re-asserting here
    /// rather than in an override of each child is what keeps the correction inside the
    /// window where the base class is still suppressing writes.
    /// </summary>
    protected override void OnSettled() => PushReadings();

    /// <summary>Hand the current slot readings to the child view models.</summary>
    protected abstract void PushReadings();

    /// <summary>
    /// Whole vectors go to the device, never half of one: the members of a composite are
    /// a single capability to the driver, and there is no command for four bands of a
    /// ten-band curve.
    /// </summary>
    private bool Record(int slot, CapabilityValue value)
    {
        if (slot < 0 || slot >= _readings.Length) return false;
        if (_readings[slot] == value) return false;

        _readings[slot] = value;
        return true;
    }

    private static CapabilityValue[] Split(CapabilityValue value, int slots)
    {
        var readings = new CapabilityValue[Math.Max(0, slots)];
        for (int i = 0; i < readings.Length; i++) readings[i] = value.Member(i);
        return readings;
    }
}

// ---------- RangeGroup ----------

/// <summary>
/// Sliders that belong together: the left and right earcup gains, a sensor that resolves
/// X and Y separately. One capability, one write, several tracks.
/// </summary>
public sealed class RangeGroupCapabilityViewModel : CompositeCapabilityViewModel
{
    public RangeGroupCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        ArgumentNullException.ThrowIfNull(state);

        Channels = [.. Members.Select(m => new GroupedRangeViewModel(this, m, state.Range))];
        PushReadings();
    }

    public IReadOnlyList<GroupedRangeViewModel> Channels { get; }

    protected override void PushReadings()
    {
        foreach (GroupedRangeViewModel channel in Channels) channel.Pull(Reading(channel.Index), CanEdit);
    }

    internal void Commit(int index, int value) => WriteSlot(index, CapabilityValue.Of(value));

    private static int Count(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Descriptor.Members.Count;
    }
}

/// <summary>One track of a <see cref="RangeGroupCapabilityViewModel"/>.</summary>
public sealed partial class GroupedRangeViewModel : ObservableObject
{
    private readonly RangeGroupCapabilityViewModel _owner;
    private readonly CapabilityRange _bounds;

    /// <summary>The last value the device reported. Outranks anything the slider coerces.</summary>
    private int _reading;

    internal GroupedRangeViewModel(
        RangeGroupCapabilityViewModel owner,
        CapabilityMember member,
        CapabilityRange? shared)
    {
        _owner = owner;

        // A member without its own bounds borrows the capability's, which is what a
        // symmetric pair wants: left and right run on one scale and saying so twice
        // invites the two copies to drift apart.
        _bounds = member.Range ?? shared ?? new CapabilityRange { Min = 0, Max = 100 };

        Index = member.Index;
        Label = member.Label;
    }

    public int Index { get; }

    public string Label { get; }

    public double Minimum => _bounds.Min;

    public double Maximum => _bounds.Max;

    public string? Unit => _bounds.Unit;

    [ObservableProperty]
    public partial double Value { get; set; }

    /// <summary>False before the first reading, so the track shows a dash and not a zero.</summary>
    [ObservableProperty]
    public partial bool HasValue { get; private set; }

    [ObservableProperty]
    public partial bool CanEdit { get; private set; }

    public string Display => HasValue
        ? Unit is { } unit ? CapabilityViewModel.WithUnit(Value, unit) : $"{Value:0}"
        : "—";

    internal void Pull(CapabilityValue value, bool canEdit)
    {
        // Bounds before value, for the reason spelled out in RangeCapabilityViewModel:
        // a Slider clamps to the range it holds at that instant and widening it
        // afterwards does not hand the value back.
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));

        CanEdit = canEdit;
        HasValue = !value.IsAbsent;
        _reading = value.AsNumber((int)Minimum);
        Value = _reading;

        OnPropertyChanged(nameof(Display));
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Display));

        int rounded = (int)Math.Round(value);
        if (rounded != _reading) _owner.Commit(Index, rounded);
    }
}

// ---------- Curve ----------

/// <summary>A band of a curve, addressed by the plot as a fraction of its own height.</summary>
public sealed record CurvePoint(int Index, double Fraction);

/// <summary>
/// A curve of bands: a graphic equaliser, an axis response. The plot draws the points
/// and drags them; the numbers stay here, because the control must not be the place a
/// value lives.
/// </summary>
public sealed partial class CurveCapabilityViewModel : CompositeCapabilityViewModel
{
    private readonly CapabilityRange _bounds;
    private readonly int[] _values;
    private readonly bool[] _known;

    public CurveCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        ArgumentNullException.ThrowIfNull(state);

        _bounds = state.Range ?? state.Descriptor.Range ?? new CapabilityRange { Min = -12, Max = 12 };
        _values = new int[Members.Count];
        _known = new bool[Members.Count];

        Labels = [.. Members.Select(m => m.Label)];
        PushReadings();
    }

    /// <summary>Frequency captions under the plot.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>
    /// The ends of the horizontal axis. Only the ends: ten band captions across the
    /// width the design gives this control would collide into an unreadable smear, and
    /// the two ends are what tell the reader whether they are looking at hertz or
    /// degrees of stick travel.
    /// </summary>
    public string Scale => Labels.Count switch
    {
        0 => string.Empty,
        1 => Labels[0],
        _ => $"{Labels[0]} … {Labels[^1]}",
    };

    /// <summary>
    /// Each band as a fraction of the plot's height, zero at the floor. Fractions rather
    /// than raw values so the control never has to know what a decibel is, and so a
    /// response curve in percent draws through the same code as an equaliser in dB.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<double> Points { get; private set; } = [];

    /// <summary>False until at least one band has been read. An unread curve is not flat.</summary>
    [ObservableProperty]
    public partial bool HasCurve { get; private set; }

    /// <summary>
    /// The band count with no bands at all is the shape Maxwell's equaliser has: the
    /// firmware plainly holds one, and the command that reaches it has not been found.
    /// That is a different sentence from "read, and flat".
    /// </summary>
    public string Display => Members.Count == 0
        ? "Полосы недоступны"
        : HasCurve ? $"{Members.Count} полос · {Describe()}" : $"{Members.Count} полос · нет данных";

    /// <summary>
    /// Raised by the plot on a drag. Nothing else in the control writes, so a curve that
    /// is merely being looked at cannot move.
    /// </summary>
    [RelayCommand]
    private void MovePoint(CurvePoint? point)
    {
        if (point is null || !CanEdit) return;
        if (point.Index < 0 || point.Index >= _values.Length) return;

        int raw = (int)Math.Round(_bounds.Min + (Math.Clamp(point.Fraction, 0, 1) * (_bounds.Max - _bounds.Min)));
        if (raw == _values[point.Index] && _known[point.Index]) return;

        _values[point.Index] = raw;
        _known[point.Index] = true;

        Republish();
        WriteSlot(point.Index, CapabilityValue.Of(raw));
    }

    protected override void PushReadings()
    {
        for (int i = 0; i < _values.Length; i++)
        {
            CapabilityValue reading = Reading(i);

            _known[i] = !reading.IsAbsent;

            // An unread band sits on the centre line so the shape stays drawable, but
            // HasCurve keeps the whole plot dimmed rather than presenting the flat line
            // as something the device said.
            _values[i] = reading.AsNumber((_bounds.Min + _bounds.Max) / 2);
        }

        Republish();
    }

    private void Republish()
    {
        double span = Math.Max(1, _bounds.Max - _bounds.Min);
        Points = [.. _values.Select(v => Math.Clamp((v - _bounds.Min) / span, 0, 1))];

        HasCurve = Array.Exists(_known, k => k);
        OnPropertyChanged(nameof(Display));
    }

    private string Describe()
    {
        int low = int.MaxValue;
        int high = int.MinValue;

        for (int i = 0; i < _values.Length; i++)
        {
            if (!_known[i]) continue;
            low = Math.Min(low, _values[i]);
            high = Math.Max(high, _values[i]);
        }

        if (low > high) return "нет данных";

        string unit = _bounds.Unit is { } u ? $" {u}" : string.Empty;
        return low == high ? $"{low:+0;-0;0}{unit}" : $"{low:+0;-0;0}…{high:+0;-0;0}{unit}";
    }

    private static int Count(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Descriptor.Members.Count;
    }
}

// ---------- Lighting ----------

/// <summary>One colour a zone can be set to.</summary>
public sealed record LightingSwatch(string Name, int Packed);

/// <summary>
/// Colour per lighting zone. Brightness and effect are separate capabilities — the
/// device exposes them separately and folding them in here would make a keyboard whose
/// firmware offers only one of the three undrawable.
/// </summary>
public sealed class LightingCapabilityViewModel : CompositeCapabilityViewModel
{
    /// <summary>
    /// The offered colours are a presentation choice, not a claim about the hardware.
    /// The value written is a packed RGB, so a device that accepts arbitrary colour is
    /// not narrowed by this list — the list exists because eight named swatches are
    /// operable in a row forty pixels tall, and a full picker is a section of its own.
    /// </summary>
    private static readonly LightingSwatch[] Palette =
    [
        new("Белый", 0xFFFFFF),
        new("Красный", 0xE0483C),
        new("Янтарный", 0xEAA300),
        new("Зелёный", 0x3FA45B),
        new("Бирюзовый", 0x2AA9B8),
        new("Синий", 0x2C6FD1),
        new("Фиолетовый", 0x7B4EC4),
        new("Розовый", 0xD65DB1),
    ];

    public LightingCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        Zones = [.. Members.Select(m => new LightingZoneViewModel(this, m, Palette))];
        PushReadings();
    }

    public IReadOnlyList<LightingZoneViewModel> Zones { get; }

    protected override void PushReadings()
    {
        foreach (LightingZoneViewModel zone in Zones) zone.Pull(Reading(zone.Index), CanEdit);
    }

    internal void Commit(int index, int packed) => WriteSlotNow(index, CapabilityValue.Of(packed));

    private static int Count(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Descriptor.Members.Count;
    }
}

/// <summary>One zone of a <see cref="LightingCapabilityViewModel"/>.</summary>
public sealed partial class LightingZoneViewModel : ObservableObject
{
    private readonly LightingCapabilityViewModel _owner;

    internal LightingZoneViewModel(
        LightingCapabilityViewModel owner,
        CapabilityMember member,
        IReadOnlyList<LightingSwatch> palette)
    {
        _owner = owner;

        Index = member.Index;
        Label = member.Label;
        Palette = palette;
    }

    public int Index { get; }

    public string Label { get; }

    public IReadOnlyList<LightingSwatch> Palette { get; }

    [ObservableProperty]
    public partial LightingSwatch? Selected { get; set; }

    /// <summary>False until the zone has answered. An unlit zone and an unread one differ.</summary>
    [ObservableProperty]
    public partial bool HasColour { get; private set; }

    [ObservableProperty]
    public partial bool CanEdit { get; private set; }

    /// <summary>
    /// Assigning the selection here raises the same notification a click does, so this
    /// would echo the device's own colour straight back to it. Nothing suppresses that
    /// locally on purpose: every path into this method already runs inside the write
    /// guard on <see cref="CapabilityViewModel"/>, and a second flag beside that guard
    /// would be one more place for the two to disagree about when a change is real.
    /// </summary>
    internal void Pull(CapabilityValue value, bool canEdit)
    {
        CanEdit = canEdit;
        HasColour = !value.IsAbsent;

        int packed = value.AsNumber(-1);
        Selected = Palette.FirstOrDefault(s => s.Packed == packed);
    }

    partial void OnSelectedChanged(LightingSwatch? value)
    {
        if (value is not null) _owner.Commit(Index, value.Packed);
    }
}

// ---------- ButtonMap ----------

/// <summary>
/// One action per physical button. The buttons come from the descriptor because a mouse
/// has the ones it has; the vocabulary of actions comes from
/// <see cref="Capability.Choices"/> because that is what the driver will accept.
/// </summary>
public sealed class ButtonMapCapabilityViewModel : CompositeCapabilityViewModel
{
    public ButtonMapCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        ArgumentNullException.ThrowIfNull(state);

        IReadOnlyList<CapabilityChoice> actions = state.Descriptor.Choices;
        Buttons = [.. Members.Select(m => new ButtonAssignmentViewModel(this, m, actions))];
        PushReadings();
    }

    public IReadOnlyList<ButtonAssignmentViewModel> Buttons { get; }

    protected override void PushReadings()
    {
        foreach (ButtonAssignmentViewModel button in Buttons) button.Pull(Reading(button.Index), CanEdit);
    }

    internal void Commit(int index, int action) => WriteSlotNow(index, CapabilityValue.Choice(action));

    private static int Count(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Descriptor.Members.Count;
    }
}

/// <summary>One button of a <see cref="ButtonMapCapabilityViewModel"/>.</summary>
public sealed partial class ButtonAssignmentViewModel : ObservableObject
{
    private readonly ButtonMapCapabilityViewModel _owner;

    internal ButtonAssignmentViewModel(
        ButtonMapCapabilityViewModel owner,
        CapabilityMember member,
        IReadOnlyList<CapabilityChoice> actions)
    {
        _owner = owner;

        Index = member.Index;
        Label = member.Label;
        Hint = member.Hint;
        Actions = actions;
    }

    public int Index { get; }

    public string Label { get; }

    /// <summary>Why this button is special: it cycles DPI, it cannot be remapped.</summary>
    public string? Hint { get; }

    public IReadOnlyList<CapabilityChoice> Actions { get; }

    [ObservableProperty]
    public partial CapabilityChoice? Selected { get; set; }

    [ObservableProperty]
    public partial bool CanEdit { get; private set; }

    /// <summary>See the note on <see cref="LightingZoneViewModel.Pull"/>: the echo this
    /// could produce is held off by the one write guard, not by a flag of its own.</summary>
    internal void Pull(CapabilityValue value, bool canEdit)
    {
        CanEdit = canEdit;

        int index = value.AsChoiceIndex();
        Selected = Actions.FirstOrDefault(a => a.Index == index);
    }

    partial void OnSelectedChanged(CapabilityChoice? value)
    {
        if (value is not null) _owner.Commit(Index, value.Index);
    }
}

// ---------- KeyMap ----------

/// <summary>One layer tab of a <see cref="KeyMapCapabilityViewModel"/>.</summary>
public sealed record KeyLayer(int Index, string Label);

/// <summary>
/// Layers of keycodes written into the keyboard itself.
///
/// The outer dimension of the reading is layers, not keys, which is why this is the one
/// composite whose slot is not a member: a layer is written as a unit, and a keyboard
/// that has never been read is missing whole layers rather than scattered keys.
/// </summary>
public sealed partial class KeyMapCapabilityViewModel : CompositeCapabilityViewModel
{
    /// <summary>Cap width plus its margin, matching <c>Border.keycap</c> in the styles.</summary>
    private const double CapPitch = 19;

    public KeyMapCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        ArgumentNullException.ThrowIfNull(state);

        Columns = Math.Max(1, state.Descriptor.GridColumns);
        Keycodes = state.Descriptor.Choices;

        Layers = [.. Enumerable
            .Range(0, Math.Max(1, state.Descriptor.LayerCount))
            .Select(i => new KeyLayer(i, $"Слой {i}"))];

        Keys = [.. Members.Select(m => new KeyCapViewModel(m))];

        // Assigned rather than left null so the grid has something to draw before anyone
        // touches it: a layout with no layer selected is a blank rectangle, and a blank
        // rectangle is indistinguishable from a keyboard that failed to answer.
        ActiveLayer = Layers[0];
        PushReadings();
    }

    public int Columns { get; }

    /// <summary>
    /// The width that makes the caps wrap at <see cref="Columns"/>.
    ///
    /// A WrapPanel sized to the row rather than a UniformGrid told how many columns to
    /// use, because the column count is a per-capability fact and an items panel
    /// template has no data context to read it from. Both produce the same grid; only
    /// this one can be driven by the descriptor.
    /// </summary>
    public double GridWidth => Columns * CapPitch;

    public IReadOnlyList<KeyLayer> Layers { get; }

    public IReadOnlyList<KeyCapViewModel> Keys { get; }

    /// <summary>What a selected key may be reassigned to.</summary>
    public IReadOnlyList<CapabilityChoice> Keycodes { get; }

    [ObservableProperty]
    public partial KeyLayer? ActiveLayer { get; set; }

    [ObservableProperty]
    public partial KeyCapViewModel? SelectedKey { get; set; }

    [ObservableProperty]
    public partial CapabilityChoice? Assignment { get; set; }

    /// <summary>False until the shown layer has been read. Blank caps, not empty ones.</summary>
    [ObservableProperty]
    public partial bool HasLayout { get; private set; }

    public bool HasSelection => SelectedKey is not null;

    protected override void PushReadings()
    {
        IReadOnlyList<CapabilityValue> layer = Reading(ActiveLayer?.Index ?? 0).AsVector();

        HasLayout = layer.Count > 0;

        foreach (KeyCapViewModel key in Keys)
        {
            key.Pull(key.Index < layer.Count ? layer[key.Index] : CapabilityValue.Absent);
        }
    }

    partial void OnActiveLayerChanged(KeyLayer? value)
    {
        // Switching tabs is a read, never a write: the layer the user is looking at is
        // not a setting on the device.
        SelectedKey = null;
        PushReadings();
    }

    partial void OnSelectedKeyChanged(KeyCapViewModel? value)
    {
        foreach (KeyCapViewModel key in Keys) key.IsSelected = ReferenceEquals(key, value);

        Assignment = value is null
            ? null
            : Keycodes.FirstOrDefault(c => string.Equals(c.Label, value.Legend, StringComparison.Ordinal));

        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnAssignmentChanged(CapabilityChoice? value)
    {
        if (value is null || SelectedKey is null || !CanEdit) return;
        if (string.Equals(value.Label, SelectedKey.Legend, StringComparison.Ordinal)) return;

        int layerIndex = ActiveLayer?.Index ?? 0;

        SelectedKey.Assign(value.Label);

        // The whole layer goes down, because that is the unit the firmware stores and
        // because a half-written layer would leave the keyboard in a state no reading
        // could explain.
        WriteSlotNow(layerIndex, CapabilityValue.Vector([.. Keys.Select(k => k.Reading)]));
    }

    private static int Count(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Math.Max(1, state.Descriptor.LayerCount);
    }
}

/// <summary>One key position of a <see cref="KeyMapCapabilityViewModel"/>.</summary>
public sealed partial class KeyCapViewModel : ObservableObject
{
    internal KeyCapViewModel(CapabilityMember member)
    {
        Index = member.Index;
        Position = member.Label;
    }

    public int Index { get; }

    /// <summary>Where the key sits, which never changes. The legend on it does.</summary>
    public string Position { get; }

    [ObservableProperty]
    public partial string Legend { get; private set; } = string.Empty;

    /// <summary>
    /// Nobody has asked this key what it holds, which is not the same as its holding
    /// nothing. Most of a function layer is genuinely unassigned and draws as a plain
    /// blank cap; a layer the keyboard was never asked for draws faded. Collapsing the
    /// two would make an unread layer look like a wiped one.
    /// </summary>
    [ObservableProperty]
    public partial bool IsUnread { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>What goes back to the device for this position on the shown layer.</summary>
    internal CapabilityValue Reading { get; private set; } = CapabilityValue.Absent;

    internal void Pull(CapabilityValue value)
    {
        Reading = value;
        IsUnread = value.IsAbsent;
        Legend = value.AsText() ?? string.Empty;
    }

    internal void Assign(string legend)
    {
        Legend = legend;
        IsUnread = false;
        Reading = CapabilityValue.Of(legend);
    }
}

// ---------- ProfileSet and HostSlots ----------

/// <summary>
/// Named slots on the device with exactly one of them live: onboard profiles, paired
/// hosts. The list is a descriptor fact and the reading is only which one is current,
/// so this is a <see cref="ChoiceValue"/> and needs no shape of its own.
///
/// The two subclasses differ in nothing but their verbs, and the verbs matter: applying
/// a profile and switching a host are not the same promise, and a free slot invites
/// filling rather than selecting.
/// </summary>
public abstract partial class SlotListCapabilityViewModel : CapabilityViewModel
{
    protected SlotListCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
        ArgumentNullException.ThrowIfNull(state);

        Slots = [.. state.Descriptor.Members.Select(m => new CapabilitySlotViewModel(this, m))];
        Sync(state);
    }

    public IReadOnlyList<CapabilitySlotViewModel> Slots { get; }

    /// <summary>The badge on the live slot: "активен", "сейчас".</summary>
    public abstract string ActiveLabel { get; }

    /// <summary>The button on a slot that holds something: "Применить", "Переключить".</summary>
    public abstract string SwitchVerb { get; }

    /// <summary>The button on a free slot: "Записать сюда…", "Сопрячь…".</summary>
    public abstract string FillVerb { get; }

    protected override void OnStateApplied(CapabilityState state) => Sync(state);

    internal void Activate(int index) => WriteNow(CapabilityValue.Choice(index));

    private void Sync(CapabilityState state)
    {
        int active = state.Value.AsChoiceIndex();

        foreach (CapabilitySlotViewModel slot in Slots)
        {
            slot.Pull(slot.Index == active, CanEdit);
        }
    }
}

/// <summary>Onboard profiles: settings the device carries with it.</summary>
public sealed class ProfileSetCapabilityViewModel : SlotListCapabilityViewModel
{
    public ProfileSetCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
    }

    public override string ActiveLabel => "активен";

    public override string SwitchVerb => "Применить";

    /// <summary>
    /// The ellipsis is the promise of a question. Writing a profile into an empty slot
    /// still overwrites whatever the device believes is there, and the design system
    /// requires the warning before the write rather than after it.
    /// </summary>
    public override string FillVerb => "Записать сюда…";
}

/// <summary>Paired hosts, and which of them the device is talking to.</summary>
public sealed class HostSlotsCapabilityViewModel : SlotListCapabilityViewModel
{
    public HostSlotsCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
    }

    public override string ActiveLabel => "сейчас";

    public override string SwitchVerb => "Переключить";

    public override string FillVerb => "Сопрячь…";
}

/// <summary>One slot of a <see cref="SlotListCapabilityViewModel"/>.</summary>
public sealed partial class CapabilitySlotViewModel : ObservableObject
{
    private readonly SlotListCapabilityViewModel _owner;

    internal CapabilitySlotViewModel(SlotListCapabilityViewModel owner, CapabilityMember member)
    {
        _owner = owner;

        Index = member.Index;
        Label = member.Label;
        Hint = member.Hint;
        IsEmpty = member.IsEmpty;
    }

    public int Index { get; }

    public string Label { get; }

    /// <summary>What the slot holds, or what filling it would take.</summary>
    public string? Hint { get; }

    public bool IsEmpty { get; }

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    [ObservableProperty]
    public partial bool CanEdit { get; private set; }

    public string Verb => IsEmpty ? _owner.FillVerb : _owner.SwitchVerb;

    public string ActiveLabel => _owner.ActiveLabel;

    /// <summary>The live slot shows a badge instead of a button — there is nowhere to go.</summary>
    public bool ShowsButton => !IsActive;

    internal void Pull(bool active, bool canEdit)
    {
        IsActive = active;
        CanEdit = canEdit;

        OnPropertyChanged(nameof(ShowsButton));
    }

    [RelayCommand]
    private void Select() => _owner.Activate(Index);
}
