using System.Globalization;
using System.Text;
using System.Text.Json;
using Maxwell.Cli;
using Maxwell.Race;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Diagnostics;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

Console.OutputEncoding = Encoding.UTF8;

bool verbose = args.Contains("--verbose") || args.Contains("-v");

Console.WriteLine("Maxwell RACE harness");
Console.WriteLine("====================");
Console.WriteLine();

// The device-model path is a different entry point entirely: it goes through the
// registry rather than opening a session by hand, so it runs before the direct HID
// route below — with --mock there may be no Audeze hardware attached at all.
if (args.Contains("--devices"))
{
    TimeSpan? watch = args.Contains("--watch") ? TimeSpan.FromSeconds(30) : null;
    return await DeviceInventory.RunAsync(args.Contains("--mock"), watch).ConfigureAwait(false);
}

// Tier zero on its own: everything the operating system knows with no driver involved.
if (args.Contains("--system")) return SystemInventory.Run(args.Contains("--raw"));

// Which class protocols this machine actually exposes. Asked before writing one.
if (args.Contains("--hid")) return HidSurvey.Run(!args.Contains("--all"));

// What is actually paired behind a Logitech receiver. Read-only.
if (args.Contains("--hidpp")) return HidPlusPlusProbe.Run();

var enumerator = new WindowsHidEnumerator();
IReadOnlyList<MaxwellInterface> all = enumerator.Enumerate();

if (all.Count == 0)
{
    Console.WriteLine("No Maxwell HID interface found. Connect the cable or the dongle.");
    return 1;
}

Console.WriteLine("[1] HID interfaces with VID 0x3329");
foreach (MaxwellInterface iface in all)
{
    string race = iface.IsRaceInterface ? "  <- RACE" : "";
    Console.WriteLine($"    PID 0x{iface.ProductId:X4}  usage {iface.UsagePage:X4}:{iface.Usage:X2}  " +
                      $"in {iface.InputReportLength,2}  out {iface.OutputReportLength,2}  {iface.Transport}{race}");
    if (verbose) Console.WriteLine($"        {iface.Path}");
}

if (args.Contains("--descriptor"))
{
    Console.WriteLine();
    Console.WriteLine("[descriptor] Declared reports per collection");
    Console.WriteLine();
    HidDescriptor.Dump(Console.Out);
    return 0;
}

IReadOnlyList<MaxwellInterface> candidates = enumerator.EnumerateRaceInterfaces();
if (ParsePid(args) is { } wanted)
    candidates = candidates.Where(i => i.ProductId == wanted).ToList();

if (candidates.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("No RACE interface among them.");
    return 1;
}

MaxwellInterface target = candidates[0];
Console.WriteLine();
Console.WriteLine($"[2] Opening {target.Transport} (PID 0x{target.ProductId:X4})");

var factory = new WindowsHidTransportFactory();
RaceSession session;
try
{
    session = new RaceSession(factory.Open(target));
}
catch (IOException ex)
{
    Console.WriteLine($"    {ex.Message}");
    return 1;
}

using (session)
{
    Console.WriteLine("    opened.");
    if (verbose) session.ReportReceived += r => Console.WriteLine($"        <- {Convert.ToHexString(r)}");
    Console.WriteLine();

    if (args.Contains("--probe")) return await DongleProbe.RunAsync(session, Console.Out).ConfigureAwait(false);
    if (args.Contains("--features")) return await ShowFeaturesAsync(session).ConfigureAwait(false);
    if (args.Contains("--scan")) return await ScanAsync(session, args).ConfigureAwait(false);
    if (args.Contains("--test")) return await RunTestsAsync(session).ConfigureAwait(false);
    if (args.Contains("--set")) return await SetAsync(session, args).ConfigureAwait(false);

    return await CheckAsync(session, target, args.Contains("--watch")).ConfigureAwait(false);
}

// ---------- modes ----------

