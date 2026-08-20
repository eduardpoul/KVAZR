using System.Text;
using Maxwell.Race;

Console.OutputEncoding = Encoding.UTF8;

bool verbose = args.Contains("--verbose") || args.Contains("-v");
bool watch = args.Contains("--watch") || args.Contains("-w");
ushort? wantedPid = ParsePid(args);

Console.WriteLine("Maxwell RACE connectivity check");
Console.WriteLine("===============================");
Console.WriteLine();

// ---------- 1. enumerate ----------

Console.WriteLine("[1] HID interfaces with VID 0x3329");

List<MaxwellInterface> all = MaxwellHid.Enumerate();
if (all.Count == 0)
{
    Console.WriteLine("    none found - the dongle is unplugged and no cable is connected.");
    return 1;
}

foreach (MaxwellInterface iface in all)
{
    string race = iface.IsRaceInterface ? " <- RACE" : "";
    Console.WriteLine($"    PID 0x{iface.ProductId:X4}  usage {iface.UsagePage:X4}:{iface.Usage:X2}  " +
                      $"in {iface.InputReportLength,2}  out {iface.OutputReportLength,2}  " +
                      $"{iface.Transport}{race}");
    if (verbose) Console.WriteLine($"        {iface.Path}");
}

List<MaxwellInterface> candidates = MaxwellHid.EnumerateRaceInterfaces();
if (wantedPid is { } pid)
    candidates = candidates.Where(i => i.ProductId == pid).ToList();

if (candidates.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("    no RACE interface among them.");
    return 1;
}

MaxwellInterface target = candidates[0];
Console.WriteLine();

if (args.Contains("--features"))
{
    Console.WriteLine($"[features] Reading every mapped feature on {target.Transport}");
    Console.WriteLine();

    using RaceSession? reader = OpenOrExit(target);
    if (reader is null) return 1;

    Console.WriteLine($"  {"Feature",-24} {"Access",-11} {"Command",-18} Value");
    Console.WriteLine($"  {new string('-', 24)} {new string('-', 11)} {new string('-', 18)} -----");

    foreach (MaxwellFeatures.Feature f in MaxwellFeatures.Readable)
    {
        int value = f.ReadCommand switch
        {
            RaceSession.CmdBattery => reader.ReadBattery(),
            RaceSession.CmdChatMixRead => reader.ReadNamespace2C(f.Sub),
            _ => reader.ReadValue(f.Sub),
        };

        string command = f.ReadCommand == RaceSession.CmdBattery
            ? "0x0CD6"
            : $"0x{f.ReadCommand:X4} sub 0x{f.Sub:X2}";

        string shown = value >= 0 ? $"{value} (0x{value:X2})" : "no reply";
        Console.WriteLine($"  {f.Name,-24} {f.Access,-11} {command,-18} {shown}" +
                          (f.Notes.Length > 0 ? $"   [{f.Notes}]" : ""));
    }

    return 0;
}

if (args.Contains("--monitor"))
{
    int seconds = int.TryParse(ValueAfter(args, "--monitor"), out int s) ? s : 180;

    Console.WriteLine($"[monitor] Watching the 0x2C83 namespace for {seconds}s.");
    Console.WriteLine("          Open Audeze HQ and toggle ONE feature at a time.");
    Console.WriteLine("          Every change is timestamped so each one can be attributed.");
    Console.WriteLine();

    using RaceSession? watcher = OpenOrExit(target);
    if (watcher is null) return 1;

    byte[] watched = { 0x00, 0x01, 0x06, 0x07, 0x09, 0x0B, 0x0C, 0x12 };
    var last = new Dictionary<byte, int>();

    foreach (byte sub in watched)
        last[sub] = watcher.ReadNamespace2C(sub, attempts: 2, windowMs: 120);

    Console.WriteLine("  start: " + string.Join("  ", watched.Select(x => $"0x{x:X2}={last[x]}")));
    Console.WriteLine();

    long stop = Environment.TickCount64 + seconds * 1000L;
    while (Environment.TickCount64 < stop)
    {
        foreach (byte sub in watched)
        {
            int now = watcher.ReadNamespace2C(sub, attempts: 1, windowMs: 110);
            if (now < 0 || now == last[sub]) continue;

            Console.WriteLine($"  {DateTime.Now:HH:mm:ss}  0x2C83 sub 0x{sub:X2}: " +
                              $"{last[sub]} -> {now}");
            last[sub] = now;
        }
    }

    Console.WriteLine();
    Console.WriteLine("  final: " + string.Join("  ", watched.Select(x => $"0x{x:X2}={last[x]}")));
    return 0;
}

