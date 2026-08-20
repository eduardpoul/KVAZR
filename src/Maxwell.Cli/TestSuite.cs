using Maxwell.Race;

namespace Maxwell.Cli;

/// <summary>
/// Exercises every known function and puts each setting back the way it was.
/// Each write is verified by reading it back, then reverted and verified again,
/// and a final snapshot is diffed against the opening one to prove nothing drifted.
/// Undocumented 0x0900 write registers are deliberately not touched: an unknown
/// register cannot be restored if it turns out to be destructive.
/// </summary>
internal static class TestSuite
{
    internal sealed record Result(string Area, string Command, string Status, string Detail);

    private static readonly byte[] ReadableSubs = BuildRange();

    private static byte[] BuildRange()
    {
        var subs = new byte[0x40];
        for (int i = 0; i < subs.Length; i++) subs[i] = (byte)i;
        return subs;
    }

    public static List<Result> Run(RaceSession session, TextWriter output)
    {
        var results = new List<Result>();

        output.WriteLine("  capturing baseline...");
        Dictionary<byte, int> baseline = Snapshot(session);
        output.WriteLine($"  baseline: {baseline.Count} readable sub-commands");
        output.WriteLine();

        RunReadTests(session, results, output);
        RunWriteTests(session, results, output);
        RunUnreachedTests(session, results, output);

        output.WriteLine();
        output.WriteLine("  verifying every setting is back where it started...");
        Dictionary<byte, int> final = Snapshot(session);

        var drift = new List<string>();
        foreach (byte sub in baseline.Keys.Union(final.Keys).OrderBy(s => s))
        {
            baseline.TryGetValue(sub, out int before);
            final.TryGetValue(sub, out int after);
            if (before != after) drift.Add($"0x{sub:X2}: {before} -> {after}");
        }

        if (drift.Count == 0)
        {
            output.WriteLine("  RESTORED: final snapshot matches the baseline exactly.");
            results.Add(new Result("Safety", "restore check", "PASS", "all values back to baseline"));
        }
        else
        {
            output.WriteLine($"  DRIFT on {drift.Count} value(s): {string.Join(", ", drift)}");
            results.Add(new Result("Safety", "restore check", "FAIL", string.Join("; ", drift)));
        }

        return results;
    }

    private static Dictionary<byte, int> Snapshot(RaceSession session)
    {
        var values = new Dictionary<byte, int>();
        foreach (byte sub in ReadableSubs)
        {
            int value = session.ReadValue(sub, attempts: 2, windowMs: 140);
            if (value >= 0) values[sub] = value;
        }
        return values;
    }

    // ---------- reads ----------

    private static void RunReadTests(RaceSession session, List<Result> results, TextWriter output)
    {
        output.WriteLine("  -- reads --");

        int battery = session.ReadBattery();
        Add(results, output, "Battery", "0x0CD6", battery >= 0, battery >= 0 ? $"{battery}%" : "no reply");

        foreach ((byte sub, string name) in new[]
        {
            (RaceSession.SubGainMaster, "Master gain"),
            (RaceSession.SubGainLeft, "Left gain"),
            (RaceSession.SubGainRight, "Right gain"),
            ((byte)0x25, "Balance slider"),
            ((byte)0x27, "Volume"),
            ((byte)0x20, "Source preference"),
            (RaceSession.SubAudioSource, "Audio source state"),
        })
        {
            int value = session.ReadValue(sub);
            Add(results, output, name, $"0x0901 sub 0x{sub:X2}", value >= 0,
                value >= 0 ? $"{value} (0x{value:X2})" : "no reply");
        }

        RacePacket? status = session
            .SendRaw(new byte[] { 0x80, 0x05, 0x5A, 0x03, 0x00, 0x10, 0x09 })
            .FirstOrDefault(p => p.Command == 0x0910);
        Add(results, output, "Status struct", "0x0910", status is not null,
            status is not null ? status.Hex : "no reply");

        string? version = null;
        foreach (uint address in FirmwareAddresses.For(session.Interface.Transport))
        {
            version = session.ReadVersionString(address);
            if (version is not null) break;
        }
        Add(results, output, "Firmware version", "0x1680", version is not null, version ?? "not resolved");
    }

    // ---------- writes, each reverted ----------

