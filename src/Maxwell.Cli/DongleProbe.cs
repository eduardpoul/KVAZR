using Maxwell.Race;
using Maxwell.Race.Protocol;

namespace Maxwell.Cli;

/// <summary>
/// Discovery sweep for the wireless dongle, which accepts every request and answers
/// none of them. Audeze's own application logs "Maxwell device for FW update only
/// found" in the same situation, yet in the same session it printed the headset's
/// firmware version — so something does get through, and this looks for what.
///
/// Everything sent from here is read-shaped with an empty payload. The 0x11xx family
/// is never touched: factory reset and re-pairing live there and are issued in the
/// READ-shaped form, so a blind sweep of it wipes the headset.
/// </summary>
public static class DongleProbe
{
    /// <summary>Command families worth sweeping, chosen because every command we know lives in one.</summary>
    private static readonly byte[] Families = [0x09, 0x0C, 0x0D, 0x10, 0x16, 0x2C];

    /// <summary>
    /// Never send these. The 0x11xx family is excluded wholesale rather than by
    /// sub-command, because the payload here is empty and what an empty-bodied factory
    /// reset does is not something worth finding out on someone's headset.
    /// </summary>
    private static bool IsForbidden(ushort command) =>
        (command >> 8) == 0x11
        || command == RaceCommands.Write
        || command == RaceCommands.ChatMixWrite;

    public static async Task<int> RunAsync(RaceSession session, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine("[probe] Dongle discovery sweep");
        output.WriteLine("        Read-shaped, empty payload. 0x11xx and both write namespaces excluded.");
        output.WriteLine();

        int answered = 0;
        answered += await KnownGoodAsync(session, output).ConfigureAwait(false);
        answered += await TargetByteAsync(session, output).ConfigureAwait(false);
        answered += await SweepAsync(session, output).ConfigureAwait(false);

        output.WriteLine();
        output.WriteLine(answered > 0
            ? $"  {answered} command(s) produced fresh data."
            : "  Nothing produced fresh data. The dongle acknowledges writes and never answers.");

        return answered > 0 ? 0 : 2;
    }

    /// <summary>
    /// The commands we already know the shape of, tried first and with a long window.
    /// If the memory read answers here, the dongle has a working command channel and
    /// the sweep below is looking for siblings rather than for a channel.
    /// </summary>
    private static async Task<int> KnownGoodAsync(RaceSession session, TextWriter output)
    {
        output.WriteLine("  [1] Known commands, 1200 ms window");

        int hits = 0;

        // 0x1680 is the one the dongle is documented to answer out of its own flash,
        // without the headset being involved at all.
        foreach (uint address in new[] { FirmwareAddresses.XboxDongle, FirmwareAddresses.PlayStationDongle })
        {
            IReadOnlyList<byte[]> fresh = await session
                .ProbeBodyAsync(RaceCodec.BuildMemoryReadBody(address), longFraming: true, windowMs: 1200)
                .ConfigureAwait(false);

            hits += Report(output, $"0x1680 @ 0x{address:X8}", fresh);
        }

        // The same read under short framing, in case our framing choice is the fault
        // rather than the dongle's silence.
        hits += Report(output, "0x1680 short framing",
            await session.ProbeBodyAsync(
                RaceCodec.BuildMemoryReadBody(FirmwareAddresses.XboxDongle),
                longFraming: false, windowMs: 1200).ConfigureAwait(false));

        foreach ((string name, ushort command) in new[]
                 {
                     ("battery 0x0CD6", RaceCommands.Battery),
                     ("status 0x0910", RaceCommands.Status),
                     ("read 0x0901", RaceCommands.Read),
                     ("chatmix 0x2C83", RaceCommands.ChatMixRead),
                 })
        {
            hits += Report(output, name,
                await session.ProbeBodyAsync(ReadBody(command), longFraming: false, windowMs: 1200)
                    .ConfigureAwait(false));
        }

        return hits;
    }

    /// <summary>
    /// Sweep the leading byte with a battery read, which is harmless and which we know
    /// the headset answers over the cable.
    ///
    /// The vendor library separates AVAirohaLocalDevice from AVAirohaRemoteDevice and
    /// carries a whole family of AVDual* tasks, so the protocol addresses the dongle
    /// and the headset behind it differently somewhere. This byte sits where such a
    /// selector would sit, and 0x80 is the only value anyone has tried.
    /// </summary>
    private static async Task<int> TargetByteAsync(RaceSession session, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("  [2] Leading byte 0x00-0xFF with a battery read");

        int hits = 0;

        for (int target = 0x00; target <= 0xFF; target++)
        {
            IReadOnlyList<byte[]> fresh = await session
                .ProbeBodyAsync(ReadBody(RaceCommands.Battery, (byte)target), longFraming: false, windowMs: 140)
                .ConfigureAwait(false);

            if (fresh.Count > 0) hits += Report(output, $"target 0x{target:X2}", fresh);
        }

        if (hits == 0) output.WriteLine("      no value routed anywhere");
        return hits;
    }

    private static async Task<int> SweepAsync(RaceSession session, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"  [3] Sweeping {Families.Length} families x 256, 120 ms each");

        int hits = 0;
        int sent = 0;

        foreach (byte family in Families)
        {
            for (int low = 0x00; low <= 0xFF; low++)
            {
                var command = (ushort)((family << 8) | low);
                if (IsForbidden(command)) continue;

                sent++;
                IReadOnlyList<byte[]> fresh = await session
                    .ProbeBodyAsync(ReadBody(command), longFraming: false, windowMs: 120)
                    .ConfigureAwait(false);

                if (fresh.Count > 0) hits += Report(output, $"0x{command:X4}", fresh);
            }

            output.WriteLine($"      0x{family:X2}xx done ({sent} sent, {hits} answered)");
        }

        return hits;
    }

    /// <summary>
    /// A read-shaped body with no payload: <c>80 05 5A &lt;len:u16&gt; &lt;cmd:u16&gt;</c>.
    ///
    /// The leading byte is not decoration. Under the short framing the report becomes
    /// <c>06 &lt;len&gt; 80 05 5A ...</c>, which puts the magic on byte 3 where the chip
    /// parses it. Drop that byte and everything shifts down one, the magic lands on
    /// byte 2, and every packet is silently discarded — which reads exactly like a
    /// device that has nothing to say.
    /// </summary>
    private static byte[] ReadBody(ushort command, byte target = DefaultTarget) =>
    [
        target, 0x05, 0x5A, 0x03, 0x00, (byte)command, (byte)(command >> 8),
    ];

    /// <summary>The leading byte every working request carries.</summary>
    private const byte DefaultTarget = 0x80;

    private static int Report(TextWriter output, string label, IReadOnlyList<byte[]> fresh)
    {
        if (fresh.Count == 0)
        {
            output.WriteLine($"      {label,-24} silent");
            return 0;
        }

        output.WriteLine($"      {label,-24} ANSWERED, {fresh.Count} buffer(s)");
        foreach (byte[] buffer in fresh.Take(3))
        {
            int length = Math.Min(buffer.Length, 32);
            output.WriteLine($"          {Convert.ToHexString(buffer.AsSpan(0, length))}");
        }

        return 1;
    }
}