if (args.Contains("--hunt"))
{
    Console.WriteLine($"[hunt] Sweeping the 0x2C83 read namespace on {target.Transport}");
    Console.WriteLine("       0x2C83 is a confirmed read command; nothing here writes.");
    Console.WriteLine();

    using RaceSession? hunter = OpenOrExit(target);
    if (hunter is null) return 1;

    int found = 0;
    for (int sub = 0x00; sub <= 0xFF; sub++)
    {
        int value = hunter.ReadNamespace2C((byte)sub, attempts: 2, windowMs: 120);
        if (value < 0) continue;

        found++;
        string note = sub switch
        {
            0x0B => "  <- ChatMix (confirmed)",
            _ => "",
        };
        Console.WriteLine($"  0x2C83 sub 0x{sub:X2}  value {value,3} (0x{value:X2}){note}");
    }

    Console.WriteLine();
    Console.WriteLine($"  {found} sub-command(s) answered.");
    return 0;
}

if (args.Contains("--set"))
{
    // Deliberately limited to sub-commands whose meaning is known, so a typo cannot
    // write into an undecoded register.
    byte[] allowed = { 0x25, 0x28, 0x29, 0x2A, 0x2F, 0x31 };

    string? subText = ValueAfter(args, "--set");
    string? valueText = ValueAfter(args, "--value");
    if (subText is null || valueText is null)
    {
        Console.WriteLine("usage: --set <subHex> --value <decimal>");
        return 1;
    }

    byte setSub = Convert.ToByte(subText, 16);
    byte setValue = byte.Parse(valueText);

    if (Array.IndexOf(allowed, setSub) < 0)
    {
        Console.WriteLine($"sub 0x{setSub:X2} is not in the allow-list " +
                          $"({string.Join(", ", allowed.Select(s => $"0x{s:X2}"))}).");
        return 1;
    }

    using RaceSession? writer = OpenOrExit(target);
    if (writer is null) return 1;

    int before = writer.ReadValue(setSub);
    bool ok = writer.WriteValue(setSub, setValue);
    int after = writer.ReadValue(setSub);

    Console.WriteLine($"  sub 0x{setSub:X2}: {before} -> requested {setValue} -> now {after} " +
                      $"({(ok && after == setValue ? "applied" : "did not stick")})");
    return ok && after == setValue ? 0 : 2;
}

if (args.Contains("--test"))
{
    Console.WriteLine($"[test] Full function test on {target.Transport} (PID 0x{target.ProductId:X4})");
    Console.WriteLine("       Every write is reverted and the end state is diffed against the start.");
    Console.WriteLine();

    using RaceSession? suite = OpenOrExit(target);
    if (suite is null) return 1;

    List<Maxwell.Cli.TestSuite.Result> results = Maxwell.Cli.TestSuite.Run(suite, Console.Out);

    Console.WriteLine();
    Console.WriteLine("  RESULTS");
    Console.WriteLine($"  {"Area",-26} {"Command",-18} {"Status",-7} Detail");
    Console.WriteLine($"  {new string('-', 26)} {new string('-', 18)} {new string('-', 7)} ------");
    foreach (Maxwell.Cli.TestSuite.Result r in results)
        Console.WriteLine($"  {r.Area,-26} {r.Command,-18} {r.Status,-7} {r.Detail}");

    int passed = results.Count(r => r.Status == "PASS");
    Console.WriteLine();
    Console.WriteLine($"  {passed}/{results.Count} passed.");
    return results.Any(r => r is { Area: "Safety", Status: "FAIL" }) ? 2 : 0;
}

