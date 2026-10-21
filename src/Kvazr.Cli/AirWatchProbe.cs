using System.Runtime.Versioning;

using Windows.Devices.Bluetooth.Advertisement;

namespace Kvazr.Cli;

/// <summary>
/// Everyone in earshot, named where they say a name.
///
/// The device watcher next door answers "nothing unpaired" and cannot be believed on its
/// own: it found a monitor over classic Bluetooth and nothing at all over low energy, and
/// "nothing is there" and "this path cannot see" print the same. This one listens to the
/// advertisements themselves — the path that has already been shown to work on this
/// machine, a hundred and thirty frames from twenty-two radios in twelve seconds — so a
/// silence here is about the air rather than about the listener.
///
/// A keyboard put into pairing starts advertising, usually with its name and the HID
/// service. Both are printed where they are given.
///
/// Read only. Nothing is paired, connected or written.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class AirWatchProbe
{
    /// <summary>The service a keyboard, mouse or gamepad announces itself with.</summary>
    private static readonly Guid HumanInterface = new("00001812-0000-1000-8000-00805f9b34fb");

    private sealed record Heard(string? Name, short Signal, int Count, bool Hid);

    public static async Task<int> RunAsync(int seconds, string? match, CancellationToken cancellationToken = default)
    {
        var heard = new Dictionary<ulong, Heard>();

        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        watcher.Received += (_, args) =>
        {
            lock (heard)
            {
                Heard? before = heard.GetValueOrDefault(args.BluetoothAddress);

                heard[args.BluetoothAddress] = new Heard(
                    // A name arrives in some frames and not others, so the one already
                    // known outranks a later frame that carries none.
                    args.Advertisement.LocalName is { Length: > 0 } name ? name : before?.Name,
                    args.RawSignalStrengthInDBm,
                    (before?.Count ?? 0) + 1,
                    (before?.Hid ?? false) || args.Advertisement.ServiceUuids.Contains(HumanInterface));
            }
        };

        Console.WriteLine($"Слушаю эфир {seconds} с. Ничего не отправляется.");
        Console.WriteLine();

        watcher.Start();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
        }

        lock (heard)
        {
            List<KeyValuePair<ulong, Heard>> found =
            [
                .. heard.Where(p => match is not { Length: > 0 }
                        || (p.Value.Name?.Contains(match, StringComparison.OrdinalIgnoreCase) ?? false))
                    .OrderByDescending(p => p.Value.Signal),
            ];

            Console.WriteLine($"   {"адрес",-14} {"сигнал",-7} {"кадров",-7} {"HID",-4} имя");
            Console.WriteLine($"   {new string('-', 14)} {new string('-', 7)} {new string('-', 7)} {new string('-', 4)} ----");

            foreach ((ulong address, Heard who) in found)
            {
                Console.WriteLine(
                    $"   {address:X12}   {who.Signal,5}   {who.Count,5}   {(who.Hid ? "да" : "—"),-4} {who.Name ?? ""}");
            }

            Console.WriteLine();
            Console.WriteLine($"   радио в эфире: {heard.Count}, из них с именем: {heard.Count(p => p.Value.Name is { Length: > 0 })}");
            Console.WriteLine($"   объявляют HID: {heard.Count(p => p.Value.Hid)}");
        }

        return 0;
    }
}
