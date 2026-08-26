using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.App.Infrastructure;
using Maxwell.App.Localization;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

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

    /// <summary>
    /// Every slot at once — a revert, a preset dropped in whole.
    ///
    /// Goes through the same readings array a single slot does, and that is the whole
    /// point of it existing. A caller that built its own vector and handed it straight to
    /// <see cref="CapabilityViewModel.Write"/> left this array holding the values from
    /// before, and the next <see cref="PushReadings"/> — which the base class runs after
    /// every settle, whether or not a reading arrived — painted those back over the
    /// change. That is exactly how the equaliser's reset appeared to work and then undo
    /// itself a third of a second later.
    /// </summary>
    protected void WriteAll(IReadOnlyList<CapabilityValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (int i = 0; i < _readings.Length; i++)
        {
            _readings[i] = i < values.Count ? values[i] : CapabilityValue.Absent;
        }

        Write(CapabilityValue.Vector([.. _readings]));
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
public sealed partial class RangeGroupCapabilityViewModel : CompositeCapabilityViewModel
{
    public RangeGroupCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        ArgumentNullException.ThrowIfNull(state);

        Channels = [.. Members.Select(m => new GroupedRangeViewModel(this, m, state.Range))];
        PushReadings();
    }

    public IReadOnlyList<GroupedRangeViewModel> Channels { get; }

    /// <summary>
    /// Held back rather than sent, while a dialog is open over these tracks.
    ///
    /// A group normally writes as it is dragged, which is right for a pair of earcup
    /// gains: the ear hears the change and that is the point of the control. It is wrong
    /// for a mouse's stored resolutions, which are edited in a dialog with a Save and a
    /// Cancel — a Cancel that had already written four times is not a cancel, and a
    /// mouse's flash is not a place to write on the way past.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditing { get; private set; }

    /// <summary>What the tracks held when the dialog opened, to go back to on a cancel.</summary>
    private int[] _asOpened = [];

    public void BeginEdit()
    {
        _asOpened = [.. Channels.Select(c => (int)Math.Round(c.Value))];
        IsEditing = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;

        // Straight back onto the tracks. Nothing was sent, so there is nothing to undo at
        // the device — only the drawing to put right.
        for (int i = 0; i < Channels.Count && i < _asOpened.Length; i++)
        {
            Channels[i].Value = _asOpened[i];
        }
    }

    /// <summary>The whole ladder, in one write, and the dialog closes.</summary>
    public void SaveEdit()
    {
        IsEditing = false;

        WriteAll([.. Channels.Select(c => CapabilityValue.Of((int)Math.Round(c.Value)))]);
    }

    protected override void PushReadings()
    {
        // Not over a hand that is mid-edit. A reading arriving while the dialog is open
        // would drag the tracks back under the person moving them.
        if (IsEditing) return;

        foreach (GroupedRangeViewModel channel in Channels) channel.Pull(Reading(channel.Index), CanEdit);
    }

    internal void Commit(int index, int value)
    {
        if (IsEditing) return;

        WriteSlot(index, CapabilityValue.Of(value));
    }

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
    private readonly string _label;

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
        _label = member.Label;
    }

    public int Index { get; }

    /// <summary>
    /// Translated on the way out rather than in the constructor, like every other name a
    /// member carries: these objects are built once and live as long as the page, so a
    /// name captured at construction would stay in whichever language was in force then
    /// and survive a language switch untouched.
    /// </summary>
    public string Label => Localizer.Current.Say(_label);

    public double Minimum => _bounds.Min;

    public double Maximum => _bounds.Max;

    public string? Unit => _bounds.Unit;

    /// <summary>
    /// How far one nudge moves. The device's, where it stated one — a track that steps by
    /// a unit across a scale of thousands is a track nobody can land on.
    /// </summary>
    public double Step => _bounds.Step > 0 ? _bounds.Step : 1;

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

        // Band captions are the driver's — "1 k", "250 Hz" — and translated here,
        // where they are drawn, like everything else a driver writes.
        Labels = [.. Members.Select(m => Maxwell.App.Localization.Localizer.Current.Say(m.Label))];
        PushReadings();

        // The curve the page opened on is as much a baseline as any that arrives later.
        // Captured only on a snapshot, revert stayed dead until the first poll — long
        // enough for someone to drag a band, press it and watch nothing happen.
        if (Array.TrueForAll(_known, k => k)) _baseline = [.. _values];
    }

    /// <summary>
    /// The slot picker the artboard puts in this card's header, or null when the device
    /// has no such capability.
    ///
    /// Linked rather than owned: the preset is a capability in its own right, read and
    /// written like any other, and folding it into the curve would make one control out of
    /// two things the device keeps apart. The page hands it over so the header can draw it.
    /// </summary>
    public ChoiceCapabilityViewModel? Slots
    {
        get => _slots;
        set
        {
            if (ReferenceEquals(_slots, value)) return;

            if (_slots is not null) _slots.PropertyChanged -= OnSlotChanged;
            _slots = value;
            if (_slots is not null) _slots.PropertyChanged += OnSlotChanged;

            RebuildPresets();
        }
    }

    private ChoiceCapabilityViewModel? _slots;

    /// <summary>
    /// Picking a preset replaces the curve on the device, and nothing else would ask for
    /// it: the capability is read on the polling interval, so without this the plot went
    /// on showing the previous preset's shape for as long as ten seconds after the name
    /// above it had changed.
    /// </summary>
    private async void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChoiceCapabilityViewModel.Selected) || Session is null) return;

        IsEditing = false;
        IsDirty = false;

        // A preset picked on the headset decides the shape from here, so whichever saved
        // curve was loaded is no longer what is playing and the picker must stop saying
        // it is.
        _savedName = null;
        OnPropertyChanged(nameof(SelectedPresetIndex));
        OnPropertyChanged(nameof(CanDelete));

        try
        {
            await Session.RefreshAsync([State.Id]).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The page closed while the read was in flight; there is nothing left to show.
        }
    }

    /// <summary>
    /// True once a band has been moved away from what the selected preset holds. The
    /// artboard shows this as a badge beside the title and as the appearance of a save
    /// button; it is also what makes the revert worth offering.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    /// <summary>
    /// The curve as the selected preset has it, kept so an edit can be undone.
    ///
    /// Taken from the device rather than remembered from the last save: switching preset
    /// replaces it wholesale, which is exactly the behaviour wanted — revert means "back
    /// to this preset", not "back to whatever I had open an hour ago".
    /// </summary>
    private int[] _baseline = [];

    /// <summary>Puts every band back to the selected preset and sends that to the device.</summary>
    [RelayCommand]
    private void Revert()
    {
        if (_baseline.Length != _values.Length || !CanEdit) return;

        Array.Copy(_baseline, _values, _values.Length);
        Array.Fill(_known, true);
        IsEditing = false;

        // Forced, not merely requested. Republish is deliberately quiet when the numbers
        // match what it last published, and after a revert they can — the plot would then
        // keep showing the edit while the headset had already gone back.
        Republish(force: true);

        // Scheduled rather than immediate, so it replaces whatever the drag left pending
        // instead of queueing behind it. The plot has already gone back; only the headset
        // is still catching up.
        WriteAll([.. _values.Select(CapabilityValue.Of)]);
    }

    // ---------- the wearer's own curves ----------

    /// <summary>
    /// The curves the wearer has saved. Handed over by the page rather than resolved
    /// here, for the same reason <see cref="Slots"/> is: a view model that reaches into
    /// application services is a view model that cannot be built in a test.
    /// </summary>
    public CurvePresetLibrary? Library
    {
        get => _library;
        set
        {
            if (ReferenceEquals(_library, value)) return;

            if (_library is not null) _library.Changed -= OnLibraryChanged;
            _library = value;
            if (_library is not null) _library.Changed += OnLibraryChanged;

            RebuildPresets();
        }
    }

    private CurvePresetLibrary? _library;

    /// <summary>The saved curve the plot is showing, or null when none is.</summary>
    private string? _savedName;

    /// <summary>
    /// What the headset preset underneath holds, kept from before a saved curve took over
    /// as the baseline. Deleting the saved curve has to be able to get back to it, and by
    /// then the ordinary baseline is the curve being deleted.
    /// </summary>
    private int[] _presetBaseline = [];

    private void OnLibraryChanged(object? sender, EventArgs e) => RebuildPresets();

    /// <summary>
    /// One list for the picker: what the headset ships with, then what the wearer has
    /// saved. Two dropdowns side by side was the alternative and it is worse — a person
    /// choosing a sound does not care which side of the USB cable it is kept on, and
    /// making them pick a list before they pick a curve is asking them to know.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<CurvePresetOption> Presets { get; private set; } = [];

    /// <summary>Which row of <see cref="Presets"/> is showing, or -1 for none of them.</summary>
    public int SelectedPresetIndex
    {
        get
        {
            for (int i = 0; i < Presets.Count; i++)
            {
                CurvePresetOption option = Presets[i];

                if (_savedName is { } saved)
                {
                    if (option.IsSaved && string.Equals(option.Label, saved, StringComparison.Ordinal)) return i;
                    continue;
                }

                if (Slots?.Selected is { } device && ReferenceEquals(option.Device, device)) return i;
            }

            return -1;
        }
    }

    /// <summary>
    /// True when there is a curve to save and somewhere to put it. The button also needs
    /// <see cref="IsDirty"/> before it appears.
    ///
    /// It was briefly shown whenever there was a curve at all, to get out of a dead end:
    /// deleting a preset left the headset playing a shape with no name and no edit
    /// against it, unsaveable until a band was nudged and put back. Deleting now returns
    /// the curve to the headset's own first preset, so that shape no longer exists — and
    /// a save button standing lit over an untouched preset is an offer to save nothing.
    /// </summary>
    public bool CanSave => HasCurve && IsDirty && Library is not null;

    /// <summary>
    /// True when a saved curve is showing, which is the only kind that can be thrown
    /// away. What the headset ships with is not ours to delete.
    /// </summary>
    public bool CanDelete => _savedName is not null && Library is not null;

    /// <summary>Set while the name box is up, which is the only thing that draws it.</summary>
    [ObservableProperty]
    public partial bool IsNaming { get; private set; }

    /// <summary>What is in the name box. Settable, so the field can be typed into.</summary>
    [ObservableProperty]
    public partial string PendingName { get; set; } = string.Empty;

    /// <summary>
    /// Picks a row of the merged list. A headset preset is set on the headset and read
    /// back; a saved curve is ten bands sent exactly the way a drag sends them, because
    /// the headset has never heard of it.
    /// </summary>
    [RelayCommand]
    private void ChoosePreset(int index)
    {
        if (Presets.ElementAtOrDefault(index) is not { } option) return;

        if (option.Device is { } device)
        {
            _savedName = null;
            if (Slots is { } slots) slots.Selected = device;

            OnPropertyChanged(nameof(SelectedPresetIndex));
            OnPropertyChanged(nameof(CanDelete));
            return;
        }

        ApplySaved(option.Label);
    }

    private void ApplySaved(string name)
    {
        if (Library?.Find(name) is not { } preset || !CanEdit) return;
        if (preset.Bands.Count != _values.Length) return;

        for (int i = 0; i < _values.Length; i++)
        {
            _values[i] = Math.Clamp(preset.Bands[i], _bounds.Min, _bounds.Max);
        }

        Array.Fill(_known, true);

        // The saved curve is what "unchanged" means from here: revert comes back to it,
        // and the row must not announce an edit nobody made. What the headset preset held
        // is put aside first, because that is where deleting this curve has to land.
        if (_savedName is null) _presetBaseline = [.. _baseline];

        _savedName = preset.Name;
        _baseline = [.. _values];
        IsEditing = false;

        Republish(force: true);
        WriteAll([.. _values.Select(CapabilityValue.Of)]);

        OnPropertyChanged(nameof(SelectedPresetIndex));
        OnPropertyChanged(nameof(CanDelete));
    }

    /// <summary>Opens the name box, prefilled with something worth pressing Enter on.</summary>
    [RelayCommand]
    private void BeginSave()
    {
        if (!CanSave) return;

        PendingName = _savedName ?? SuggestName();
        IsNaming = true;
    }

    [RelayCommand]
    private void CancelSave() => IsNaming = false;

    /// <summary>
    /// Stores the curve under the typed name. Saving over the name already loaded is
    /// allowed, and is the ordinary way of editing a preset.
    /// </summary>
    [RelayCommand]
    private void ConfirmSave()
    {
        if (Library is not { } library) return;

        string name = PendingName.Trim();
        if (name.Length == 0) return;

        library.Save(name, [.. _values]);

        if (_savedName is null) _presetBaseline = [.. _baseline];

        _savedName = name;
        _baseline = [.. _values];
        IsEditing = false;
        IsNaming = false;

        Republish(force: true);
        RebuildPresets();
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (Library is not { } library || _savedName is not { } name) return;

        library.Delete(name);
        _savedName = null;

        // Back to the headset's own first preset.
        //
        // Two earlier answers to this were wrong. Leaving the deleted shape playing left
        // the picker naming a preset while the drivers played a curve that existed nowhere
        // any more. Falling back to whichever preset the saved curve had been written over
        // was no better: a saved curve is ten absolute gains, it carries no memory of what
        // was underneath, and landing on Bass Boost because that happened to be selected
        // the day it was saved is a preset nobody chose. The first option is the one the
        // headset ships on, and going there is a decision the wearer can read off the
        // picker afterwards.
        if (Slots is { Options.Count: > 0 } slots && !ReferenceEquals(slots.Selected, slots.Options[0]))
        {
            // Writes the preset and re-reads the curve, through the same path as picking
            // it by hand — so the headset genuinely reloads that filter rather than being
            // handed ten numbers we believe belong to it.
            slots.Selected = slots.Options[0];
            RebuildPresets();
            return;
        }

        // Already on the first preset, or a curve with no presets at all. Selecting what
        // is selected writes nothing, so the curve it held goes back the way an edit does.
        if (_presetBaseline.Length == _values.Length)
        {
            Array.Copy(_presetBaseline, _values, _values.Length);
            Array.Fill(_known, true);
            _baseline = [.. _values];
            IsEditing = false;

            Republish(force: true);
            WriteAll([.. _values.Select(CapabilityValue.Of)]);
        }
        else
        {
            Republish(force: true);
        }

        RebuildPresets();
    }

    /// <summary>
    /// A name nobody has used yet. Numbered rather than dated, because a person choosing
    /// between their own presets recognises "My preset 2" and does not recognise a
    /// timestamp.
    /// </summary>
    private string SuggestName()
    {
        string Stem = Maxwell.App.Localization.Localizer.Current.Say("My preset");

        if (Library is not { } library) return Stem;

        IReadOnlyList<EqCurvePreset> saved = library.For(_values.Length);
        if (!saved.Any(p => string.Equals(p.Name, Stem, StringComparison.OrdinalIgnoreCase))) return Stem;

        for (int n = 2; n < 100; n++)
        {
            string candidate = $"{Stem} {n}";

            if (!saved.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return Stem;
    }

    private void RebuildPresets()
    {
        List<CurvePresetOption> built = [];

        if (Slots is { } slots)
        {
            built.AddRange(slots.Options.Select(o => new CurvePresetOption(o.Label, false, o)));
        }

        if (Library is { } library)
        {
            built.AddRange(library.For(_values.Length).Select(p => new CurvePresetOption(p.Name, true, null)));
        }

        Presets = built;

        OnPropertyChanged(nameof(SelectedPresetIndex));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDelete));
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
    /// The bands as the device states them — decibels, percent — for the numbers printed
    /// over each bar and beside the plot. <see cref="Points"/> stays the geometry and this
    /// stays the meaning, so the plot never has to convert one into the other.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<int> Values { get; private set; } = [];

    public double Minimum => _bounds.Min;

    public double Maximum => _bounds.Max;

    public string? Unit => _bounds.Unit;

    /// <summary>
    /// Where the bars grow from, as a fraction of the plot's height. A scale that spans
    /// zero — an equaliser in dB, a response curve in ± percent — hangs its bars off the
    /// zero line, because a band at 0 means "unchanged" and has to look like nothing
    /// rather than like half. A scale that starts at zero grows from the floor.
    /// </summary>
    public double ZeroFraction
    {
        get
        {
            double span = Math.Max(1, _bounds.Max - _bounds.Min);
            return Math.Clamp((0 - _bounds.Min) / span, 0, 1);
        }
    }

    /// <summary>
    /// The band the reader is working on, echoed in the plot's header. Not a selection in
    /// the device — nothing is written by pointing at a band — just where the eye is.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedBand { get; set; }

    /// <summary>The five axis captions, top to bottom, from the scale's own bounds.</summary>
    public IReadOnlyList<string> AxisTicks =>
        [.. Enumerable.Range(0, 5).Select(i => Signed((int)Math.Round(_bounds.Max - ((_bounds.Max - _bounds.Min) * i / 4.0))))];

    // No "250 Hz · +4.5 dB" beside the title any more.
    //
    // It repeated, in a second place and a beat later, a number the plot prints over the
    // band being dragged — and it repeated it for whichever band the eye last landed on,
    // which after a drag across four of them is not a question anyone was asking. The
    // owner of the headset put it plainly: you move a slider and the other numbers move
    // too, so a single one pulled out beside the title says nothing the row does not.

    /// <summary>
    /// One band's value with its sign and unit, as printed above its rail.
    ///
    /// With the unit, and it was briefly without: ten captions each ending in "dB" looked
    /// like the unit said ten times over, and the artboard prints bare numbers. Taking it
    /// off was wrong and nobody had asked for it — "+8" beside "+6" is a number with no
    /// name on a control whose whole scale the reader is being asked to judge, and the
    /// axis down the left is far enough away to be a different thing on the page.
    /// </summary>
    public string BandText(int index)
    {
        if (index < 0 || index >= Values.Count) return "—";

        string text = Signed(Values[index]);
        return _bounds.Unit is { } unit ? WithUnit(text, unit) : text;
    }

    /// <summary>Zero prints as a bare "0": a signed zero reads as a value someone set.</summary>
    private static string Signed(int value) => value.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The band count with no bands at all is the shape Maxwell's equaliser has: the
    /// firmware plainly holds one, and the command that reaches it has not been found.
    /// That is a different sentence from "read, and flat".
    /// </summary>
    public string Display => Members.Count == 0
        ? Maxwell.App.Localization.Localizer.Current.Say("No bands available")
        : HasCurve
            ? $"{Members.Count} {Maxwell.App.Localization.Localizer.Current.Say("bands")} · {Describe()}"
            : $"{Members.Count} {Maxwell.App.Localization.Localizer.Current.Say("bands")} · {Maxwell.App.Localization.Localizer.Current.Say("нет данных")}";

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
        IsEditing = true;

        Republish();
        WriteSlot(point.Index, CapabilityValue.Of(raw));
    }

    /// <summary>
    /// Ignores readings that arrive while a write is still settling.
    ///
    /// A curve is written as a whole and answered a poll later, so for a moment the device
    /// is still reporting what it held before the drag. Taken at face value that reading
    /// snaps every band back under the wearer's finger — the same echo the single sliders
    /// already guard against, and far more visible here because ten of them jump at once.
    /// </summary>
    /// <summary>
    /// The second way a reading can reach the plot, and the one that was still getting
    /// through.
    ///
    /// The base class re-pushes its readings once a write has settled, so children that
    /// clamped themselves can be corrected. For a curve mid-edit that correction is the
    /// device's old shape, and it landed a beat after the drag — the plot jumped back and
    /// then forward again as the next move overtook it. Blocking the poll alone was not
    /// enough; this path had to be closed too.
    /// </summary>
    protected override void OnSettled()
    {
        if (IsEditing) return;

        base.OnSettled();
    }

    protected override void OnStateApplied(CapabilityState state)
    {
        // The curve belongs to whoever is editing it.
        //
        // Once a band has been moved the plot shows the wearer's work, and a poll has
        // nothing to add: the device is reporting the very curve we just sent it, so
        // accepting the reading would quietly make the edit the new baseline — the
        // "unsaved" badge and both buttons would vanish a few seconds after the drag,
        // leaving no way back. Readings resume when the edit is finished, by saving,
        // reverting, or picking another preset.
        if (IsEditing || WriteInFlight) return;

        base.OnStateApplied(state);

        // Not editing, so this is what the selected preset holds — the point a revert goes
        // back to. Set before the state is settled, because publishing works out "dirty"
        // by comparing against it: updated afterwards, every incoming curve first looked
        // like an edit and the save button blinked once on every preset change.
        if (Array.TrueForAll(_known, k => k)) _baseline = [.. _values];

        IsDirty = false;
    }

    /// <summary>True between the first band moved and the edit being finished.</summary>
    private bool IsEditing { get; set; }

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

    private void Republish(bool force = false)
    {
        bool curve = Array.Exists(_known, k => k);

        // Silence when nothing moved.
        //
        // A poll answers with the same ten numbers every few seconds, and republishing
        // them regardless makes the plot rebuild its DOM on a timer. That is not merely
        // wasteful: the rebuild throws away wherever the page was scrolled to and whatever
        // the pointer was holding, so a curve could not be dragged for longer than one
        // polling interval without being yanked out from under the wearer.
        if (!force && curve == HasCurve && Values.Count == _values.Length && Values.SequenceEqual(_values))
        {
            return;
        }

        double span = Math.Max(1, _bounds.Max - _bounds.Min);
        Points = [.. _values.Select(v => Math.Clamp((v - _bounds.Min) / span, 0, 1))];
        Values = [.. _values];

        HasCurve = curve;
        IsDirty = IsEditing && _baseline.Length == _values.Length && !_baseline.SequenceEqual(_values);
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDelete));
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

        if (low > high) return Maxwell.App.Localization.Localizer.Current.Say("no data");

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
public sealed partial class LightingCapabilityViewModel : CompositeCapabilityViewModel
{
    /// <summary>
    /// The offered colours are a presentation choice, not a claim about the hardware.
    /// The value written is a packed RGB, so a device that accepts arbitrary colour is
    /// not narrowed by this list — the list exists because eight named swatches are
    /// operable in a row forty pixels tall, and a full picker is a section of its own.
    /// </summary>
    private static readonly LightingSwatch[] Palette =
    [
        new(Maxwell.App.Localization.Localizer.Current.Say("White"), 0xFFFFFF),
        new(Maxwell.App.Localization.Localizer.Current.Say("Red"), 0xE0483C),
        new(Maxwell.App.Localization.Localizer.Current.Say("Amber"), 0xEAA300),
        new(Maxwell.App.Localization.Localizer.Current.Say("Green"), 0x3FA45B),
        new(Maxwell.App.Localization.Localizer.Current.Say("Teal"), 0x2AA9B8),
        new(Maxwell.App.Localization.Localizer.Current.Say("Blue"), 0x2C6FD1),
        new(Maxwell.App.Localization.Localizer.Current.Say("Purple"), 0x7B4EC4),
        new(Maxwell.App.Localization.Localizer.Current.Say("Pink"), 0xD65DB1),
    ];

