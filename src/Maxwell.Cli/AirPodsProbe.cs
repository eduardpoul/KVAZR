using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;

using Windows.Devices.Bluetooth.Advertisement;

namespace Maxwell.Cli;

/// <summary>
/// Listens for what is already in the air, and says what it heard — all of it, not only
/// the part it was hoping for.
///
/// AirPods battery is supposed to live in Apple's proximity-pairing advertisement:
/// company 0x004C, message type 0x07, battery nibbles inside. Every "AirPods battery on
/// Windows" utility reads exactly that. The other route — Apple's own accessory protocol,
/// which the pods publish as an SDP service and which Windows lists as "AAP Server" — is
/// out of reach here: it runs over L2CAP, and Windows gives user-mode sockets RFCOMM only.
///
/// The first run of this heard nothing, with the case open and the pods connected. That
/// says nothing about AirPods until we know whether the listener hears anything at all,
/// so it counts everything: how many advertisements arrived, from how many radios, which
/// manufacturers, and the raw bytes of every Apple one. Both scanning modes are tried,
/// because an adapter that refuses one may answer the other.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class AirPodsProbe
{
    private const ushort Apple = 0x004C;
    private const byte ProximityPairing = 0x07;

    public static int Run(int seconds)
    {
        Radio();

        var heard = new Ears();

        foreach (BluetoothLEScanningMode mode in (BluetoothLEScanningMode[])
                 [BluetoothLEScanningMode.Active, BluetoothLEScanningMode.Passive])
        {
            Console.WriteLine();
            Console.WriteLine($"  {mode}, {seconds} с...");

            int before = heard.Total;
            Listen(mode, seconds, heard);

            Console.WriteLine($"    поймано {heard.Total - before}");
        }

        Console.WriteLine();
        Console.WriteLine($"  реклам всего   {heard.Total}");
        Console.WriteLine($"  разных радио   {heard.Radios.Count}");

        if (heard.Total == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Ни одной рекламы, ни от одного устройства, ни в одном режиме.");
            Console.WriteLine("  Это не про AirPods: столько слышит выключенное или занятое радио.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("  производители в эфире:");

        foreach ((ushort id, int count) in heard.Makers.OrderByDescending(m => m.Value))
        {
            Console.WriteLine($"    0x{id:X4}  {count,4}{(id == Apple ? "   ← Apple" : string.Empty)}");
        }

        foreach (string line in heard.Services) Console.WriteLine($"    {line}");

        Console.WriteLine();

        if (heard.Apple.Count == 0)
        {
            Console.WriteLine("  От Apple ничего. Реклама близости идёт при открытой крышке или когда");
            Console.WriteLine("  наушники в ушах, и может не идти, пока они подключены к этому ПК.");
            return 1;
        }

        Console.WriteLine($"  Apple: {heard.Apple.Count} разных полезных нагрузок");
        Console.WriteLine();

        foreach ((string hex, (int count, short rssi, ulong address)) in
                 heard.Apple.OrderByDescending(a => a.Value.Count))
        {
            Console.WriteLine($"    {address:X12}  rssi {rssi,4}  ×{count}");
            Console.WriteLine($"      {hex}");

            byte[] payload = Convert.FromHexString(hex);
            if (payload.Length >= 10 && payload[0] == ProximityPairing) Console.WriteLine($"      {Guess(payload)}");

            Console.WriteLine();
        }

        return 0;
    }


    /// <summary>
    /// Turns the radio off and on again, and scans either side of it.
    ///
    /// Not a fix, a question. This adapter connects to low-energy devices happily — the
    /// mouse on it was read, unpaired and paired again — and a device it once had to
    /// *find* is sitting in its paired list, so scanning worked at least once. A thing
    /// that worked and stopped is a state, and a state is worth clearing before anything
    /// is blamed on the hardware.
    ///
    /// Costs a few seconds of Bluetooth, and puts it back.
    /// </summary>
    public static int Reset(int seconds)
    {
        var before = new Ears();
        Console.WriteLine("  до перезапуска радио:");
        Listen(BluetoothLEScanningMode.Active, seconds, before);
        Console.WriteLine($"    поймано {before.Total}");

        Console.WriteLine();

        if (!Toggle()) return 1;

        var after = new Ears();
        Console.WriteLine();
        Console.WriteLine("  после:");
        Listen(BluetoothLEScanningMode.Active, seconds, after);
        Console.WriteLine($"    поймано {after.Total}, разных радио {after.Radios.Count}");

        foreach ((ushort id, int count) in after.Makers.OrderByDescending(m => m.Value))
        {
            Console.WriteLine($"      0x{id:X4}  {count,4}{(id == Apple ? "   ← Apple" : string.Empty)}");
        }

        foreach ((string hex, (int count, short rssi, ulong address)) in after.Apple)
        {
            Console.WriteLine($"      {address:X12}  rssi {rssi}  ×{count}  {hex}");

            byte[] payload = Convert.FromHexString(hex);
            if (payload.Length >= 10 && payload[0] == ProximityPairing) Console.WriteLine($"        {Guess(payload)}");
        }

        Console.WriteLine();
        Console.WriteLine(after.Total > 0
            ? "  Заработало. Значит радио застревало, а не не умело."
            : "  Не помогло. Состояние ни при чём.");

        return after.Total > 0 ? 0 : 1;
    }

    /// <summary>Off, a pause, on, and time to come back.</summary>
    private static bool Toggle()
    {
        try
        {
            var access = Windows.Devices.Radios.Radio.RequestAccessAsync().AsTask().Result;

            if (access != Windows.Devices.Radios.RadioAccessStatus.Allowed)
            {
                Console.WriteLine($"  радио трогать не дают: {access}");
                return false;
            }

            var radios = Windows.Devices.Radios.Radio.GetRadiosAsync().AsTask().Result;
            var bluetooth = radios.FirstOrDefault(r => r.Kind == Windows.Devices.Radios.RadioKind.Bluetooth);

            if (bluetooth is null)
            {
                Console.WriteLine("  радио Bluetooth не нашлось");
                return false;
            }

            Console.WriteLine($"  выключаю {bluetooth.Name}...");
            Console.WriteLine($"    {bluetooth.SetStateAsync(Windows.Devices.Radios.RadioState.Off).AsTask().Result}");
            Thread.Sleep(TimeSpan.FromSeconds(3));

            Console.WriteLine("  включаю обратно...");
            Console.WriteLine($"    {bluetooth.SetStateAsync(Windows.Devices.Radios.RadioState.On).AsTask().Result}");

            // Long enough for the stack to bring the adapter and its devices back.
            Thread.Sleep(TimeSpan.FromSeconds(8));

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  не вышло: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    /// <summary>Everything the listener collected, across every mode it was run in.</summary>
    private sealed class Ears
    {
        public int Total { get; set; }

        public HashSet<ulong> Radios { get; } = [];

        public Dictionary<ushort, int> Makers { get; } = [];

        public Dictionary<string, (int Count, short Rssi, ulong Address)> Apple { get; } =
            new(StringComparer.Ordinal);

        public HashSet<string> Services { get; } = new(StringComparer.Ordinal);
    }

    private static void Listen(BluetoothLEScanningMode mode, int seconds, Ears heard)
    {
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = mode };

        watcher.Received += (radio, args) =>
        {
            lock (heard)
            {
                heard.Total++;
                heard.Radios.Add(args.BluetoothAddress);

                foreach (BluetoothLEManufacturerData data in args.Advertisement.ManufacturerData)
                {
                    heard.Makers[data.CompanyId] = heard.Makers.GetValueOrDefault(data.CompanyId) + 1;

                    if (data.CompanyId != Apple) continue;

                    string hex = Convert.ToHexString(data.Data.ToArray());
                    int count = heard.Apple.GetValueOrDefault(hex).Count;
                    heard.Apple[hex] = (count + 1, args.RawSignalStrengthInDBm, args.BluetoothAddress);
                }

                // The universal path, worth catching alongside: the GATT battery service
                // broadcast as advertisement data. Any maker may do it, and one that does
                // hands its percentage to a passive listener for nothing.
                foreach (BluetoothLEAdvertisementDataSection section in args.Advertisement.DataSections)
                {
                    if (section.DataType != 0x16) continue;

                    byte[] payload = section.Data.ToArray();
                    if (payload.Length < 3 || payload[0] != 0x0F || payload[1] != 0x18) continue;

                    heard.Services.Add($"{args.BluetoothAddress:X12}  GATT Battery Service  {payload[2]}%");
                }
            }
        };

        watcher.Stopped += (radio, args) =>
        {
            if (args.Error != Windows.Devices.Bluetooth.BluetoothError.Success) Console.WriteLine($"    остановлен: {args.Error}");
        };

        watcher.Start();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));

        Console.WriteLine($"    состояние {watcher.Status}");
        watcher.Stop();
    }

    /// <summary>
    /// The radio, before anything is concluded from silence. A watcher that says it
    /// started and hears nothing is not a fact about any device until the thing doing the
    /// listening has been asked whether it can.
    /// </summary>
    private static void Radio()
    {
        Console.WriteLine($"  поток        {Thread.CurrentThread.GetApartmentState()}");

        try
        {
            var adapter = Windows.Devices.Bluetooth.BluetoothAdapter.GetDefaultAsync().AsTask().Result;

            if (adapter is null)
            {
                Console.WriteLine("  адаптер      нет");
                return;
            }

            Console.WriteLine($"  адаптер      {adapter.BluetoothAddress:X12}"
                + $"  low energy {adapter.IsLowEnergySupported}"
                + $"  central {adapter.IsCentralRoleSupported}");

            var radio = adapter.GetRadioAsync().AsTask().Result;
            if (radio is not null) Console.WriteLine($"  радио        {radio.Name}: {radio.State}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  адаптер      не отвечает: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// The community's reading of message 0x07, offered as a guess beside the bytes.
    ///
    /// Which nibble is the left pod depends on a bit in the status byte, and the write-ups
    /// disagree about which bit — a thing to settle against what the pods themselves say,
    /// not to pick. So both are printed, unlabelled, and the status byte with them.
    /// </summary>
    private static string Guess(byte[] p)
    {
        ushort model = (ushort)((p[3] << 8) | p[4]);

        static string Level(int nibble) =>
            nibble > 10 ? "—" : (nibble * 10).ToString(CultureInfo.InvariantCulture) + "%";

        return $"модель 0x{model:X4}  статус 0x{p[5]:X2}  "
            + $"нибблы {Level(p[6] >> 4)}/{Level(p[6] & 0x0F)}  кейс {Level(p[7] & 0x0F)}  "
            + $"флаги зарядки 0x{p[7] >> 4:X1}";
    }
}
