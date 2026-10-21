using Kvazr.Race;
using Kvazr.Race.Protocol;

namespace Kvazr.Cli;

/// <summary>
/// Exercises every known function and puts each setting back the way it was.
/// Each write is verified by reading it back, then reverted and verified again, and a
/// final snapshot is diffed against the opening one to prove nothing drifted.
/// Undocumented write registers are deliberately untouched: an unknown register cannot
/// be restored if it turns out to be destructive.
/// </summary>
internal static class TestSuite
{
    internal sealed record Result(string Area, string Status, string Detail);

    public static async Task<IReadOnlyList<Result>> RunAsync(RaceSession session, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(output);

        var results = new List<Result>();

        output.WriteLine("  capturing baseline...");
        Dictionary<byte, int> baseline = await SnapshotAsync(session).ConfigureAwait(false);
        output.WriteLine($"  baseline: {baseline.Count} readable sub-commands");
        output.WriteLine();

        await RunReadTestsAsync(session, results, output).ConfigureAwait(false);
        await RunWriteTestsAsync(session, results, output).ConfigureAwait(false);

        output.WriteLine();
        output.WriteLine("  verifying every setting is back where it started...");
        Dictionary<byte, int> final = await SnapshotAsync(session).ConfigureAwait(false);

        var drift = new List<string>();
        foreach (byte sub in baseline.Keys.Union(final.Keys).Order())
        {
            baseline.TryGetValue(sub, out int before);
            final.TryGetValue(sub, out int after);
            if (before != after) drift.Add($"0x{sub:X2}: {before} -> {after}");
        }

        if (drift.Count == 0)
        {
            output.WriteLine("  RESTORED: final snapshot matches the baseline exactly.");
            results.Add(new Result("Safety", "PASS", "all values back to baseline"));
        }
        else
        {
            output.WriteLine($"  DRIFT on {drift.Count} value(s): {string.Join(", ", drift)}");
            results.Add(new Result("Safety", "FAIL", string.Join("; ", drift)));
        }

        return results;
    }

    private static async Task<Dictionary<byte, int>> SnapshotAsync(RaceSession session)
    {
        var values = new Dictionary<byte, int>();
        for (int sub = 0x00; sub <= 0x3F; sub++)
        {
            int value = await session.ReadValueAsync((byte)sub).ConfigureAwait(false);
            if (value >= 0) values[(byte)sub] = value;
        }
        return values;
    }

    private static async Task RunReadTestsAsync(RaceSession session, List<Result> results, TextWriter output)
    {
        output.WriteLine("  -- reads --");

        int battery = await session.ReadBatteryAsync().ConfigureAwait(false);
        Add(results, output, "Battery", battery >= 0, battery >= 0 ? $"{battery}%" : "no reply");

        foreach ((byte sub, string name) in new (byte, string)[]
        {
            (MaxwellFeatures.AmpGain, "Amp gain"),
            (MaxwellFeatures.LeftGain, "Left gain"),
            (MaxwellFeatures.RightGain, "Right gain"),
            (MaxwellFeatures.EarcupBalance, "Earcup balance"),
            (MaxwellFeatures.NoiseReduction, "Noise reduction"),
            (MaxwellFeatures.SidetoneLevel, "Sidetone level"),
            (MaxwellFeatures.MaxSidetoneLevel, "Max sidetone level"),
            (MaxwellFeatures.VpLevel, "Voice prompt level"),
            (MaxwellFeatures.PeqIndex, "EQ preset index"),
            (MaxwellFeatures.MicMute, "Mic mute"),
            (MaxwellFeatures.MicLevel, "Mic level"),
            (MaxwellFeatures.IsNewFilm, "Source flag"),
        })
        {
            int value = await session.ReadValueAsync(sub).ConfigureAwait(false);
            Add(results, output, name, value >= 0, value >= 0 ? $"{value} (0x{value:X2})" : "no reply");
        }

        int chatMix = await session.ReadChatMixAsync().ConfigureAwait(false);
        Add(results, output, "ChatMix", chatMix >= 0, chatMix >= 0 ? chatMix.ToString() : "no reply");

        int sidetoneState = await session.ReadNamespace2CAsync(MaxwellFeatures.SidetoneState).ConfigureAwait(false);
        Add(results, output, "Sidetone on/off", sidetoneState >= 0,
            sidetoneState >= 0 ? sidetoneState.ToString() : "no reply");

        int autoOff = await session.ReadNamespace2CAsync(MaxwellFeatures.AutoPowerOff).ConfigureAwait(false);
        Add(results, output, "Auto power off", autoOff >= 0, autoOff >= 0 ? autoOff.ToString() : "no reply");

        byte[]? status = await session.ReadStatusAsync().ConfigureAwait(false);
        Add(results, output, "Status struct", status is not null,
            status is not null ? Convert.ToHexString(status) : "no reply");
    }

