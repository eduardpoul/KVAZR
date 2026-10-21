using Kvazr.Race;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// The two features still marked broken in docs/VERIFY-LATER.md, asked of real hardware.
///
/// Both commands are now known exactly — they were lifted out of Audeze HQ the same way
/// the auto power-off frame was, by reading the constants the compiler baked into
/// <c>Devices::MaxwellTransport</c>. What is not known is how the headset behaves, and
/// that is what this settles:
///
/// * The equaliser answers only for the four user slots. This dumps each one raw and
///   decoded, so an empty slot can be told apart from a slot we are mis-parsing.
/// * Voice-prompt volume sends a frame identical to the one Audeze HQ sends, yet the
///   value has never stuck. This sweeps the range and reports which values the headset
///   keeps, which separates a scale problem from a refusal.
///
/// The voice-prompt sweep writes, and restores what it found when it is done. The
/// equaliser half is read only.
/// </summary>
internal static class EqProbe
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var factory = new WindowsHidTransportFactory();

        IReadOnlyList<MaxwellInterface> found = enumerator.EnumerateRaceInterfaces();
        if (found.Count == 0)
        {
            Console.WriteLine("No Audeze RACE interface found.");
            return 1;
        }

        MaxwellInterface target = found[0];
        using var session = new RaceSession(factory.Open(target));

        Console.WriteLine($"interface: PID 0x{target.ProductId:X4}  {target.Transport}");

        int battery = await session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"battery:   {(battery < 0 ? "no reply — headset asleep or off" : battery + "%")}");
        Console.WriteLine();

        if (battery < 0)
        {
            Console.WriteLine("The headset is not answering. Power it on and run this again.");
            return 1;
        }

        await EqualiserAsync(session, cancellationToken).ConfigureAwait(false);
        Console.WriteLine();
        await VoicePromptsAsync(session, cancellationToken).ConfigureAwait(false);

        return 0;
    }

    // ---------- equaliser ----------

    private static async Task EqualiserAsync(RaceSession session, CancellationToken cancellationToken)
    {
        Console.WriteLine("EQUALISER");
        Console.WriteLine("=========");

        int preset = await session
            .ReadValueAsync(MaxwellFeatures.PeqIndex, cancellationToken)
            .ConfigureAwait(false);

        int mapped = RaceCommands.EqSlotOfPreset(preset);
        Console.WriteLine($"  current preset slot: {preset}" +
                          (mapped >= 0 ? $"  (user, equaliser slot {mapped})" : "  (built-in — no bands stored)"));
        Console.WriteLine();

        var raw = new List<byte[]>();
        session.ReportReceived += report => { if (raw.Count < 8) raw.Add(report); };

        for (byte slot = 0; slot <= 3; slot++)
        {
            raw.Clear();

            MaxwellEqReply? reply = await session
                .ReadEqBandsAsync(slot, cancellationToken)
                .ConfigureAwait(false);

            if (reply is null)
            {
                Console.WriteLine($"  slot {slot}:  nothing our parser accepted");
                foreach (byte[] report in raw)
                {
                    Console.WriteLine($"      in  {Convert.ToHexString(report.AsSpan(0, Math.Min(28, report.Length)))}");
                }

                continue;
            }

            if (reply.IsEmpty)
            {
                Console.WriteLine($"  slot {slot}:  status 0x{reply.Status:X2}" +
                                  (reply.Status == MaxwellEqReply.Empty
                                      ? "  — never written, nothing stored"
                                      : "  — refused"));
                continue;
            }

            if (reply.Bands is not { } curve)
            {
                Console.WriteLine($"  slot {slot}:  status ok, but the blob is not a whole number of bands");
                continue;
            }

            Console.WriteLine($"  slot {slot}:  header {Convert.ToHexString(curve.Header)}" +
                              $"  rate {(curve.SampleRate == 0 ? "?" : curve.SampleRate.ToString())}" +
                              $"  bands {curve.Bands.Count}");

            foreach (MaxwellEqBand band in curve.Bands)
            {
                Console.WriteLine($"       tag {band.Tag:X4}  {band}");
            }
        }
    }

    // ---------- voice prompts ----------

    private static async Task VoicePromptsAsync(RaceSession session, CancellationToken cancellationToken)
    {
        Console.WriteLine("VOICE PROMPT VOLUME");
        Console.WriteLine("===================");

        int start = await session
            .ReadValueAsync(MaxwellFeatures.VpLevel, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"  current: {start}");

        if (start < 0)
        {
            Console.WriteLine("  the headset does not answer this register at all.");
            return;
        }

        // Spread across the byte, plus the small values a three-step control would use.
        // If only the low ones stick, the range is a handful of steps and not 0..255.
        byte[] ladder = [0, 1, 2, 3, 4, 5, 8, 16, 32, 64, 128, 200, 255];
        var kept = new List<byte>();

        Console.WriteLine();
        foreach (byte value in ladder)
        {
            bool wrote = await session
                .WriteValueAsync(MaxwellFeatures.VpLevel, value, cancellationToken)
                .ConfigureAwait(false);

            int back = await session
                .ReadValueAsync(MaxwellFeatures.VpLevel, cancellationToken)
                .ConfigureAwait(false);

            string verdict = back == value ? "kept" : $"became {back}";
            if (back == value) kept.Add(value);

            Console.WriteLine($"    wrote {value,3}  acked={wrote,-5}  read {back,3}   {verdict}");
        }

        Console.WriteLine();
        Console.WriteLine($"  restoring {start} ...");
        await session.WriteValueAsync(MaxwellFeatures.VpLevel, (byte)start, cancellationToken)
            .ConfigureAwait(false);
        int final = await session.ReadValueAsync(MaxwellFeatures.VpLevel, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"  now {final}");

        Console.WriteLine();
        Console.WriteLine(kept.Count switch
        {
            0 => "RESULT: nothing sticks — the register is read-only on this firmware.",
            _ when kept.Count == ladder.Length => "RESULT: the whole byte sticks; the control works as declared.",
            _ => $"RESULT: only these stick: {string.Join(", ", kept)} — the range is smaller than 0..255.",
        });
    }
}
