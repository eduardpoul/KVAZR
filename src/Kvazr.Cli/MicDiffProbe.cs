using System.Runtime.Versioning;

using Kvazr.Race;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Every register the headset will answer, before and after a switch is moved.
///
/// The mute register is the obvious place for a microphone switch to show up, and on this
/// headset it does not: 0x22 answers 0xFF and stays there through twenty seconds of
/// flipping, while the capture stream carries speech the whole time. That rules out one
/// byte, not the switch — so this stops guessing which register to watch and reads all of
/// them, twice, with the flip in between.
///
/// A dropped reply is never a difference. Single reads over the dongle go missing often
/// enough that a sweep of sixty-odd registers will lose one or two, and "no reply" on
/// either side is excluded rather than reported: the whole value of a diff is that
/// everything it prints is real. Anything that does differ is then re-read three times
/// with full patience, so a flapping register is told from a moved one.
///
/// Read only, both namespaces and the status struct. Nothing here writes.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MicDiffProbe
{
    /// <summary>The 0x0901 namespace. Known features run to 0x35; the rest is what we are here for.</summary>
    private const byte LastFeature = 0x3F;

    /// <summary>The 0x2C83 namespace: sidetone, auto power-off, chat mix.</summary>
    private const byte LastChat = 0x1F;

    /// <summary>Two attempts, briefly. A sweep waiting four times over on every absent register takes minutes.</summary>
    private const int Attempts = 2;
    private const int WindowMs = 200;

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var factory = new WindowsHidTransportFactory();

        IReadOnlyList<MaxwellInterface> found = enumerator.EnumerateRaceInterfaces();

        if (found.Count == 0)
        {
            Console.WriteLine("Интерфейс Audeze RACE не найден — наушники выключены или не на связи.");
            return 1;
        }

        MaxwellInterface target = found[0];
        using var session = new RaceSession(factory.Open(target));

        Console.WriteLine($"наушники: PID 0x{target.ProductId:X4}  {target.Transport}");
        Console.WriteLine();
        Console.WriteLine("Снимаю два слепка подряд, ничего не трогая. Это секунд тридцать.");

        Dictionary<string, string> first = await SweepAsync(session, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  снято: {first.Count(p => p.Value != "—")} ответивших из {first.Count}");

        Dictionary<string, string> before = await SweepAsync(session, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  снято: {before.Count(p => p.Value != "—")} ответивших из {before.Count}");

        // Two sweeps with nothing touched, so that a register which moves on its own is
        // struck out before the switch is ever flipped. Without this the diff reported
        // 0x0901 06 and 0x2C83 00 as having moved during a run where nothing moved — they
        // simply carry live values, and a probe that cannot tell that from an answer is
        // worse than none.
        HashSet<string> restless =
        [
            .. first.Keys.Where(key => Both(first, before, key) && first[key] != before[key]),
        ];

        if (restless.Count > 0)
        {
            Console.WriteLine($"  живут своей жизнью, из сравнения исключены: {string.Join(", ", restless)}");
        }

        // The other half of the question. A mute button on a headset classically reports
        // itself on the telephony HID page rather than in any vendor register, and that
        // arrives unprompted — so it can only be caught while the switch is being moved,
        // which is exactly the pause below.
        //
        // Every interface except the one the session is holding: a second handle on the
        // RACE endpoint would drain the replies the sweep is waiting for.
        var caught = new List<string>();
        var channels = new List<(MaxwellInterface Face, WindowsHidChannel Channel)>();

        foreach (MaxwellInterface each in enumerator.Enumerate())
        {
            if (string.Equals(each.Path, target.Path, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                channels.Add((each, new WindowsHidChannel(each.Path, each.InputReportLength, each.OutputReportLength)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  не открылся {each.UsagePage:X4}:{each.Usage:X2} — {ex.Message}");
            }
        }

        using var stop = new CancellationTokenSource();

        Task[] watchers = [.. channels.Select(pair => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                byte[]? report = pair.Channel.Listen(TimeSpan.FromMilliseconds(200));
                if (report is null) continue;

                string line = $"{pair.Face.UsagePage:X4}:{pair.Face.Usage:X2} {Page(pair.Face.UsagePage)}"
                    + $"  {Convert.ToHexString(report.AsSpan(0, Math.Min(16, report.Length)))}";

                lock (caught) caught.Add(line);
            }
        }, CancellationToken.None))];

        Console.WriteLine();
        Console.WriteLine($"Слушаю {channels.Count} HID-интерфейс(ов). Щёлкните переключателем микрофона и нажмите Enter.");
        Console.ReadLine();

        await stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(watchers).ConfigureAwait(false);
        foreach ((_, WindowsHidChannel channel) in channels) channel.Dispose();

        Console.WriteLine(caught.Count == 0
            ? "  по HID не пришло ничего: переключатель не объявляет о себе."
            : $"  по HID пришло {caught.Count} отчёт(ов):");

        foreach (string line in caught.Take(20)) Console.WriteLine($"    {line}");

        Console.WriteLine("Снимаю второй слепок...");
        Dictionary<string, string> after = await SweepAsync(session, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  снято: {after.Count(p => p.Value != "—")} ответивших из {after.Count}");
        Console.WriteLine();

        // A register that failed to answer on either side says nothing about the switch.
        List<string> moved =
        [
            .. before.Keys
                .Where(key => !restless.Contains(key))
                .Where(key => Both(before, after, key) && before[key] != after[key]),
        ];

        if (moved.Count == 0)
        {
            Console.WriteLine("Ничего не изменилось. Переключатель не виден ни в одном регистре,");
            Console.WriteLine("ни в структуре состояния, ни во флагах Windows.");
            return 0;
        }

        Console.WriteLine($"   {"регистр",-22} {"было",-14} стало");
        Console.WriteLine($"   {new string('-', 22)} {new string('-', 14)} ------");

        foreach (string key in moved)
            Console.WriteLine($"   {key,-22} {before[key],-14} {after[key]}");

        return 0;
    }

    private static async Task<Dictionary<string, string>> SweepAsync(
        RaceSession session, CancellationToken cancellationToken)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        for (byte sub = 0; sub <= LastFeature; sub++)
        {
            int value = await session
                .ReadValueAsync(RaceCommands.Read, sub, Attempts, WindowMs, cancellationToken)
                .ConfigureAwait(false);

            seen[$"0x0901 {sub:X2}"] = Say(value);
        }

        for (byte sub = 0; sub <= LastChat; sub++)
        {
            int value = await session
                .ReadNamespace2CAsync(sub, Attempts, WindowMs, cancellationToken)
                .ConfigureAwait(false);

            seen[$"0x2C83 {sub:X2}"] = Say(value);
        }

        byte[]? status = await session.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        seen["0x0910 состояние"] = status is null ? "—" : Convert.ToHexString(status);

        int battery = await session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
        seen["батарея"] = battery >= 0 ? $"{battery}%" : "—";

        // What Windows thinks, in the same breath. If the switch reaches the operating
        // system at all, it reaches it here.
        var endpoints = new WindowsAudioEndpoints();

        AudioEndpoint? mic = endpoints.Enumerate()
            .FirstOrDefault(e => e.Flow == AudioFlow.Capture
                && !e.IsContainerless
                && e.Name.Contains("Maxwell", StringComparison.OrdinalIgnoreCase));

        if (mic is not null)
        {
            using IAudioEndpointChannel channel = endpoints.Open(mic.Id);
            seen["Windows mute"] = channel.Mute is { } m ? m ? "да" : "нет" : "—";
            seen["Windows громкость"] = channel.Volume is { } v ? $"{v}%" : "—";
        }

        return seen;
    }

    /// <summary>Both sides answered, so a difference between them is about the device rather than the link.</summary>
    private static bool Both(Dictionary<string, string> left, Dictionary<string, string> right, string key) =>
        left.GetValueOrDefault(key, "—") != "—" && right.GetValueOrDefault(key, "—") != "—";

    private static string Say(int value) => value < 0 ? "—" : $"{value,3} (0x{value:X2})";

    private static string Page(ushort usagePage) => usagePage switch
    {
        0x000C => "громкость, колесо",
        0x000B => "телефония — заглушка, снятие трубки",
        0xFF13 => "RACE — настройки",
        _ => "",
    };
}