    public LightingCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session, Count(state))
    {
        Zones = [.. Members.Select(m => new LightingZoneViewModel(this, m, Palette))];
        PushReadings();
    }

    public IReadOnlyList<LightingZoneViewModel> Zones { get; }

    /// <summary>
    /// Whether the zones are steered by sliders rather than picked from the swatches.
    ///
    /// A preference about operating the control, not about the device, so it is not
    /// written to anything: eight named colours are quicker when one of them is what you
    /// want, and two sliders are the only way when it is not. Which of those a person
    /// prefers is not something to guess at, so both are here and the choice is theirs.
    ///
    /// It lasts as long as the page does. Remembering it belongs with the other interface
    /// preferences and there is nowhere for it in a capability.
    /// </summary>
    [ObservableProperty]
    public partial bool BySliders { get; set; }

    protected override void PushReadings()
    {
        foreach (LightingZoneViewModel zone in Zones) zone.Pull(Reading(zone.Index), CanEdit);
    }

    internal void Commit(int index, int packed) => WriteSlotNow(index, CapabilityValue.Of(packed));

    /// <summary>
    /// The same, on the debounce a dragged control gets.
    ///
    /// A swatch is one click and one colour, so it goes at once. A slider dragged across
    /// its track is sixty colours a second, each of them a write through the session's
    /// gate, and all but the last are on the way to somewhere.
    /// </summary>
    internal void CommitSoon(int index, int packed) => WriteSlot(index, CapabilityValue.Of(packed));

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
    private readonly string _label;

    internal LightingZoneViewModel(
        LightingCapabilityViewModel owner,
        CapabilityMember member,
        IReadOnlyList<LightingSwatch> palette)
    {
        _owner = owner;

        Index = member.Index;
        _label = member.Label;
        Palette = palette;
    }

    public int Index { get; }

    public string Label => Localizer.Current.Say(_label);

    public IReadOnlyList<LightingSwatch> Palette { get; }

    [ObservableProperty]
    public partial LightingSwatch? Selected { get; set; }

    /// <summary>False until the zone has answered. An unlit zone and an unread one differ.</summary>
    [ObservableProperty]
    public partial bool HasColour { get; private set; }

    /// <summary>
    /// The colour itself, whether or not it is one of the swatches.
    ///
    /// The palette is eight quick picks and a device that takes a packed colour takes any
    /// of sixteen million, so the selection alone cannot describe the zone: a colour
    /// chosen from the system's own picker matches no swatch and would otherwise leave
    /// this reading as nothing at all.
    /// </summary>
    [ObservableProperty]
    public partial int Packed { get; private set; }

    /// <summary>The same colour as CSS, for the markup and for a colour input.</summary>
    public string Hex => $"#{Packed & 0xFFFFFF:X6}";

    /// <summary>
    /// The colour again, as the two numbers a person actually steers by.
    ///
    /// Hue and saturation and not brightness, because brightness is a row of its own on
    /// the same card and a second one here would be two controls for one thing. They are
    /// kept as fields rather than derived on demand: a grey has no hue and a black has no
    /// saturation either, so a slider that recomputed itself from the colour would jump
    /// to zero the moment somebody dragged the other one to the end.
    /// </summary>
    [ObservableProperty]
    public partial int Hue { get; set; }

    [ObservableProperty]
    public partial int Saturation { get; set; }

    /// <summary>How far up the value axis this zone sits. Not shown; kept so the round trip is lossless.</summary>
    private double _value = 1;

    partial void OnHueChanged(int value) => Steer();

    partial void OnSaturationChanged(int value) => Steer();

    /// <summary>The colour the two sliders currently describe, sent on if it is a change.</summary>
    private void Steer()
    {
        if (_pulling) return;

        Choose(FromHsv(Hue, Saturation / 100.0, _value), dragged: true);
    }

    /// <summary>Set while a reading is being taken in, so echoing it back does not write.</summary>
    private bool _pulling;

    /// <summary>The track a hue slider is drawn on: the whole spectrum, left to right.</summary>
    public static string HueTrack =>
        "linear-gradient(to right, #f00, #ff0, #0f0, #0ff, #00f, #f0f, #f00)";

    /// <summary>
    /// And the one a saturation slider is drawn on, which has to be built per zone: it
    /// runs from grey to this colour, so the slider shows what it is about to do.
    /// </summary>
    public string SaturationTrack =>
        $"linear-gradient(to right, #{FromHsv(Hue, 0, _value) & 0xFFFFFF:X6}, #{FromHsv(Hue, 1, _value) & 0xFFFFFF:X6})";

    private static int FromHsv(int hue, double saturation, double value)
    {
        double h = ((hue % 360) + 360) % 360 / 60.0;
        double c = Math.Clamp(value, 0, 1) * Math.Clamp(saturation, 0, 1);
        double x = c * (1 - Math.Abs((h % 2) - 1));
        double m = Math.Clamp(value, 0, 1) - c;

        (double r, double g, double b) = (int)h switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return ((int)Math.Round((r + m) * 255) << 16)
             | ((int)Math.Round((g + m) * 255) << 8)
             | (int)Math.Round((b + m) * 255);
    }

    private void TakeHsv(int packed)
    {
        double r = ((packed >> 16) & 0xFF) / 255.0;
        double g = ((packed >> 8) & 0xFF) / 255.0;
        double b = (packed & 0xFF) / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double span = max - min;

        _value = max;

        _pulling = true;

        try
        {
            // A grey keeps whatever hue the slider was on. Snapping it to red would move a
            // control nobody touched, and the colour it describes is the same either way.
            if (span > 0)
            {
                double hue =
                    max == r ? ((g - b) / span + (g < b ? 6 : 0)) :
                    max == g ? ((b - r) / span + 2) :
                    ((r - g) / span + 4);

                Hue = (int)Math.Round(hue * 60) % 360;
            }

            Saturation = max <= 0 ? Saturation : (int)Math.Round(span / max * 100);
        }
        finally
        {
            _pulling = false;
        }
    }

    /// <summary>
    /// Takes a colour from outside the palette. Nothing is written unless it is a change,
    /// because a colour input fires on every nudge of a slider in the system dialog.
    /// </summary>
    public void Choose(int packed) => Choose(packed, dragged: false);

    private void Choose(int packed, bool dragged)
    {
        packed &= 0xFFFFFF;

        if (packed == Packed) return;

        Packed = packed;
        HasColour = true;
        Selected = Palette.FirstOrDefault(s => s.Packed == packed);
        TakeHsv(packed);

        // Directly rather than through Selected: a colour with no swatch leaves that null,
        // and the notification it raises would not carry the colour anywhere.
        if (dragged) _owner.CommitSoon(Index, packed);
        else _owner.Commit(Index, packed);
    }

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

        if (packed >= 0)
        {
            Packed = packed & 0xFFFFFF;
            TakeHsv(Packed);
        }

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

        // The vocabulary is translated once, here, rather than per button: the same list
        // is handed to every row, and the index — not the label — is what goes down to
        // the device, so renaming an action for display cannot change what is written.
        IReadOnlyList<CapabilityChoice> actions =
        [
            .. state.Descriptor.Choices.Select(c => c with
            {
                Label = Localizer.Current.Say(c.Label),
                Hint = Localizer.Current.Say(c.Hint),
            }),
        ];

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
    private readonly string _label;
    private readonly string? _hint;

    internal ButtonAssignmentViewModel(
        ButtonMapCapabilityViewModel owner,
        CapabilityMember member,
        IReadOnlyList<CapabilityChoice> actions)
    {
        _owner = owner;

        Index = member.Index;
        _label = member.Label;
        _hint = member.Hint;
        Actions = actions;
    }

    public int Index { get; }

    public string Label => Localizer.Current.Say(_label);

    /// <summary>Why this button is special: it cycles DPI, it cannot be remapped.</summary>
    public string? Hint => Localizer.Current.Say(_hint);

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
/// <summary>
/// One tab above the layout. Carries the number and nothing else: the word beside it is
/// looked up when it is drawn, so switching language renames the tabs like everything
/// else. The catalogue key is the whole sentence with its hole in it — "Layer {0}" — and
/// not the bare word, because where the number goes is part of the translation.
/// </summary>
public sealed record KeyLayer(int Index)
{
    public string Label => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        Localizer.Current.Say("Layer {0}"),
        Index);
}

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
            .Select(i => new KeyLayer(i))];

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
    private readonly string _position;

    internal KeyCapViewModel(CapabilityMember member)
    {
        Index = member.Index;
        _position = member.Label;
    }

    public int Index { get; }

    /// <summary>Where the key sits, which never changes. The legend on it does.</summary>
    public string Position => Localizer.Current.Say(_position);

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

    /// <summary>The badge on the live slot: "active", "now".</summary>
    public abstract string ActiveLabel { get; }

    /// <summary>The button on a slot that holds something: "Apply", "Switch".</summary>
    public abstract string SwitchVerb { get; }

    /// <summary>The button on a free slot: "Write here…", "Pair…".</summary>
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

    public override string ActiveLabel => Maxwell.App.Localization.Localizer.Current.Say("active");

    public override string SwitchVerb => Maxwell.App.Localization.Localizer.Current.Say("Apply");

    /// <summary>
    /// The ellipsis is the promise of a question. Writing a profile into an empty slot
    /// still overwrites whatever the device believes is there, and the design system
    /// requires the warning before the write rather than after it.
    /// </summary>
    public override string FillVerb => Maxwell.App.Localization.Localizer.Current.Say("Record here…");
}

