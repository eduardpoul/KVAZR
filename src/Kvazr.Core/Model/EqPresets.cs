namespace Kvazr.Core.Model;

/// <summary>
/// An EQ slot on the headset.
/// </summary>
/// <param name="Curve">
/// Gain per band, in dB, aligned to <see cref="EqPresets.Bands"/>. Null for the user
/// slots, whose curve lives on the headset rather than in this table.
/// </param>
public sealed record EqPreset(byte Index, string Name, bool IsUserSlot, IReadOnlyList<int>? Curve = null);

/// <summary>
/// The ten EQ slots the Maxwell exposes: six factory presets followed by four user
/// slots.
///
/// Two things about this table were wrong before, and neither is detectable from
/// inside the app — the only witness is the voice prompt in the wearer's ears, so
/// both cost a listening test to find.
///
/// The order is not the one printed in the Audeze user guide, which lists Treble
/// Boost last. It is the vendor application's own, read from the contiguous block of
/// string literals that follows its getCurrentEqName routine in Audeze.exe.
///
/// And the firmware numbers the slots from one, not zero. Confirmed against the
/// headset: index 1 announces "Audeze" and index 5 announces "Competition", a pair
/// that only holds with the order below. The firmware accepts 0 and rejects 11, so 0
/// is a real state that is not one of these ten; it gets no entry here rather than a
/// guessed name.
/// </summary>
public static class EqPresets
{
    /// <summary>
    /// The ten band centres, in Hz. Fixed: the Maxwell's equaliser is a graphic one, and
    /// the wearer moves gain only. Audeze HQ builds this same list for every preset.
    /// </summary>
    public static readonly IReadOnlyList<int> Bands =
        [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>
    /// The ten slots, with the factory curves lifted from Audeze HQ's own
    /// <c>getBuitInModelData</c> — read through its jump table rather than off the order
    /// the arms happen to sit in memory, which is not the same thing.
    ///
    /// The curves corroborate the slot order independently of the string block it was
    /// first taken from: index 2 lifts only the top, index 3 only the bottom, index 4
    /// both. A table numbered the way the user guide prints it would have Treble Boost
    /// raising bass.
    ///
    /// "Audeze" is flat on purpose — it is the headset's native tuning with nothing
    /// added, not a missing entry.
    /// </summary>
    public static readonly IReadOnlyList<EqPreset> All =
    [
        new(1, "Audeze", false, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        new(2, "Treble Boost", false, [0, 0, 0, 0, 0, 0, 0, 1, 2, 1]),
        new(3, "Bass Boost", false, [3, 2, 0, 0, 0, 0, 0, 0, 0, 0]),
        new(4, "Immersive", false, [3, 2, 0, 0, 0, 0, 0, 1, 2, 1]),
        new(5, "Competition", false, [-5, -5, -4, -2, 0, 0, 0, 4, -3, 2]),
        new(6, "Footsteps", false, [-1, -1, 3, 0, 0, 1, 4, 3, 0, 2]),
        new(7, "EQ1", true),
        new(8, "EQ2", true),
        new(9, "EQ3", true),
        new(10, "EQ4", true),
    ];

    /// <summary>
    /// The slot at <paramref name="index"/>, or null when the device reports something
    /// outside the known range — better an honest blank than a confidently wrong name.
    /// Looks the index up rather than treating it as a position, because the two stopped
    /// being the same thing once the table became one-based.
    /// </summary>
    public static EqPreset? ByIndex(int index) =>
        All.FirstOrDefault(preset => preset.Index == index);

    public static string NameOf(int index) => ByIndex(index)?.Name ?? "—";

    /// <summary>
    /// The slot after <paramref name="index"/>, wrapping back to the first. Anything
    /// outside the ten — including the unknown slot 0 and the "not read yet" sentinel —
    /// enters the cycle at the beginning instead of falling off it.
    /// </summary>
    public static byte Next(int index)
    {
        int position = IndexOfSlot(index);
        return position < 0 ? All[0].Index : All[(position + 1) % All.Count].Index;
    }

    private static int IndexOfSlot(int index)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Index == index) return i;

        return -1;
    }
}
