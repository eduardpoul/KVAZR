using Maxwell.Race;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// The one experiment the whole host-side equaliser plan rests on: can the headset be
/// made to play a curve we hand it, without writing to its non-volatile memory?
///
/// Every step is a cut rather than a boost, and deliberately so. A coefficient set built
/// wrong is not a silent failure — it is whatever the DSP makes of the bytes, straight
/// into someone's ears. Asking only for attenuation means the worst outcome of a mistake
/// is quiet or ugly, not loud.
///
/// Nothing here touches NVDM. The coefficients live until the headset is power-cycled or
/// its preset changes, so switching preset undoes anything this does.
/// </summary>
internal static class EqPushProbe
{
    private static readonly (string Label, int[] Gains)[] Steps =
    [
        ("flat — nothing should change", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("TREBLE GUTTED — 2k..16k cut 12 dB", [0, 0, 0, 0, 0, 0, -12, -12, -12, -12]),
        ("BASS GUTTED — 32..250 cut 12 dB", [-12, -12, -12, -12, 0, 0, 0, 0, 0, 0]),

        // The unmistakable one. A cut in part of the spectrum needs an ear to
        // notice; a cut across all of it just sounds quieter, and nobody has to
        // wonder whether they imagined it.
        ("QUIET — every band cut 12 dB", [-12, -12, -12, -12, -12, -12, -12, -12, -12, -12]),
        ("flat again — should sound normal", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
    ];

    /// <summary>The frame as the session builds it, so the dry run checks the real thing.</summary>
    private static byte[] Frame(byte[] blob)
    {
        int length = blob.Length + 3;
        byte[] body = new byte[7 + blob.Length];
        body[0] = 0x05;
        body[1] = 0x5A;
        body[2] = (byte)length;
        body[3] = (byte)(length >> 8);
        body[4] = 0x03;
        body[5] = 0x0E;
        body[6] = 0x00;
        blob.CopyTo(body.AsSpan(7));
        return body;
    }


    /// <summary>
    /// Anything else on this machine holding the headset open. Only our own app polls it
    /// unprompted, and only its handle can splice a read into the middle of a transfer.
    /// </summary>
    private static readonly string[] AppProcessNames = ["Maxwell", "KVAZR"];

    private static IEnumerable<System.Diagnostics.Process> Rivals() =>
        AppProcessNames
            .SelectMany(System.Diagnostics.Process.GetProcessesByName)
            .Where(p => p.Id != Environment.ProcessId);

    public static async Task<int> RunAsync(
        bool dry, bool force, bool mine, string which, CancellationToken cancellationToken = default)
    {
        // Locked shut on purpose. Two runs, two crashed headsets — this stays behind an
        // explicit flag until a capture explains why, so it cannot go off by reflex.
        if (!dry && !force)
        {
            Console.WriteLine("REFUSING TO SEND.");
            Console.WriteLine("");
            Console.WriteLine("The blob was wrong for the first three runs and the headset crackled every time,");
            Console.WriteLine("once rebooting on its watchdog. A capture of Audeze HQ found it: 04 00 00 00 is not");
            Console.WriteLine("a fixed header, it is a count of blocks, and a curve is generated once per sample");
            Console.WriteLine("rate - 44100, 48000, 88200, 96000 - all four sent together. We were sending one");
            Console.WriteLine("block under a header claiming four, so the headset read past the end of what arrived");
            Console.WriteLine("and played whatever was there.");
            Console.WriteLine("");
            Console.WriteLine("That is fixed, and the frame now matches the captured one field for field: 851 bytes,");
            Console.WriteLine("844 of blob, four blocks of 103 words, the same 15 chunks. But it has not been sent");
            Console.WriteLine("to hardware since, so this stays behind the flag until it has.");
            Console.WriteLine("");
            Console.WriteLine("--dry   prints what would be sent and touches nothing.");
            Console.WriteLine("--force sends it.");
            return 1;
        }

        // Nothing else may talk to the headset while the chunks go out. Our own app is the
        // one thing on this machine guaranteed to be doing so.
        // Excluding this process by id, not by name: the probe's own executable is also
        // called maxwell.exe, so a name check finds itself and refuses every run.
        if (!dry && Rivals().Any())
        {
            Console.WriteLine("REFUSING TO SEND: the KVAZR app is running.");
            Console.WriteLine("");
            Console.WriteLine("Nothing else may talk to the headset while a transfer's chunks go out, and the app");
            Console.WriteLine("polls it on its own handle. This is hygiene rather than the cure it was once thought");
            Console.WriteLine("to be: closing the app did not stop the headset crackling.");
            Console.WriteLine("");
            Console.WriteLine("Close it and run this again.");
            return 1;
        }

        if (!mine && !AirohaPeq.Available)
        {
            Console.WriteLine("AirohaPeqLibrary.dll not found. Pass --mine to use our own maths instead.");
            return 1;
        }

        if (dry)
        {
            foreach ((string label, int[] gains) in Steps)
            {
                byte[] generated = mine
                    ? MaxwellEqCoefficients.Blob(AirohaPeq.Bands, gains)
                    : AirohaPeq.Blob(gains);
                List<byte[]> reports = RaceCodec.BuildChunkedReports(0x80, Frame(generated), 62);

                Console.WriteLine($"  {label}");
                Console.WriteLine($"    blob {generated.Length} bytes");
                Console.WriteLine($"    head {Convert.ToHexString(generated.AsSpan(0, 16))}");
                Console.WriteLine($"    {reports.Count} reports, counts [{string.Join(", ", reports.Select(r => r[1]))}]");
            }

            return 0;
        }

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

        int battery = await session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"interface: PID 0x{target.ProductId:X4}  {target.Transport}");
        Console.WriteLine($"battery:   {(battery < 0 ? "no reply — headset off" : battery + "%")}");

        if (battery < 0)
        {
            Console.WriteLine("Power the headset on and run this again.");
            return 1;
        }

        int preset = await session.ReadValueAsync(MaxwellFeatures.PeqIndex, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"preset:    {preset}");
        Console.WriteLine();

        // One transfer per run, on purpose. The first attempt sent four back to back, and
        // when the sound came out wrong there was no way to say which one did it — the
        // only certainty was that the flat curve, which cannot be audible, had been
        // accepted before the trouble started.
        (string Label, int[] Gains) step = Steps.FirstOrDefault(s => s.Label.StartsWith(which, StringComparison.OrdinalIgnoreCase));
        if (step.Gains is null)
        {
            Console.WriteLine($"unknown curve '{which}'. One of: {string.Join(", ", Steps.Select(s => s.Label))}");
            return 1;
        }

        Console.WriteLine($"sending: {step.Label}   [{(mine ? "our own maths" : "vendor library")}]");
        Console.WriteLine($"         [{string.Join(", ", step.Gains)}] dB, one block per rate: " +
                          $"{string.Join(", ", AirohaPeq.Rates.Select(r => r.Hz.ToString("0")))} Hz");

        byte[] blob = mine
            ? MaxwellEqCoefficients.Blob(AirohaPeq.Bands, step.Gains)
            : AirohaPeq.Blob(step.Gains);
        EqApplyResult result = await session.ApplyEqCoefficientsAsync(blob, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"         blob {blob.Length} bytes");
        Console.WriteLine($"         -> {result}");
        Console.WriteLine();

        if (result.Truncated)
        {
            Console.WriteLine("The headset is holding half a filter. Power-cycle it before listening to anything.");
            return 2;
        }

        Console.WriteLine(step.Label.StartsWith("flat", StringComparison.OrdinalIgnoreCase)
            ? "A flat curve must be inaudible. If anything changed, the blob is wrong, not the framing."
            : "If the cut was clearly audible and clean, a host-side equaliser works.");
        Console.WriteLine("Power-cycling the headset undoes this; nothing was written to its memory.");
        return 0;
    }
}