/// <summary>Paired hosts, and which of them the device is talking to.</summary>
public sealed class HostSlotsCapabilityViewModel : SlotListCapabilityViewModel
{
    public HostSlotsCapabilityViewModel(CapabilityState state, IDeviceSession? session)
        : base(state, session)
    {
    }

    public override string ActiveLabel => Maxwell.App.Localization.Localizer.Current.Say("now");

    public override string SwitchVerb => Maxwell.App.Localization.Localizer.Current.Say("Toggle");

    public override string FillVerb => Maxwell.App.Localization.Localizer.Current.Say("Pair…");
}

/// <summary>One slot of a <see cref="SlotListCapabilityViewModel"/>.</summary>
public sealed partial class CapabilitySlotViewModel : ObservableObject
{
    private readonly SlotListCapabilityViewModel _owner;
    private readonly string _label;
    private readonly string? _hint;

    internal CapabilitySlotViewModel(SlotListCapabilityViewModel owner, CapabilityMember member)
    {
        _owner = owner;

        Index = member.Index;
        _label = member.Label;
        _hint = member.Hint;
        IsEmpty = member.IsEmpty;
    }

    public int Index { get; }

    public string Label => Localizer.Current.Say(_label);

    /// <summary>What the slot holds, or what filling it would take.</summary>
    public string? Hint => Localizer.Current.Say(_hint);

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