    private static void RunWriteTests(RaceSession session, List<Result> results, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("  -- writes (each one reverted immediately) --");

        TestWrite(session, results, output, RaceSession.SubGainMaster, "Master gain", GainProbe);
        TestWrite(session, results, output, RaceSession.SubGainLeft, "Left gain", GainProbe);
        TestWrite(session, results, output, RaceSession.SubGainRight, "Right gain", GainProbe);
        TestWrite(session, results, output, 0x25, "Balance slider", current => (byte)(current == 0x80 ? 0x7F : 0x80));
        TestWrite(session, results, output, RaceSession.SubAudioSource, "Audio source state",
            current => (byte)(current == 0x0A ? 0x00 : 0x0A));
    }

    /// <summary>Step one away from the current gain, staying clear of the reserved codes.</summary>
    private static byte GainProbe(int current)
    {
        int candidate = current > 0 ? current - 1 : current + 1;
        while (RaceSession.IsReservedGain(candidate) || candidate is < 0 or > RaceSession.MaxGain)
            candidate--;
        return (byte)candidate;
    }

    private static void TestWrite(RaceSession session, List<Result> results, TextWriter output,
        byte sub, string name, Func<int, byte> pickProbe)
    {
        int original = session.ReadValue(sub);
        if (original < 0)
        {
            Add(results, output, name + " (write)", $"0x0900 sub 0x{sub:X2}", false, "cannot read original, skipped");
            return;
        }

        byte probe = pickProbe(original);

        try
        {
            bool applied = session.WriteValue(sub, probe);
            int observed = session.ReadValue(sub);

            bool restored = session.WriteValue(sub, (byte)original);
            int back = session.ReadValue(sub);

            string detail = $"{original} -> {probe} ({(applied && observed == probe ? "took" : $"read {observed}")}), " +
                            $"restored to {back}{(restored && back == original ? "" : " FAILED")}";

            Add(results, output, name + " (write)", $"0x0900 sub 0x{sub:X2}",
                applied && observed == probe && back == original, detail);

            if (back != original)
                output.WriteLine($"     !! {name} did not revert: expected {original}, got {back}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Add(results, output, name + " (write)", $"0x0900 sub 0x{sub:X2}", false, "blocked: " + ex.Message);
        }
    }

    // ---------- features whose command is not known ----------

    private static void RunUnreachedTests(RaceSession session, List<Result> results, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("  -- features with no confirmed command --");

        // ChatMix turned out to be readable on 0x2C83, so its original value is
        // known and the write can be exercised and reverted like the others.
        int chatMix = session.ReadChatMix();
        Add(results, output, "ChatMix", "0x2C83 sub 0x0B", chatMix >= 0,
            chatMix >= 0 ? $"{chatMix} ({Describe(chatMix)})" : "no reply");

        if (chatMix >= 0)
        {
            byte probe = (byte)(chatMix == RaceSession.ChatMixBalanced ? 5 : RaceSession.ChatMixBalanced);
            bool applied = session.WriteChatMix(probe);
            int observed = session.ReadChatMix();

            bool restored = session.WriteChatMix((byte)chatMix);
            int back = session.ReadChatMix();

            Add(results, output, "ChatMix (write)", "0x2C82 sub 0x0B",
                applied && observed == probe && back == chatMix,
                $"{chatMix} -> {probe} ({(observed == probe ? "took" : $"read {observed}")}), restored to {back}");
        }

        // Sidetone is documented as sharing the ChatMix handler; sub 0x0C is the
        // next slot along, so it is the cheapest candidate to test for.
        int sidetone = session.ReadNamespace2C(0x0C);
        Add(results, output, "Sidetone (candidate)", "0x2C83 sub 0x0C", sidetone >= 0,
            sidetone >= 0 ? $"{sidetone} (0x{sidetone:X2}) - meaning unconfirmed" : "no reply");

        foreach (string feature in new[] { "EQ / PEQ preset", "Mic noise reduction", "Mic mute" })
            Add(results, output, feature, "unknown", false, "no command identified yet");
    }

    private static string Describe(int chatMix) => chatMix switch
    {
        0 => "all game",
        RaceSession.ChatMixBalanced => "balanced",
        RaceSession.ChatMixMax => "all chat",
        _ => "custom",
    };

    private static void Add(List<Result> results, TextWriter output,
        string area, string command, bool ok, string detail)
    {
        results.Add(new Result(area, command, ok ? "PASS" : "FAIL", detail));
        output.WriteLine($"     [{(ok ? "ok" : "--")}] {area,-24} {detail}");
    }
}