if (args.Contains("--scan"))
{
    Console.WriteLine($"[scan] Read-only sweep on {target.Transport} (PID 0x{target.ProductId:X4})");
    Console.WriteLine("       cmd 0x0901 is the READ namespace; nothing here writes to the headset.");
    Console.WriteLine();

    using RaceSession? scan = OpenOrExit(target);
    if (scan is null) return 1;

    var known = new Dictionary<byte, string>
    {
        [0x20] = "source preference (NVDM 0xF666)",
        [0x24] = "audio reset",
        [0x25] = "balance slider (-6..+6 around 0x80)",
        [0x27] = "volume",
        [0x28] = "master gain",
        [0x29] = "left gain",
        [0x2A] = "right gain",
        [0x2C] = "audio config",
        [0x2E] = "audio config",
        [0x2F] = "audio source state (NVDM 0xF702)",
    };

    var snapshot = new SortedDictionary<string, string>();

    for (byte sub = 0x00; sub <= 0x3F; sub++)
    {
        byte[] body = { 0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x09, sub };
        RacePacket? hit = scan.Probe(body, 140).FirstOrDefault(p =>
            p.Command == RaceSession.CmdRead && p.Sub == sub && p.Payload.Length >= 2);

        if (hit is not null) snapshot[$"09.{sub:X2}"] = hit.Hex;
    }

    RacePacket? status = scan.Probe(new byte[] { 0x80, 0x05, 0x5A, 0x03, 0x00, 0x10, 0x09 }, 500)
        .FirstOrDefault(p => p.Command == 0x0910);
    if (status is not null) snapshot["0910"] = status.Hex;

    string? diffPath = ValueAfter(args, "--diff");
    string? savePath = ValueAfter(args, "--save");

    if (diffPath is not null)
    {
        // Toggle a feature between two snapshots and whatever moved is the
        // sub-command behind it. This is how the undocumented half gets mapped.
        var baseline = System.Text.Json.JsonSerializer
            .Deserialize<SortedDictionary<string, string>>(File.ReadAllText(diffPath))!;

        var keys = baseline.Keys.Union(snapshot.Keys).OrderBy(k => k);
        int changes = 0;

        foreach (string key in keys)
        {
            baseline.TryGetValue(key, out string? before);
            snapshot.TryGetValue(key, out string? after);
            if (before == after) continue;

            changes++;
            Console.WriteLine($"  CHANGED {key}: {before ?? "(absent)"} -> {after ?? "(absent)"}");
        }

        Console.WriteLine();
        Console.WriteLine(changes == 0 ? "  nothing changed." : $"  {changes} value(s) changed.");
        return 0;
    }

    foreach ((string key, string hex) in snapshot)
    {
        if (!key.StartsWith("09.")) continue;
        byte sub = Convert.ToByte(key[3..], 16);
        byte value = Convert.FromHexString(hex)[^1];
        string label = known.TryGetValue(sub, out string? name) ? name : "undocumented";
        Console.WriteLine($"  sub 0x{sub:X2}  payload {hex,-12} value {value,3} (0x{value:X2})  {label}");
    }

    Console.WriteLine();
    Console.WriteLine($"  {snapshot.Count(k => k.Key.StartsWith("09."))} of 64 sub-commands answered.");
    if (snapshot.TryGetValue("0910", out string? statusHex))
        Console.WriteLine($"  cmd 0x0910 status struct: {statusHex}");

    if (savePath is not null)
    {
        File.WriteAllText(savePath, System.Text.Json.JsonSerializer.Serialize(snapshot,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
        Console.WriteLine($"  snapshot saved to {savePath}");
    }

    return 0;
}

if (args.Contains("--listen"))
{
    Console.WriteLine($"[listen] Passive capture on {target.Transport} (PID 0x{target.ProductId:X4}), 12s.");
    Console.WriteLine("         Press the power / volume / mute buttons on the headset now.");
    Console.WriteLine();
    RaceListener.Listen(target, Console.Out, 12);
    return 0;
}

if (args.Contains("--descriptor"))
{
    Console.WriteLine("[descriptor] Declared reports per collection");
    Console.WriteLine();
    HidDescriptor.Dump(Console.Out);
    return 0;
}

if (args.Contains("--sweep"))
{
    Console.WriteLine($"[sweep] Transport matrix on {target.Transport} (PID 0x{target.ProductId:X4})");
    Console.WriteLine();
    RaceSweep.Run(target, Console.Out);
    return 0;
}

if (args.Contains("--diag"))
{
    Console.WriteLine($"[diag] Probing {target.Transport} (PID 0x{target.ProductId:X4})");
    Console.WriteLine();
    Console.WriteLine("  battery request 06 07 80 05 5A 03 00 D6 0C");
    RaceDiagnostics.Probe(target, Console.Out, new byte[] { 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C });
    Console.WriteLine();
    Console.WriteLine("  right-gain request 06 08 80 05 5A 04 00 01 09 2A");
    RaceDiagnostics.Probe(target, Console.Out, new byte[] { 0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x09, 0x2A });
    Console.WriteLine();
    Console.WriteLine("  interrupt-endpoint cross-check");
    RaceDiagnostics.ProbeInterrupt(target, Console.Out, new byte[] { 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C });
    return 0;
}

Console.WriteLine($"[2] Opening {target.Transport} (PID 0x{target.ProductId:X4})");

using RaceSession? session = OpenOrExit(target);
if (session is null) return 1;

Console.WriteLine("    opened.");
if (verbose)
    session.ReportReceived += report => Console.WriteLine($"        <- {RaceSession.ToHex(report)}");

Console.WriteLine();

// ---------- 3. battery ----------

Console.WriteLine("[3] Battery (cmd 0x0CD6)");
int battery = session.ReadBattery();
Console.WriteLine(battery >= 0
    ? $"    {battery}%  (0x{battery:X2})"
    : "    no reply.");

Console.WriteLine();

// ---------- 4. gains ----------

Console.WriteLine("[4] Channel gain (cmd 0x0901)");
int master = session.ReadValue(RaceSession.SubGainMaster);
int left = session.ReadValue(RaceSession.SubGainLeft);
int right = session.ReadValue(RaceSession.SubGainRight);
Console.WriteLine(left >= 0 && right >= 0
    ? $"    master = {master}   L = {left}   R = {right}"
    : $"    no reply (master={master}, L={left}, R={right}).");

Console.WriteLine();

// ---------- 5. source state ----------

Console.WriteLine("[5] Audio source state (NVDM 0xF702, cmd 0x0901 sub 0x2F)");
int source = session.ReadValue(RaceSession.SubAudioSource);
Console.WriteLine(source >= 0
    ? $"    state = 0x{source:X2} ({(source == 0x0A ? "USB-C profile" : "wireless / dongle profile")})"
    : "    no reply.");

Console.WriteLine();

// ---------- 6. firmware ----------

Console.WriteLine("[6] Firmware version (cmd 0x1680 flash read)");
string? version = null;
foreach (uint address in FirmwareAddresses.For(target.Transport))
{
    version = session.ReadVersionString(address);
    if (version is not null) break;
}
Console.WriteLine(version is not null
    ? $"    v{version}  ({(target.IsDongle ? "dongle" : "headset")})"
    : "    not resolved.");

// ---------- 7. verdict ----------

Console.WriteLine();
bool raceWorks = battery >= 0 || left >= 0 || source >= 0;
Console.WriteLine(raceWorks
    ? "RESULT: RACE link is up - the device answers commands."
    : "RESULT: interface opened but nothing answered. Close Audeze HQ and retry.");

if (!watch) return raceWorks ? 0 : 2;

// ---------- watch loop ----------

Console.WriteLine();
Console.WriteLine("Watching battery every 10s. Ctrl+C to stop.");
while (true)
{
    Thread.Sleep(10_000);
    int tick = session.ReadBattery();
    string stamp = DateTime.Now.ToString("HH:mm:ss");
    Console.WriteLine(tick >= 0 ? $"  {stamp}  {tick}%" : $"  {stamp}  no reply");
}

static string? ValueAfter(string[] args, string flag)
{
    int index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static ushort? ParsePid(string[] args)
{
    int index = Array.IndexOf(args, "--pid");
    if (index < 0 || index + 1 >= args.Length) return null;
    string raw = args[index + 1].TrimStart('0', 'x', 'X');
    return ushort.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out ushort value)
        ? value
        : null;
}

static RaceSession? OpenOrExit(MaxwellInterface iface)
{
    try
    {
        return RaceSession.Open(iface);
    }
    catch (IOException ex)
    {
        Console.WriteLine($"    {ex.Message}");
        return null;
    }
}
