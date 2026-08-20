namespace Maxwell.Core.Model;

/// <summary>An EQ slot on the headset.</summary>
public sealed record EqPreset(byte Index, string Name, bool IsUserSlot);

/// <summary>
/// The ten EQ slots the Maxwell exposes: six factory presets followed by four
/// user slots.
///
/// The order is NOT the one printed in the Audeze user guide. The guide lists Treble
/// Boost last; the firmware puts it at index 1, which shifts every preset between it
/// and the end. Taking the guide at face value meant the headset announced a different
/// preset than the one selected. The order here is the vendor application's own:
/// the names sit as one contiguous literal block immediately after its
/// getCurrentEqName routine, in this sequence.
/// </summary>
public static class EqPresets
{
    public static readonly IReadOnlyList<EqPreset> All =
    [
        new(0, "Audeze", false),
        new(1, "Treble Boost", false),
        new(2, "Bass Boost", false),
        new(3, "Immersive", false),
        new(4, "Competition", false),
        new(5, "Footsteps", false),
        new(6, "EQ1", true),
        new(7, "EQ2", true),
        new(8, "EQ3", true),
        new(9, "EQ4", true),
    ];

    /// <summary>
    /// The slot at <paramref name="index"/>, or null when the device reports something
    /// outside the known range — better an honest blank than a confidently wrong name.
    /// </summary>
    public static EqPreset? ByIndex(int index) =>
        index >= 0 && index < All.Count ? All[index] : null;

    public static string NameOf(int index) => ByIndex(index)?.Name ?? "—";
}
