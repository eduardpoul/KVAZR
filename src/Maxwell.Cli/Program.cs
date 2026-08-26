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
    TimeSpan? watch = args.Contains("--watch")
        ? TimeSpan.FromSeconds(int.TryParse(ValueAfter(args, "--watch"), out int held) ? held : 30)
        : null;

    // Which device gets the page-open attention. It used to be whichever one could be
    // controlled, which quietly skipped the case the lane exists for: a headset that has
    // stopped answering is exactly the device somebody has open and is waiting on.
    return await DeviceInventory.RunAsync(
        args.Contains("--mock"),
        watch,
        args.Contains("--no-vendor"),
        ValueAfter(args, "--focus")).ConfigureAwait(false);
}

// Tier zero on its own: everything the operating system knows with no driver involved.
if (args.Contains("--system")) return SystemInventory.Run(args.Contains("--raw"));

// Which class protocols this machine actually exposes. Asked before writing one.
if (args.Contains("--hid")) return HidSurvey.Run(!args.Contains("--all"));

// One vendor's collections: what they declare and what they answer with nothing
// written. --vendor 0951 for the QuadCast.
if (args.Contains("--vendor"))
{
    ushort vid = ushort.TryParse(ValueAfter(args, "--vendor"), NumberStyles.HexNumber,
        CultureInfo.InvariantCulture, out ushort parsed) ? parsed : (ushort)0x0951;

    if (args.Contains("--watch"))
    {
        int seconds = int.TryParse(ValueAfter(args, "--watch"), out int held) ? held : 60;

        int everyMs = int.TryParse(ValueAfter(args, "--ms"), out int rate) ? Math.Max(1, rate) : 250;

        return VendorSurvey.Watch(Console.Out, vid, seconds, everyMs, args.Contains("--quiet"));
    }

    // One frame of colour, then silence, to find out whether a frame stays put.
    if (args.Contains("--rgb"))
    {
        string colour = ValueAfter(args, "--rgb") ?? "00FF00";

        if (colour.Length != 6
            || !byte.TryParse(colour.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(colour.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(colour.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            Console.WriteLine("usage: --vendor 0951 --rgb RRGGBB");
            return 1;
        }

        Console.WriteLine("Painting both light bars once. Two packets, both of them shapes");
        Console.WriteLine("NGENUITY was watched sending. Unplugging the cable undoes it.");
        Console.WriteLine();

        return VendorSurvey.Paint(Console.Out, vid, r, g, b);
    }

    // The one command this harness will send, and it is a replay of what NGENUITY was
    // watched sending: ask the microphone what its polar selector is set to.
    if (args.Contains("--ask"))
    {
        Console.WriteLine("Asking the microphone for its state. This writes one command — the");
        Console.WriteLine("same bytes NGENUITY sends at start-up — and reads the answer back.");
        Console.WriteLine();

        return VendorSurvey.Ask(Console.Out, vid, [0x04, 0x58, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01]);
    }

    return VendorSurvey.Dump(Console.Out, vid);
}

// The polar selector, read through the contributor the application uses. The device
// inventory cannot show this: it prints one snapshot and the row is empty until the
// dial moves.
if (args.Contains("--polar"))
{
    return await PolarProbe.RunAsync(
        int.TryParse(ValueAfter(args, "--polar"), out int watched) ? watched : 40).ConfigureAwait(false);
}

// The guided dig: a script of things to do to the microphone, with every channel
// listened to and the lighting pipe polled, so each report lands under the step that
// caused it. --patterns names the selector's icons; --ngenuity watches the maker's own
// software drive the device. Passive throughout.
if (args.Contains("--quadcast"))
{
    return QuadCastProbe.Run(
        ValueAfter(args, "--log"), args.Contains("--patterns"), args.Contains("--ngenuity"));
}

// What is actually paired behind a Logitech receiver. Read-only.
if (args.Contains("--pointers")) return PointerProbe.Run();
if (args.Contains("--setdpi"))
{
    int resolution = int.TryParse(ValueAfter(args, "--setdpi"), out int asked) ? asked : 1600;
    return DpiWriteProbe.Run(resolution, args.Contains("--keep"));
}
if (args.Contains("--pairings")) return PairingProbe.Run();
if (args.Contains("--pointer")) return PointerSettingsProbe.Run();
if (args.Contains("--props")) return PropertyProbe.Run(ValueAfter(args, "--props"));
if (args.Contains("--tester"))
{
    return TesterProbe.Run(int.TryParse(ValueAfter(args, "--tester"), out int watching) ? watching : 20);
}
if (args.Contains("--hidpp")) return HidPlusPlusProbe.Run();
if (args.Contains("--xinput")) return XInputProbe.Run();
if (args.Contains("--audio")) return AudioProbe.Run();
if (args.Contains("--micmute")) return await MuteProbe.RunAsync(args.Contains("--leave")).ConfigureAwait(false);
if (args.Contains("--meter"))
{
    int seconds = int.TryParse(ValueAfter(args, "--meter"), out int held) ? held : 6;
    return await MeterProbe.RunAsync(seconds).ConfigureAwait(false);
}
if (args.Contains("--blindspot"))
{
    int seconds = int.TryParse(ValueAfter(args, "--blindspot"), out int span) ? span : 12;
    return await BlindSpotProbe.RunAsync(seconds, ValueAfter(args, "--name")).ConfigureAwait(false);
}
if (args.Contains("--witness"))
{
    int seconds = int.TryParse(ValueAfter(args, "--witness"), out int watched) ? watched : 30;
    return await WitnessProbe.RunAsync(seconds, ValueAfter(args, "--name")).ConfigureAwait(false);
}
if (args.Contains("--desk")) return DeskProbe.Run(args);
if (args.Contains("--timing")) return await TimingProbe.RunAsync().ConfigureAwait(false);
if (args.Contains("--devchange")) return DeviceChangeProbe.Run(TimeSpan.FromSeconds(20));
if (args.Contains("--charge")) return await ChargeProbe.RunAsync(4).ConfigureAwait(false);
if (args.Contains("--autooff")) return await AutoOffProbe.RunAsync().ConfigureAwait(false);
if (args.Contains("--eqwrite")) return await EqWriteProbe.RunAsync().ConfigureAwait(false);
// Whether a headset switching on announces itself anywhere at all. Decides whether the
// panel can subscribe or has to keep asking on a timer.
if (args.Contains("--wake"))
{
    int window = int.TryParse(ValueAfter(args, "--wake"), out int watching) ? watching : 60;
    return await WakeProbe.RunAsync(window).ConfigureAwait(false);
}

if (args.Contains("--listen"))
{
    // How long to hold the door open. Twenty seconds was hard-coded and it is not enough
    // when the person who has to move the boom is reading about it in another window.
    int seconds = int.TryParse(ValueAfter(args, "--listen"), out int given) ? given : 20;
    return await ListenProbe.RunAsync(seconds).ConfigureAwait(false);
}
if (args.Contains("--eqcompare")) return EqCompareProbe.Run();
if (args.Contains("--eqpush"))
{
    return await EqPushProbe.RunAsync(
        args.Contains("--dry"), args.Contains("--force"), args.Contains("--mine"),
        ValueAfter(args, "--curve") ?? "flat")
        .ConfigureAwait(false);
}

if (args.Contains("--eq")) return await EqProbe.RunAsync().ConfigureAwait(false);
if (args.Contains("--airpods")) return AirPodsProbe.Run();
if (args.Contains("--live"))
{
    return LiveProbe.Run(int.TryParse(ValueAfter(args, "--live"), out int held) ? held : 30);
}

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
    Console.WriteLine("Watching every 5s. Ctrl+C to stop.");
    Console.WriteLine();
    Console.WriteLine("  time      battery  gain   <- the gain is the reading the application");
    Console.WriteLine("                            takes 'the headset is awake' from, so it is");
    Console.WriteLine("                            the one worth watching for a stray answer.");
    Console.WriteLine();

    while (true)
    {
        await Task.Delay(5_000).ConfigureAwait(false);

        int tick = await session.ReadBatteryAsync().ConfigureAwait(false);
        int gain = await session.ReadValueAsync(MaxwellFeatures.AmpGain).ConfigureAwait(false);
        string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        Console.WriteLine(
            $"  {stamp}  {(tick >= 0 ? tick + "%" : "—"),7}  {(gain >= 0 ? gain.ToString(CultureInfo.InvariantCulture) : "—"),5}");
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
    // Which read namespace, and how far up. Both default to what the sweep has always
    // done — 0x0901, sub-commands 0x00 to 0x3F — because that is the range the settings
    // live in; the arguments are for looking outside it, which is how you find out that
    // something is not there rather than assuming it.
    ushort ns = ParseHex(ValueAfter(args, "--ns")) is { } n ? (ushort)n : RaceCommands.Read;
    int from = ParseHex(ValueAfter(args, "--from")) ?? 0x00;
    int to = ParseHex(ValueAfter(args, "--to")) ?? 0x3F;

    // A sweep is not a value somebody is waiting on: most sub-commands do not answer, and
    // the patient read spends a second and a half on each of those.
    bool fast = args.Contains("--fast") || to - from > 0x40;
    int attempts = fast ? 1 : 4;
    int window = fast ? 120 : 400;

    Console.WriteLine($"[scan] Read-only sweep of 0x{ns:X4}, sub 0x{from:X2}..0x{to:X2}" +
                      (fast ? "  (one attempt, 120 ms)" : ""));
    Console.WriteLine();

    var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);

    for (int sub = from; sub <= to; sub++)
    {
        // The 0x2C namespace answers with the sub-command in a different byte, so it
        // needs its own reader. Sweeping it through the general one found nothing and
        // would have been read as "the headset has nothing there", which is the worst
        // kind of wrong answer a probe can give.
        int value = ns == RaceCommands.ChatMixRead
            ? await session.ReadNamespace2CAsync((byte)sub, attempts, window).ConfigureAwait(false)
            : await session.ReadValueAsync(ns, (byte)sub, attempts, window).ConfigureAwait(false);

        if (value >= 0) snapshot[$"{ns >> 8:X2}.{sub:X2}"] = value.ToString(CultureInfo.InvariantCulture);
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

static int? ParseHex(string? text) =>
    text is { Length: > 0 }
        && int.TryParse(text.Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
        ? value
        : null;

static ushort? ParsePid(string[] args)
{
    if (ValueAfter(args, "--pid") is not { } raw) return null;
    return ushort.TryParse(raw.TrimStart('0', 'x', 'X'), NumberStyles.HexNumber,
        CultureInfo.InvariantCulture, out ushort value) ? value : null;
}
