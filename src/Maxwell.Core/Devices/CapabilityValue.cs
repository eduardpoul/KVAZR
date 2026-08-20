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

    /// <summary>The number, or <paramref name="fallback"/> when absent or another shape.</summary>
    public int AsNumber(int fallback = 0) => this is NumberValue n ? n.Value : fallback;

    public bool AsFlag(bool fallback = false) => this is FlagValue f ? f.Value : fallback;

    public string? AsText() => this is TextValue t ? t.Value : null;

    public int AsChoiceIndex(int fallback = -1) => this is ChoiceValue c ? c.Index : fallback;
}

/// <summary>A scalar: battery percent, gain, sidetone level, DPI.</summary>
public sealed record NumberValue(int Value) : CapabilityValue;

/// <summary>A boolean: sidetone on, lighting on, Fn inversion.</summary>
public sealed record FlagValue(bool Value) : CapabilityValue;

/// <summary>Free text: firmware version, device name.</summary>
public sealed record TextValue(string Value) : CapabilityValue;

/// <summary>An index into <see cref="Capability.Choices"/>.</summary>
public sealed record ChoiceValue(int Index) : CapabilityValue;

/// <summary>Not read. See the remarks on <see cref="CapabilityValue"/>.</summary>
public sealed record AbsentValue : CapabilityValue;