static async Task<int> CheckAsync(RaceSession session, MaxwellInterface target, bool watch)
{
    Console.WriteLine("[3] Battery");
    int battery = await session.ReadBatteryAsync().ConfigureAwait(false);
    Console.WriteLine(battery >= 0 ? $"    {battery}%" : "    no reply.");

    Console.WriteLine();
    Console.WriteLine("[4] Channel gain");
    int master = await session.ReadValueAsync(MaxwellFeatures.AmpGain).ConfigureAwait(false);
    int left = await session.ReadValueAsync(MaxwellFeatures.LeftGain).ConfigureAwait(false);
    int right = await session.ReadValueAsync(MaxwellFeatures.RightGain).ConfigureAwait(false);
    Console.WriteLine(left >= 0 && right >= 0
        ? $"    master = {master}   L = {left}   R = {right}"
        : $"    no reply (master={master}, L={left}, R={right}).");

    Console.WriteLine();
    Console.WriteLine("[5] ChatMix");
    int chatMix = await session.ReadChatMixAsync().ConfigureAwait(false);
    Console.WriteLine(chatMix >= 0 ? $"    {chatMix}" : "    no reply.");

    Console.WriteLine();
    Console.WriteLine("[6] Firmware version");
    string? version = null;
    foreach (uint address in FirmwareAddresses.For(target.Transport))
    {
        version = await session.ReadVersionStringAsync(address).ConfigureAwait(false);
        if (version is not null) break;
    }
    Console.WriteLine(version is not null ? $"    v{version}" : "    not resolved.");

    bool ok = battery >= 0 || left >= 0 || chatMix >= 0;
    Console.WriteLine();
    Console.WriteLine(ok
        ? "RESULT: RACE link is up."
        : "RESULT: interface opened but nothing answered. Close Audeze HQ and retry.");

    if (!watch) return ok ? 0 : 2;

    Console.WriteLine();
    Console.WriteLine("Watching battery every 10s. Ctrl+C to stop.");
    while (true)
    {
        await Task.Delay(10_000).ConfigureAwait(false);
        int tick = await session.ReadBatteryAsync().ConfigureAwait(false);
        string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Console.WriteLine(tick >= 0 ? $"  {stamp}  {tick}%" : $"  {stamp}  no reply");
    }
}

static async Task<int> ShowFeaturesAsync(RaceSession session)
{
    Console.WriteLine("[features] Every mapped feature");
    Console.WriteLine();
    Console.WriteLine($"  {"Feature",-24} {"Access",-11} {"Command",-18} Value");
    Console.WriteLine($"  {new string('-', 24)} {new string('-', 11)} {new string('-', 18)} -----");

    foreach (MaxwellFeatures.Feature f in MaxwellFeatures.Readable)
    {
        int value = f.ReadCommand switch
        {
            RaceCommands.Battery => await session.ReadBatteryAsync().ConfigureAwait(false),
            RaceCommands.ChatMixRead => await session.ReadNamespace2CAsync(f.Sub).ConfigureAwait(false),
            _ => await session.ReadValueAsync(f.Sub).ConfigureAwait(false),
        };

        string command = f.ReadCommand == RaceCommands.Battery
            ? "0x0CD6"
            : $"0x{f.ReadCommand:X4} sub 0x{f.Sub:X2}";

        string shown = value >= 0 ? $"{value} (0x{value:X2})" : "no reply";
        Console.WriteLine($"  {f.Name,-24} {f.Access,-11} {command,-18} {shown}" +
                          (f.Notes.Length > 0 ? $"   [{f.Notes}]" : ""));
    }

    return 0;
}

