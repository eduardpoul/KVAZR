using System.Globalization;
using System.Runtime.Versioning;
using Maxwell.Core.Drivers.Logitech;
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
    public static int Write(int sector, int slot, int dpi, bool keep = false)
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
        int down = original[at + 2] | (original[at + 3] << 8);

        Console.WriteLine($"[profile write] sector {sector}, resolution {slot}");
        Console.WriteLine();
        Console.WriteLine(
            $"  before   {was} across / {down} down in the sector, {Live(channel, open.Device)} DPI running");

        byte[] changed = [.. original];
        changed[at] = (byte)(dpi & 0xFF);
        changed[at + 1] = (byte)(dpi >> 8);

        // And down as well as across. Writing one axis and not the other is a mouse that
        // is fast up the screen and slow along it — which is what it did, because this
        // touched two bytes where the entry has four.
        changed[at + 2] = changed[at];
        changed[at + 3] = changed[at + 1];

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
        Console.WriteLine($"  running  {Live(channel, open.Device)} DPI  (what the mouse is doing now)");
        Console.WriteLine();
        Console.WriteLine(now == dpi ? "  It took." : "  It did not take.");
        if (keep)
        {
            Console.WriteLine("  Left as written.");
            return now == dpi ? 0 : 1;
        }

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

    /// <summary>
    /// Which of a profile's five resolutions the mouse is on, and where that is written.
    ///
    /// The header of a profile is four bytes and only the first is understood from
    /// elsewhere: it is the report rate, and it agrees with what the rate feature reports
    /// separately. One of the other three says which resolution is in force, and which one
    /// cannot be read off a single sample — the mouse is on 800, which is the first of its
    /// five, and two of the three bytes are zero.
    ///
    /// So it is settled by changing one and asking the mouse what it is doing: the
    /// resolution feature answers from the live setting rather than from the sector, so a
    /// byte that moves it is the byte. Then everything goes back.
    /// </summary>
    public static int Active(int offset, int index)
    {
        if (Open() is not { } open) return 1;

        using WindowsHidChannel channel = open.Channel;

        if (Enabled(channel, open.Device, open.Feature) is not { } sector)
        {
            Console.WriteLine("  No profile is switched on.");
            return 1;
        }

        if (Sector(channel, open.Device, open.Feature, sector, 255) is not { Length: 255 } original)
        {
            Console.WriteLine($"  Sector {sector} would not read in full — nothing was written.");
            return 1;
        }

        int before = Live(channel, open.Device);

        Console.WriteLine($"[active] profile in sector {sector}, header {Convert.ToHexString(original.AsSpan(0, 4))}");
        Console.WriteLine();
        Console.WriteLine($"  byte {offset} is {original[offset]}, and the mouse is running at {before} DPI");

        byte[] changed = [.. original];
        changed[offset] = (byte)index;
        Seal(changed);

        if (!Put(channel, open.Device, open.Feature, sector, changed))
        {
            Console.WriteLine("  write    refused");
            return 1;
        }

        int after = Live(channel, open.Device);

        Console.WriteLine($"  set byte {offset} to {index}, and the mouse is now at {after} DPI");
        Console.WriteLine();
        Console.WriteLine(after != before
            ? $"  That byte is the one: the mouse moved from {before} to {after}."
            : "  Nothing moved. That byte is not it.");

        Console.WriteLine();

        if (Put(channel, open.Device, open.Feature, sector, original) && Live(channel, open.Device) == before)
        {
            Console.WriteLine($"  restored {before} DPI");
            return 0;
        }

        Console.WriteLine($"  RESTORE FAILED — the mouse is not back at {before} DPI.");
        return 1;
    }

    /// <summary>
    /// The driver's own write, exercised end to end and put back.
    ///
    /// Through <see cref="PointerWriter"/> rather than through this file's copy of the
    /// same idea, because what needs proving is the code the page runs — a harness that
    /// tests its own reimplementation of a thing proves the reimplementation.
    /// </summary>
    public static int Driver(int wanted)
    {
        if (Open() is not { } open) return 1;

        using WindowsHidChannel channel = open.Channel;

        int before = Live(channel, open.Device);
        if (before < 0)
        {
            Console.WriteLine("  The mouse would not say what it is running at.");
            return 1;
        }

        Console.WriteLine($"[driver] the page's own write");
        Console.WriteLine();
        Console.WriteLine($"  before   {before} DPI");

        if (!PointerWriter.SetResolution(channel, open.Device, before, wanted))
        {
            Console.WriteLine("  write    refused");
            return 1;
        }

        int after = Live(channel, open.Device);
        Console.WriteLine($"  after    {after} DPI");
        Console.WriteLine();
        Console.WriteLine(after == wanted ? "  It took." : "  It did not take.");
        Console.WriteLine();

        if (PointerWriter.SetResolution(channel, open.Device, after, before) && Live(channel, open.Device) == before)
        {
            Console.WriteLine($"  restored {before} DPI");
            return after == wanted ? 0 : 1;
        }

        Console.WriteLine($"  RESTORE FAILED — the mouse is not back at {before} DPI.");
        return 1;
    }

    /// <summary>The driver's own rate write, exercised end to end and put back.</summary>
    public static int DriverRate(int hertz)
    {
        if (Open() is not { } open) return 1;

        using WindowsHidChannel channel = open.Channel;

        int before = LiveRate(channel, open.Device);
        int[] rates = [125, 250, 500, 1000, 2000, 4000, 8000];

        Console.WriteLine("[driver] the page's own rate write");
        Console.WriteLine();
        Console.WriteLine($"  before   {(before >= 0 && before < rates.Length ? rates[before] : -1)} Hz");

        if (!PointerWriter.SetReportRate(channel, open.Device, hertz))
        {
            Console.WriteLine("  write    refused");
            return 1;
        }

        int after = LiveRate(channel, open.Device);
        Console.WriteLine($"  after    {(after >= 0 && after < rates.Length ? rates[after] : -1)} Hz");
        Console.WriteLine();
        Console.WriteLine(rates[after] == hertz ? "  It took." : "  It did not take.");
        Console.WriteLine();

        if (PointerWriter.SetReportRate(channel, open.Device, rates[before]) && LiveRate(channel, open.Device) == before)
        {
            Console.WriteLine($"  restored {rates[before]} Hz");
            return 0;
        }

        Console.WriteLine($"  RESTORE FAILED — the rate is not back at {rates[before]} Hz.");
        return 1;
    }

    /// <summary>
    /// Whether the report rate can be changed the same way the resolution was.
    ///
    /// It should be: the first byte of a profile is the rate, and it agrees with what the
    /// rate feature reports separately. Whether writing it moves the mouse is a different
    /// question from whether it decodes, and only the mouse can answer it.
    /// </summary>
    public static int Rate(int index, int at = 0)
    {
        if (Open() is not { } open) return 1;

        using WindowsHidChannel channel = open.Channel;

        if (Enabled(channel, open.Device, open.Feature) is not { } sector
            || Sector(channel, open.Device, open.Feature, sector, 255) is not { Length: 255 } original)
        {
            Console.WriteLine("  The active profile would not read.");
            return 1;
        }

        int before = LiveRate(channel, open.Device);

        Console.WriteLine($"[rate] profile in sector {sector}");
        Console.WriteLine();
        Console.WriteLine($"  before   byte {at} is {original[at]}, and the mouse reports rate index {before}");

        byte[] changed = [.. original];
        changed[at] = (byte)index;
        Seal(changed);

        if (!Put(channel, open.Device, open.Feature, sector, changed))
        {
            Console.WriteLine("  write    refused");
            return 1;
        }

        int straight = LiveRate(channel, open.Device);
        Console.WriteLine($"  written  byte {at} is {index}, and the mouse still reports index {straight}");

        // Made to read its profile again. A setting the mouse takes from a profile it has
        // already loaded does not move because the sector changed — it moves when the
        // profile is loaded, so the profile is loaded again.
        Reload(channel, open.Device, open.Feature);

        int after = LiveRate(channel, open.Device);

        Console.WriteLine($"  reloaded and the mouse reports index {after}");
        Console.WriteLine();
        Console.WriteLine(after != before
            ? "  The rate follows the profile."
            : "  Nothing moved — the rate is not kept there, or not read from there.");

        Console.WriteLine();

        if (Put(channel, open.Device, open.Feature, sector, original) && LiveRate(channel, open.Device) == before)
        {
            Console.WriteLine($"  restored rate index {before}");
            return 0;
        }

        Console.WriteLine($"  RESTORE FAILED — the rate is not back at index {before}.");
        return 1;
    }

    /// <summary>
    /// The rate asked for directly, which is the other place it could live.
    ///
    /// The profile holds a rate byte and the mouse does not follow it — so either it is
    /// read only when a profile is loaded, or it is not the rate at all. Either way the
    /// feature that reports the rate has a function for setting it, and whether that one
    /// is refused the way the resolution's was is a question only the mouse answers.
    ///
    /// Two shapes tried, because the parameters are not certain: the rate alone, and a
    /// link followed by the rate. A wrong shape is refused rather than obeyed — the value
    /// has to be one of the rates the device said it offers.
    /// </summary>
    public static int RateDirect(int index)
    {
        if (Open() is not { } open) return 1;

        using WindowsHidChannel channel = open.Channel;

        if (HidPlusPlusReader.FeatureIndex(channel, open.Device, ExtendedRate) is not { } feature)
        {
            Console.WriteLine("  No report rate feature.");
            return 1;
        }

        int before = LiveRate(channel, open.Device);
        Console.WriteLine($"[rate] asked directly");
        Console.WriteLine();
        Console.WriteLine($"  before   index {before}");

        foreach ((string shape, byte[] parameters) in
                 new (string, byte[])[] { ("rate alone", [(byte)index]), ("link, rate", [0x00, (byte)index]) })
        {
            byte[]? reply = HidPlusPlusReader.Ask(channel, open.Device, feature, 0x03, parameters);

            string said = reply is null ? "нет ответа"
                : HidPlusPlus.IsError(reply) ? HidPlusPlus.Explain(reply)
                : "accepted";

            int now = LiveRate(channel, open.Device);
            Console.WriteLine($"  {shape,-12} {said,-24} index now {now}");

            if (now == index)
            {
                Console.WriteLine();
                Console.WriteLine("  It took.");
                Console.WriteLine();

                HidPlusPlusReader.Ask(channel, open.Device, feature, 0x03, parameters[..^1].Append((byte)before).ToArray());

                int back = LiveRate(channel, open.Device);
                Console.WriteLine(back == before
                    ? $"  restored index {before}"
                    : $"  RESTORE FAILED — the rate is at index {back}, not {before}.");

                return back == before ? 0 : 1;
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Neither shape moved it.");
        return 1;
    }

    /// <summary>
    /// Makes the mouse read its profile again, by telling it what it is already doing.
    ///
    /// Setting the onboard mode to the value it already holds is the least that could
    /// cause a reload and the least that could go wrong: nothing about how the mouse
    /// behaves is being changed, it is only being told again.
    /// </summary>
    private static void Reload(WindowsHidChannel channel, byte device, byte feature)
    {
        byte[]? mode = HidPlusPlusReader.Ask(channel, device, feature, 0x02);
        byte held = mode is { Length: > 4 } && !HidPlusPlus.IsError(mode) ? mode[4] : (byte)0x01;

        HidPlusPlusReader.Ask(channel, device, feature, 0x01, held);
    }

    /// <summary>The rate index the mouse reports, from its own feature.</summary>
    private static int LiveRate(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.FeatureIndex(channel, device, ExtendedRate) is not { } feature) return -1;

        byte[]? reply = HidPlusPlusReader.Ask(channel, device, feature, 0x02, 0x00);

        return reply is { Length: > 4 } && !HidPlusPlus.IsError(reply) ? reply[4] : -1;
    }

    private const ushort ExtendedRate = 0x8061;

    /// <summary>The resolution the mouse is actually running at, from its own feature.</summary>
    private static int Live(WindowsHidChannel channel, byte device)
    {
        if (HidPlusPlusReader.FeatureIndex(channel, device, ExtendedResolution) is not { } feature) return -1;

        byte[]? reply = HidPlusPlusReader.Ask(channel, device, feature, 0x05, 0x00);

        return reply is { Length: > 6 } && !HidPlusPlus.IsError(reply) ? (reply[5] << 8) | reply[6] : -1;
    }

    /// <summary>The sector of the one profile that is switched on.</summary>
    private static int? Enabled(WindowsHidChannel channel, byte device, byte feature)
    {
        if (Sector(channel, device, feature, 0, 255) is not { } directory) return null;

        for (int i = 0; i + 3 < directory.Length && directory[i] != 0xFF; i += 4)
        {
            int sector = (directory[i] << 8) | directory[i + 1];
            if (sector == 0) break;

            if (directory[i + 2] != 0) return sector;
        }

        return null;
    }

    private const ushort ExtendedResolution = 0x2202;

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
            // The ping first, and with patience. A mouse that has been still for a
            // minute is asleep, and the exchange that wakes it is the one that gets no
            // answer — so without this every second run of a probe reports that nothing
            // here keeps profiles, having asked a sleeping mouse.
            channel.Request(
                HidPlusPlus.Ping(channel.OutputLength, candidate),
                r => HidPlusPlus.Answers(r, candidate, HidPlusPlus.RootIndex, 0x01),
                HidPlusPlusReader.WakeWait);

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
