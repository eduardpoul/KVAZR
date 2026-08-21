namespace Maxwell.Core.Devices;

/// <summary>
/// The current reading of a capability.
///
/// The important case is <see cref="AbsentValue"/>. A capability that has not been read
/// yet is not zero and not "off" — showing a confident zero for something never asked
/// is a lie, and on this hardware it is a plausible-looking one: an unplugged boom
/// microphone simply refuses its level read. Every consumer has to handle absence
/// explicitly, which is why this is a closed hierarchy rather than a nullable int.
/// </summary>
public abstract record CapabilityValue
{
    /// <summary>Nothing has been read yet, or the device declined to answer.</summary>
    public static readonly CapabilityValue Absent = new AbsentValue();

    public bool IsAbsent => this is AbsentValue;

    public static CapabilityValue Of(int value) => new NumberValue(value);

    public static CapabilityValue Of(bool value) => new FlagValue(value);

    public static CapabilityValue Of(string value) => new TextValue(value);

    public static CapabilityValue Choice(int index) => new ChoiceValue(index);

    public static CapabilityValue Vector(IReadOnlyList<CapabilityValue> items) => new VectorValue(items);

    /// <summary>Convenience for the common all-numeric vector: a curve, a channel pair.</summary>
    public static CapabilityValue Numbers(params int[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new VectorValue([.. values.Select(Of)]);
    }

    /// <summary>The number, or <paramref name="fallback"/> when absent or another shape.</summary>
    public int AsNumber(int fallback = 0) => this is NumberValue n ? n.Value : fallback;

    public bool AsFlag(bool fallback = false) => this is FlagValue f ? f.Value : fallback;

    public string? AsText() => this is TextValue t ? t.Value : null;

    public int AsChoiceIndex(int fallback = -1) => this is ChoiceValue c ? c.Index : fallback;

    /// <summary>
    /// The members, or an empty list for any other shape. Emptiness is not absence:
    /// a composite that has not been read and one that genuinely has no members both
    /// come back empty here, and only <see cref="IsAbsent"/> separates them.
    /// </summary>
    public IReadOnlyList<CapabilityValue> AsVector() => this is VectorValue v ? v.Items : [];

    /// <summary>
    /// One member's reading, absent when the index is out of range. Out of range is a
    /// normal case rather than a bug: a driver may answer with fewer bands than the
    /// descriptor declares, and a control asking for the tenth must get "not read"
    /// instead of an exception or a zero.
    /// </summary>
    public CapabilityValue Member(int index)
    {
        IReadOnlyList<CapabilityValue> items = AsVector();
        return index >= 0 && index < items.Count ? items[index] : Absent;
    }
}

/// <summary>A scalar: battery percent, gain, sidetone level, DPI.</summary>
public sealed record NumberValue(int Value) : CapabilityValue;

/// <summary>A boolean: sidetone on, lighting on, Fn inversion.</summary>
public sealed record FlagValue(bool Value) : CapabilityValue;

/// <summary>Free text: firmware version, device name.</summary>
public sealed record TextValue(string Value) : CapabilityValue;

/// <summary>An index into <see cref="Capability.Choices"/>.</summary>
public sealed record ChoiceValue(int Index) : CapabilityValue;

/// <summary>
/// Several readings that move together, one per <see cref="Capability.Members"/> entry:
/// the two channels of a gain pair, the bands of an equaliser curve, the zones of a
/// lighting rig, one assignment per mouse button.
///
/// The members are themselves <see cref="CapabilityValue"/> rather than plain numbers,
/// and that is the only reason this shape is allowed to exist. A curve whose fourth band
/// has not been answered holds an <see cref="AbsentValue"/> in that slot, so the promise
/// the rest of this hierarchy makes — that "not read" can never be mistaken for zero —
/// carries one level down instead of stopping at the outermost value. A vector of ints
/// could not say it, and an equaliser drawn with a confident 0 dB in a band nobody asked
/// about is exactly the lie this file exists to prevent.
///
/// Nesting is deliberate too: a keyboard's layout is layers of keycodes, so its reading
/// is a vector of vectors and a layer that was never read is absent as a whole rather
/// than as forty-eight separate blanks.
/// </summary>
public sealed record VectorValue : CapabilityValue
{
    public VectorValue(IReadOnlyList<CapabilityValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items;
    }

    public IReadOnlyList<CapabilityValue> Items { get; }

    /// <summary>
    /// Structural, and not merely for tidiness. <c>DeviceSession</c> decides whether a
    /// reading moved by comparing it with the previous one, and a record's generated
    /// equality compares the list by reference. Left generated, every poll would look
    /// like a change and light the "changed on the device" ring on every curve and every
    /// channel pair, every few seconds, until the marker meant nothing.
    /// </summary>
    public bool Equals(VectorValue? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (CapabilityValue item in Items) hash.Add(item);
        return hash.ToHashCode();
    }
}

/// <summary>Not read. See the remarks on <see cref="CapabilityValue"/>.</summary>
public sealed record AbsentValue : CapabilityValue;
