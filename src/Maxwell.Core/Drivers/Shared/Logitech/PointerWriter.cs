using System.Runtime.Versioning;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Logitech;

/// <summary>
/// Changes a resolution where the change survives us: in the mouse's own memory.
///
/// The obvious route does not work and the reason is worth keeping. A mouse running its
/// own stored profile refuses a resolution sent from a computer — the refusal is
/// indistinguishable from the function not existing — because the profile is what it is
/// playing from. Which is also why the vendor's own application can be closed without
/// its settings going with it: it does not hold them, it edited the record.
///
/// So this edits the same record. Read the sector, change the entry the mouse is
/// currently on, put the checksum back, write it. Measured: the live resolution follows
/// immediately, and it is still there when everything here is shut down.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PointerWriter
{
    private const ushort OnboardProfiles = 0x8100;

    /// <summary>
    /// The size of a profile sector. Stated by the device and read back from it rather
    /// than assumed — but the reads come sixteen at a time and this one is not a multiple
    /// of sixteen, which is a fact about the format worth meeting rather than rounding.
    /// </summary>
    private const int SectorSize = 255;

    /// <summary>
    /// Puts the mouse on a resolution, and says whether it took.
    ///
    /// The rung the mouse is standing on is the only one that changes what it is doing —
    /// which rung that is cannot be set by anything found so far, so it is the one that
    /// has to carry the new value.
    ///
    /// Written naively that loses a resolution. Standing on 800 and asked for 1600, the
    /// obvious write leaves the ladder 1600/1200/1600/2400/3200: two of one value, none
    /// of the other, and a mouse whose own button now cycles through that. Measured on
    /// hardware, and reported by the person it happened to.
    ///
    /// So the two rungs trade places instead. The mouse ends up on the asked-for value
    /// and still stores all five it stored before — only their order changed, and their
    /// order is the order its button walks them in. A value not on the ladder at all has
    /// nowhere to trade with and is simply written.
    /// </summary>
    public static bool SetResolution(WindowsHidChannel channel, byte device, int current, int wanted)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (current == wanted) return true;

        if (HidPlusPlusReader.FeatureIndex(channel, device, OnboardProfiles) is not { } feature) return false;
        if (Enabled(channel, device, feature) is not { } sector) return false;
        if (Read(channel, device, feature, sector) is not { Length: SectorSize } profile) return false;

        if (!Trade(profile, current, wanted)) return false;

        Seal(profile);

        return Write(channel, device, feature, sector, profile);
    }

    /// <summary>
    /// The two rungs, traded, in a sector held in memory. Separate from the sending of it
    /// because this is the part that can be wrong without any device saying so: the write
    /// succeeds either way, and what it costs is a resolution quietly gone from a ladder.
    ///
    /// False when the mouse is standing on a value the ladder does not contain, which
    /// would leave nowhere to put the old one.
    /// </summary>
    internal static bool Trade(byte[] profile, int current, int wanted)
    {
        ArgumentNullException.ThrowIfNull(profile);

        int at = Entry(profile, current);
        if (at < 0) return false;

        int trade = Entry(profile, wanted);

        Rung(profile, at, wanted);
        if (trade >= 0 && trade != at) Rung(profile, trade, current);

        return true;
    }

    /// <summary>
    /// One rung, both ways. Across and down together: the two are one setting on every
    /// mouse this has been put to, and a profile whose axes disagree is a mouse that
    /// drifts — fast up the screen and slow along it, which is what it did the once they
    /// were written apart.
    /// </summary>
    private static void Rung(byte[] profile, int at, int dpi)
    {
        profile[at] = (byte)(dpi & 0xFF);
        profile[at + 1] = (byte)(dpi >> 8);
        profile[at + 2] = profile[at];
        profile[at + 3] = profile[at + 1];
    }

    /// <summary>
    /// Writes the whole ladder — every stored resolution at once.
    ///
    /// All five in one sector write rather than one write each. Not tidiness: this is
    /// flash inside somebody's mouse, and five writes to change one rung is four more
    /// erase cycles than the change needs. The profile even counts them — there is a
    /// write_count field in the record.
    ///
    /// A rung given as zero is left as it was, so a caller that only knows about one
    /// change does not have to know the other four.
    /// </summary>
    public static bool SetSteps(WindowsHidChannel channel, byte device, IReadOnlyList<int> wanted)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(wanted);

        if (HidPlusPlusReader.FeatureIndex(channel, device, OnboardProfiles) is not { } feature) return false;
        if (Enabled(channel, device, feature) is not { } sector) return false;
        if (Read(channel, device, feature, sector) is not { Length: SectorSize } profile) return false;

        bool touched = false;

        for (int slot = 0; slot < 5 && slot < wanted.Count; slot++)
        {
            if (wanted[slot] <= 0) continue;

            int at = 4 + (slot * 5);
            if ((profile[at] | (profile[at + 1] << 8)) == wanted[slot]) continue;

            profile[at] = (byte)(wanted[slot] & 0xFF);
            profile[at + 1] = (byte)(wanted[slot] >> 8);

            // Down as well as across, for the same reason SetResolution does it: a rung
            // whose axes disagree is a mouse that drifts.
            profile[at + 2] = profile[at];
            profile[at + 3] = profile[at + 1];

            touched = true;
        }

        if (!touched) return true;

        Seal(profile);

        return Write(channel, device, feature, sector, profile);
    }

    /// <summary>
    /// Sets how often the mouse reports, in the profile, where it survives us.
    ///
    /// Asked for directly the mouse answers "invalid argument", and Solaar's own note on
    /// this setting says it may need the onboard profiles turned off to take. Both are
    /// true and both are beside the point: the rate is in the profile, and writing it
    /// there moves the mouse at once.
    ///
    /// Which byte holds it is not decided here. A profile has two, one for each kind of
    /// link a mouse can be on, and they read the same on a mouse whose two links are set
    /// alike — so telling them apart by reading is impossible. Instead one is written and
    /// the mouse is asked what it is doing: the byte that moves it is the byte, and the
    /// other is put back untouched.
    /// </summary>
    public static bool SetReportRate(WindowsHidChannel channel, byte device, int hertz)
    {
        ArgumentNullException.ThrowIfNull(channel);

        int index = Array.IndexOf(PointerReader.Rates, hertz);
        if (index < 0) return false;

        if (HidPlusPlusReader.FeatureIndex(channel, device, OnboardProfiles) is not { } feature) return false;
        if (Enabled(channel, device, feature) is not { } sector) return false;
        if (Read(channel, device, feature, sector) is not { Length: SectorSize } original) return false;

        // The link in use first, because on the mouse this was measured on that is the
        // second byte and trying it first costs one exchange instead of three.
        foreach (int at in (int[])[1, 0])
        {
            byte[] changed = [.. original];
            changed[at] = (byte)index;
            Seal(changed);

            if (!Write(channel, device, feature, sector, changed)) continue;

            if (Live(channel, device) == index) return true;

            // It was accepted and did nothing, which means that byte is the other link's.
            // Put it back before trying the other, or a mouse would end up with both
            // links changed to settle one.
            Write(channel, device, feature, sector, original);
        }

        return false;
    }

    /// <summary>Which rate the mouse says it is using, as an index into the protocol's list.</summary>
    private static int Live(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.FeatureIndex(channel, device, ExtendedRate) is not { } feature) return -1;

        byte[]? reply = HidPlusPlusReader.Ask(channel, device, feature, 0x02, 0x00);

        return reply is { Length: > 4 } && !HidPlusPlus.IsError(reply) ? reply[4] : -1;
    }

    private const ushort ExtendedRate = 0x8061;

    /// <summary>
    /// Where in the sector the resolution in force is written.
    ///
    /// Five entries of five bytes from offset four: the resolution across, the resolution
    /// down, and the lift-off, each written low byte first — against a protocol that puts
    /// it last everywhere else, which is the sort of thing only reading real bytes tells
    /// you.
    /// </summary>
    private static int Entry(byte[] profile, int current)
    {
        for (int slot = 0; slot < 5; slot++)
        {
            int at = 4 + (slot * 5);

            if ((profile[at] | (profile[at + 1] << 8)) == current) return at;
        }

        return -1;
    }

    /// <summary>The sector of the one profile that is switched on.</summary>
    private static int? Enabled(WindowsHidChannel channel, byte device, byte feature)
    {
        if (Read(channel, device, feature, 0) is not { } directory) return null;

        for (int i = 0; i + 3 < directory.Length && directory[i] != 0xFF; i += 4)
        {
            int sector = (directory[i] << 8) | directory[i + 1];
            if (sector == 0) break;

            if (directory[i + 2] != 0) return sector;
        }

        return null;
    }

    /// <summary>
    /// One sector, sixteen bytes at a time, with the tail fetched from far enough back to
    /// land inside it — a sector of 255 read in sixteens leaves fifteen no aligned request
    /// can ask for.
    /// </summary>
    private static byte[]? Read(WindowsHidChannel channel, byte device, byte feature, int sector)
    {
        var bytes = new List<byte>();

        for (int offset = 0; offset < SectorSize; offset += 16)
        {
            byte[]? chunk = HidPlusPlusReader.Ask(
                channel, device, feature, 0x05,
                (byte)(sector >> 8), (byte)(sector & 0xFF), (byte)(offset >> 8), (byte)(offset & 0xFF));

            if (chunk is null || chunk.Length < 20 || HidPlusPlus.IsError(chunk)) break;

            bytes.AddRange(chunk[4..20]);
        }

        if (bytes.Count is 0 or >= SectorSize) return bytes.Count > 0 ? [.. bytes] : null;

        byte[]? tail = HidPlusPlusReader.Ask(
            channel, device, feature, 0x05,
            (byte)(sector >> 8), (byte)(sector & 0xFF),
            (byte)((SectorSize - 16) >> 8), (byte)((SectorSize - 16) & 0xFF));

        if (tail is not { Length: >= 20 } || HidPlusPlus.IsError(tail)) return null;

        bytes.AddRange(tail[(4 + (bytes.Count - (SectorSize - 16)))..20]);

        return [.. bytes];
    }

    /// <summary>Begin with a length, sixteen bytes at a time, then finish.</summary>
    private static bool Write(WindowsHidChannel channel, byte device, byte feature, int sector, byte[] bytes)
    {
        if (Refused(HidPlusPlusReader.Ask(
            channel, device, feature, 0x06,
            (byte)(sector >> 8), (byte)(sector & 0xFF), 0x00, 0x00,
            (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF))))
        {
            return false;
        }

        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            byte[] chunk = new byte[16];
            bytes.AsSpan(offset, Math.Min(16, bytes.Length - offset)).CopyTo(chunk);

            if (Refused(HidPlusPlusReader.Ask(channel, device, feature, 0x07, chunk))) return false;
        }

        return !Refused(HidPlusPlusReader.Ask(channel, device, feature, 0x08));
    }

    private static bool Refused(byte[]? reply) => reply is null || HidPlusPlus.IsError(reply);

    /// <summary>
    /// The checksum the device keeps in the last two bytes of a sector, sixteen bits,
    /// polynomial 0x1021, starting from all ones.
    ///
    /// Established by computing it over a sector the mouse had already written and
    /// finding the two answers identical. Until they were, a sector assembled here was
    /// one the device would refuse — or take, and be broken by.
    /// </summary>
    private static void Seal(byte[] sector)
    {
        int crc = 0xFFFF;

        foreach (byte value in sector.AsSpan(0, sector.Length - 2))
        {
            crc ^= value << 8;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
        }

        sector[^2] = (byte)(crc >> 8);
        sector[^1] = (byte)(crc & 0xFF);
    }
}
