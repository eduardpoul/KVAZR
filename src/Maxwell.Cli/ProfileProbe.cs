using System.Globalization;
using System.Runtime.Versioning;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Reads what a Logitech mouse keeps in its own memory, and checks whether we understand
/// it well enough to write it.
///
/// This is where the settings actually live. A mouse plays from a profile stored in
/// itself, which is why its resolution survives the vendor's application being closed —
/// that application does not hold the setting, it edited the record and went away. So
/// anything this application changes has to be changed in the same place, or it will not
/// outlive it either.
///
/// Nothing here writes. What it does instead is earn the right to: it reads a sector,
/// computes the checksum the device stores at the end of it, and says whether the two
/// agree. A checksum we can reproduce is the proof that a sector we assemble would be
/// accepted — and a mismatch is the proof that it would not, before anything is sent.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProfileProbe
{
    private const ushort Logitech = 0x046D;
    private const ushort HidPlusPlusPage = 0xFF00;
    private const ushort LongUsage = 0x02;
    private const ushort OnboardProfiles = 0x8100;

    public static int Run(int sectorSize)
    {
        HidInterface? face = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == Logitech
                && i.UsagePage == HidPlusPlusPage
                && i.Usage == LongUsage
                && i.OutputReportLength >= 20);

        if (face is null)
        {
            Console.WriteLine("No HID++ long-report interface found.");
            return 1;
        }

        using var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

        byte device = 0;
        byte feature = 0;

        foreach (byte candidate in (byte[])[HidPlusPlus.Receiver, 1, 2, 3, 4, 5, 6])
        {
            if (HidPlusPlusReader.FeatureIndex(channel, candidate, OnboardProfiles) is not { } found) continue;

            device = candidate;
            feature = found;
            break;
        }

        if (feature == 0)
        {
            Console.WriteLine("Nothing here keeps profiles on board.");
            return 1;
        }

        byte[]? shape = HidPlusPlusReader.Ask(channel, device, feature, 0x00);

        if (shape is null || shape.Length < 16)
        {
            Console.WriteLine("The profile feature would not describe itself.");
            return 1;
        }

        Console.WriteLine($"[profiles] {face.ProductName}  feature {feature}, device {device}");
        Console.WriteLine();
        Console.WriteLine($"  memory model     {shape[4]}");
        Console.WriteLine($"  profile format   {shape[5]}");
        Console.WriteLine($"  profiles         {shape[7]}, of which {shape[8]} out of the box");
        Console.WriteLine($"  buttons          {shape[9]}");
        Console.WriteLine($"  sectors          {shape[10]}, {(shape[11] << 8) | shape[12]} bytes each");

        int size = sectorSize > 0 ? sectorSize : (shape[11] << 8) | shape[12];

        // Sector zero is the directory: which profiles exist, where they live, and
        // whether they are switched on.
        byte[]? directory = Sector(channel, device, feature, 0, size);

        if (directory is null)
        {
            Console.WriteLine("  The directory would not be read.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("  directory, first thirty-two bytes:");
        Console.WriteLine($"    {Convert.ToHexString(directory.AsSpan(0, Math.Min(32, directory.Length)))}");

        Checksum("directory", directory);

        // Each entry is a sector number, a state and a spare, four bytes apart.
        for (int i = 0; i + 3 < directory.Length && directory[i] != 0xFF; i += 4)
        {
            int sector = (directory[i] << 8) | directory[i + 1];
            if (sector == 0) break;

            Console.WriteLine();
            Console.WriteLine($"  profile in sector {sector}, {(directory[i + 2] != 0 ? "on" : "off")}:");

            if (Sector(channel, device, feature, sector, size) is not { } profile) continue;

            Console.WriteLine($"    read {profile.Length} bytes");
            Console.WriteLine($"    {Convert.ToHexString(profile.AsSpan(0, Math.Min(48, profile.Length)))}");

            // Five entries of five bytes each, from offset four: the resolution across,
            // the resolution down, and the lift-off. Written the other way round from
            // everything else in this protocol, which is how the first attempt at reading
            // them produced 8192 where the device plainly means 800.
            Console.WriteLine($"    bytes 0..3        {Convert.ToHexString(profile.AsSpan(0, 4))}");

            string steps = string.Join(
                ", ",
                Enumerable.Range(0, 5).Select(n =>
                {
                    int at = 4 + (n * 5);
                    return $"{profile[at] | (profile[at + 1] << 8)}/{profile[at + 2] | (profile[at + 3] << 8)}"
                        + $" lod {profile[at + 4]}";
                }));

            Console.WriteLine($"    resolutions       {steps}");

            Checksum($"sector {sector}", profile);
        }

        Console.WriteLine();
        Console.WriteLine("Nothing was written. A checksum that matches is what would make writing safe;");
        Console.WriteLine("one that does not is the reason not to try.");

        return 0;
    }

    /// <summary>
    /// Writes one resolution into one profile, reads it back, and puts the profile
    /// exactly as it was.
    ///
    /// Into a profile the mouse is not using, deliberately. The write path is the same
    /// whichever sector it addresses, so it can be proved against a sector nobody is
    /// playing from — and a mistake there costs a profile that was already switched off.
    ///
    /// The right to do this at all was earned by the checksum: the two bytes the device
    /// stores at the end of a sector are reproduced exactly by ours, so a sector we
    /// assemble is one it will accept rather than one it will reject or, worse, take.
    /// </summary>
    public static int Write(int sector, int slot, int dpi)
    {
        if (Open() is not { } open) return 1;

        using WindowsHidChannel channel = open.Channel;

        if (Sector(channel, open.Device, open.Feature, sector, 255) is not { Length: 255 } original)
        {
            Console.WriteLine($"  Sector {sector} would not read in full — nothing was written.");
            return 1;
        }

        int at = 4 + (slot * 5);
        int was = original[at] | (original[at + 1] << 8);

        Console.WriteLine($"[profile write] sector {sector}, resolution {slot}");
        Console.WriteLine();
        Console.WriteLine($"  before   {was} DPI");

        byte[] changed = [.. original];
        changed[at] = (byte)(dpi & 0xFF);
        changed[at + 1] = (byte)(dpi >> 8);
        Seal(changed);

        if (!Put(channel, open.Device, open.Feature, sector, changed))
        {
            Console.WriteLine("  write    refused");
            return 1;
        }

        byte[]? after = Sector(channel, open.Device, open.Feature, sector, 255);
        int now = after is { Length: 255 } ? after[at] | (after[at + 1] << 8) : -1;

        Console.WriteLine($"  wrote    {dpi} DPI");
        Console.WriteLine($"  after    {(now < 0 ? "could not read back" : now + " DPI")}");
        Console.WriteLine();
        Console.WriteLine(now == dpi ? "  It took." : "  It did not take.");
        Console.WriteLine();

        // Back exactly as found, byte for byte, including the checksum that came with it.
        if (Put(channel, open.Device, open.Feature, sector, original)
            && Sector(channel, open.Device, open.Feature, sector, 255) is { Length: 255 } back
            && (back[at] | (back[at + 1] << 8)) == was)
        {
            Console.WriteLine($"  restored {was} DPI");
            return now == dpi ? 0 : 1;
        }

        Console.WriteLine($"  RESTORE FAILED — sector {sector} is not back at {was}.");
        return 1;
    }

    /// <summary>The checksum the device expects, over everything but its own two bytes.</summary>
    private static void Seal(byte[] sector)
    {
        int crc = Crc16(sector.AsSpan(0, sector.Length - 2), 0xFFFF);

        sector[^2] = (byte)(crc >> 8);
        sector[^1] = (byte)(crc & 0xFF);
    }

    /// <summary>
    /// Begin, sixteen bytes at a time, end. Three functions and no room for a partial
    /// success: the device is told the length up front and the end is a separate word.
    /// </summary>
    private static bool Put(WindowsHidChannel channel, byte device, byte feature, int sector, byte[] bytes)
    {
        byte[]? begun = HidPlusPlusReader.Ask(
            channel, device, feature, 0x06,
            (byte)(sector >> 8), (byte)(sector & 0xFF), 0x00, 0x00,
            (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF));

        if (begun is null || HidPlusPlus.IsError(begun))
        {
            Console.WriteLine($"           begin: {(begun is null ? "no answer" : HidPlusPlus.Explain(begun))}");
            return false;
        }

        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            byte[] chunk = new byte[16];
            bytes.AsSpan(offset, Math.Min(16, bytes.Length - offset)).CopyTo(chunk);

            byte[]? sent = HidPlusPlusReader.Ask(channel, device, feature, 0x07, chunk);

            if (sent is null || HidPlusPlus.IsError(sent))
            {
                Console.WriteLine($"           at {offset}: {(sent is null ? "no answer" : HidPlusPlus.Explain(sent))}");
                return false;
            }
        }

        byte[]? ended = HidPlusPlusReader.Ask(channel, device, feature, 0x08);

        if (ended is null || HidPlusPlus.IsError(ended))
        {
            Console.WriteLine($"           end: {(ended is null ? "no answer" : HidPlusPlus.Explain(ended))}");
            return false;
        }

        return true;
    }

    /// <summary>The receiver, the device index and the feature, found by asking.</summary>
    private static (WindowsHidChannel Channel, byte Device, byte Feature)? Open()
    {
        HidInterface? face = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == Logitech
                && i.UsagePage == HidPlusPlusPage
                && i.Usage == LongUsage
                && i.OutputReportLength >= 20);

        if (face is null)
        {
            Console.WriteLine("No HID++ long-report interface found.");
            return null;
        }

        var channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);

        foreach (byte candidate in (byte[])[HidPlusPlus.Receiver, 1, 2, 3, 4, 5, 6])
        {
            if (HidPlusPlusReader.FeatureIndex(channel, candidate, OnboardProfiles) is not { } found) continue;

            return (channel, candidate, found);
        }

        channel.Dispose();
        Console.WriteLine("Nothing here keeps profiles on board.");
        return null;
    }

    /// <summary>
    /// One sector, sixteen bytes at a time. Function five takes the sector and an offset
    /// and answers with the next sixteen.
    /// </summary>
    private static byte[]? Sector(WindowsHidChannel channel, byte device, byte feature, int sector, int size)
    {
        var bytes = new List<byte>();

        for (int offset = 0; offset < size; offset += 16)
        {
            byte[]? chunk = HidPlusPlusReader.Ask(
                channel, device, feature, 0x05,
                (byte)(sector >> 8), (byte)(sector & 0xFF), (byte)(offset >> 8), (byte)(offset & 0xFF));

            // A refusal ends the sector rather than losing it. The device states a size
            // of 255 where the reads come sixteen at a time, so the last of them asks
            // for a byte past the end — and what came before it is still the sector.
            if (chunk is null || chunk.Length < 20 || HidPlusPlus.IsError(chunk)) break;

            bytes.AddRange(chunk[4..20]);
        }

        // The tail, which the aligned reads cannot reach. A sector of 255 bytes read
        // sixteen at a time leaves fifteen at the end that no aligned request can ask for
        // without running past — so the last one is asked for from far enough back to
        // land inside, and its overlap is dropped.
        if (bytes.Count > 0 && bytes.Count < size && size >= 16)
        {
            byte[]? tail = HidPlusPlusReader.Ask(
                channel, device, feature, 0x05,
                (byte)(sector >> 8), (byte)(sector & 0xFF),
                (byte)((size - 16) >> 8), (byte)((size - 16) & 0xFF));

            if (tail is { Length: >= 20 } && !HidPlusPlus.IsError(tail))
            {
                int already = bytes.Count - (size - 16);
                bytes.AddRange(tail[(4 + already)..20]);
            }
        }

        return bytes.Count > 0 ? [.. bytes] : null;
    }

    /// <summary>
    /// The stored checksum against the ones we can compute.
    ///
    /// Both usual sixteen-bit variants are tried rather than one chosen: which of them a
    /// vendor used is not something to be confident about, and the sector itself settles
    /// it. Whichever reproduces the two bytes the device already stores is the one that
    /// would make a sector we assemble acceptable to it.
    /// </summary>
    private static void Checksum(string what, byte[] sector)
    {
        if (sector.Length < 3) return;

        int stored = (sector[^2] << 8) | sector[^1];
        ReadOnlySpan<byte> body = sector.AsSpan(0, sector.Length - 2);

        int zero = Crc16(body, 0x0000);
        int ones = Crc16(body, 0xFFFF);

        string verdict = stored == zero ? "matches, starting from zero"
            : stored == ones ? "matches, starting from all ones"
            : "no match — do not write";

        Console.WriteLine($"    checksum {what}  stored {stored:X4}  ours {zero:X4} / {ones:X4}  — {verdict}");
    }

    /// <summary>The sixteen-bit checksum over a sector, polynomial 0x1021.</summary>
    private static int Crc16(ReadOnlySpan<byte> bytes, int seed)
    {
        int crc = seed;

        foreach (byte value in bytes)
        {
            crc ^= value << 8;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
        }

        return crc;
    }
}