static async Task<int> ScanAsync(RaceSession session, string[] args)
{
    Console.WriteLine("[scan] Read-only sweep of the 0x0901 namespace");
    Console.WriteLine();

    var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);

    for (int sub = 0x00; sub <= 0x3F; sub++)
    {
        int value = await session.ReadValueAsync((byte)sub).ConfigureAwait(false);
        if (value >= 0) snapshot[$"09.{sub:X2}"] = value.ToString(CultureInfo.InvariantCulture);
    }

    if (await session.ReadStatusAsync().ConfigureAwait(false) is { } status)
        snapshot["0910"] = Convert.ToHexString(status);

    if (ValueAfter(args, "--diff") is { } diffPath)
    {
        var baseline = JsonSerializer.Deserialize<SortedDictionary<string, string>>(
            await File.ReadAllTextAsync(diffPath).ConfigureAwait(false))!;

        int changes = 0;
        foreach (string key in baseline.Keys.Union(snapshot.Keys).Order(StringComparer.Ordinal))
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

    foreach ((string key, string value) in snapshot)
        Console.WriteLine($"  {key}  {value}");

    Console.WriteLine();
    Console.WriteLine($"  {snapshot.Count} entries.");

    if (ValueAfter(args, "--save") is { } savePath)
    {
        await File.WriteAllTextAsync(savePath,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        Console.WriteLine($"  snapshot saved to {savePath}");
    }

    return 0;
}

static async Task<int> RunTestsAsync(RaceSession session)
{
    Console.WriteLine("[test] Full function test — every write is reverted");
    Console.WriteLine();

    IReadOnlyList<TestSuite.Result> results = await TestSuite.RunAsync(session, Console.Out).ConfigureAwait(false);

    Console.WriteLine();
    Console.WriteLine($"  {"Area",-26} {"Status",-7} Detail");
    Console.WriteLine($"  {new string('-', 26)} {new string('-', 7)} ------");
    foreach (TestSuite.Result r in results)
        Console.WriteLine($"  {r.Area,-26} {r.Status,-7} {r.Detail}");

    int passed = results.Count(r => r.Status == "PASS");
    Console.WriteLine();
    Console.WriteLine($"  {passed}/{results.Count} passed.");

    return results.Any(r => r is { Area: "Safety", Status: "FAIL" }) ? 2 : 0;
}

static async Task<int> SetAsync(RaceSession session, string[] args)
{
    // Restricted to sub-commands whose meaning is confirmed, so a typo cannot land
    // in an undecoded register.
    byte[] allowed =
    [
        MaxwellFeatures.EarcupBalance, MaxwellFeatures.AmpGain, MaxwellFeatures.LeftGain,
        MaxwellFeatures.RightGain, MaxwellFeatures.IsNewFilm, MaxwellFeatures.NoiseReduction,
        MaxwellFeatures.SidetoneLevel, MaxwellFeatures.VpLevel, MaxwellFeatures.PeqIndex,
    ];

    if (ValueAfter(args, "--set") is not { } subText || ValueAfter(args, "--value") is not { } valueText)
    {
        Console.WriteLine("usage: --set <subHex> --value <decimal>");
        return 1;
    }

    byte sub = byte.Parse(subText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    byte value = byte.Parse(valueText, CultureInfo.InvariantCulture);

    if (Array.IndexOf(allowed, sub) < 0)
    {
        Console.WriteLine($"sub 0x{sub:X2} is not in the allow-list.");
        return 1;
    }

    int before = await session.ReadValueAsync(sub).ConfigureAwait(false);
    bool ok = await session.WriteValueAsync(sub, value).ConfigureAwait(false);
    int after = await session.ReadValueAsync(sub).ConfigureAwait(false);

    Console.WriteLine($"  sub 0x{sub:X2}: {before} -> requested {value} -> now {after} " +
                      $"({(ok && after == value ? "applied" : "did not stick")})");

    return ok && after == value ? 0 : 2;
}

// ---------- helpers ----------

static string? ValueAfter(string[] args, string flag)
{
    int index = Array.IndexOf(args, flag);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static ushort? ParsePid(string[] args)
{
    if (ValueAfter(args, "--pid") is not { } raw) return null;
    return ushort.TryParse(raw.TrimStart('0', 'x', 'X'), NumberStyles.HexNumber,
        CultureInfo.InvariantCulture, out ushort value) ? value : null;
}