    private static async Task RunWriteTestsAsync(RaceSession session, List<Result> results, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("  -- writes (each one reverted immediately) --");

        await TestWriteAsync(session, results, output, MaxwellFeatures.AmpGain, "Amp gain", StepDown)
            .ConfigureAwait(false);
        await TestWriteAsync(session, results, output, MaxwellFeatures.LeftGain, "Left gain", StepDown)
            .ConfigureAwait(false);
        await TestWriteAsync(session, results, output, MaxwellFeatures.RightGain, "Right gain", StepDown)
            .ConfigureAwait(false);
        await TestWriteAsync(session, results, output, MaxwellFeatures.EarcupBalance, "Earcup balance",
            current => (byte)(current == 0x80 ? 0x7F : 0x80)).ConfigureAwait(false);

        await TestChatMixAsync(session, results, output).ConfigureAwait(false);
    }

    /// <summary>Step one away from the current gain, staying clear of the reserved codes.</summary>
    private static byte StepDown(int current)
    {
        int candidate = current > 0 ? current - 1 : current + 1;
        while (RaceCommands.IsReservedGain(candidate) || candidate is < 0 or > RaceCommands.MaxGain)
            candidate--;
        return (byte)candidate;
    }

    private static async Task TestWriteAsync(RaceSession session, List<Result> results, TextWriter output,
        byte sub, string name, Func<int, byte> pickProbe)
    {
        int original = await session.ReadValueAsync(sub).ConfigureAwait(false);
        if (original < 0)
        {
            Add(results, output, name + " (write)", false, "cannot read original, skipped");
            return;
        }

        byte probe = pickProbe(original);

        bool applied = await session.WriteValueAsync(sub, probe).ConfigureAwait(false);
        int observed = await session.ReadValueAsync(sub).ConfigureAwait(false);

        bool restored = await session.WriteValueAsync(sub, (byte)original).ConfigureAwait(false);
        int back = await session.ReadValueAsync(sub).ConfigureAwait(false);

        Add(results, output, name + " (write)", applied && observed == probe && back == original,
            $"{original} -> {probe} ({(observed == probe ? "took" : $"read {observed}")}), " +
            $"restored to {back}{(restored && back == original ? "" : " FAILED")}");
    }

    private static async Task TestChatMixAsync(RaceSession session, List<Result> results, TextWriter output)
    {
        int original = await session.ReadChatMixAsync().ConfigureAwait(false);
        if (original < 0)
        {
            Add(results, output, "ChatMix (write)", false, "cannot read original, skipped");
            return;
        }

        byte probe = (byte)(original == RaceCommands.ChatMixBalanced ? 5 : RaceCommands.ChatMixBalanced);

        bool applied = await session.WriteChatMixAsync(probe).ConfigureAwait(false);
        int observed = await session.ReadChatMixAsync().ConfigureAwait(false);

        await session.WriteChatMixAsync((byte)original).ConfigureAwait(false);
        int back = await session.ReadChatMixAsync().ConfigureAwait(false);

        Add(results, output, "ChatMix (write)", applied && observed == probe && back == original,
            $"{original} -> {probe} ({(observed == probe ? "took" : $"read {observed}")}), restored to {back}");
    }

    private static void Add(List<Result> results, TextWriter output, string area, bool ok, string detail)
    {
        results.Add(new Result(area, ok ? "PASS" : "FAIL", detail));
        output.WriteLine($"     [{(ok ? "ok" : "--")}] {area,-24} {detail}");
    }
}
