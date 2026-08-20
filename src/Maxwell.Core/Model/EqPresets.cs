namespace Maxwell.Core.Model;

/// <summary>An EQ slot on the headset.</summary>
public sealed record EqPreset(byte Index, string Name, bool IsUserSlot);

/// <summary>
/// The ten EQ slots the Maxwell exposes: six factory presets followed by four
/// user slots. Names come from the Audeze user guide, so they match what the voice
/// prompt announces when the preset changes on the headset itself.
/// </summary>
public static class EqPresets
{
    public static readonly IReadOnlyList<EqPreset> All =
    [
        new(0, "Audeze", false),
        new(1, "Bass Boost", false),
        new(2, "Immersive", false),
        new(3, "Competition", false),
        new(4, "Footsteps", false),
        new(5, "Treble Boost", false),
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
