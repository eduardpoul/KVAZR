using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;

using Windows.Devices.Bluetooth.Advertisement;

namespace Maxwell.Cli;

/// <summary>
/// Listens for Apple's proximity-pairing advertisements, which is where AirPods battery
/// actually lives. There is no GATT battery service and no Windows property — the pods
/// broadcast their state in manufacturer data (company 0x004C, message type 0x07), and
/// every "AirPods battery on Windows" utility is reading exactly this.
///
/// Purely passive: an advertisement watcher receives what is already in the air.
/// The layout is community-documented (OpenPods et al.): battery nibbles for each pod,
/// a charging bit field, and a case level that is only valid while the lid is open.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class AirPodsProbe
{
    public static int Run()
    {
        Console.WriteLine("Слушаю BLE-рекламу Apple (10 с). Откройте крышку кейса рядом с ПК.");
        Console.WriteLine();

        var heard = new HashSet<string>(StringComparer.Ordinal);

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };

        watcher.Received += (_, args) =>
        {
            foreach (BluetoothLEManufacturerData data in args.Advertisement.ManufacturerData)
            {
                if (data.CompanyId != 0x004C) continue;

                byte[] payload = data.Data.ToArray();
                if (payload.Length < 10 || payload[0] != 0x07) continue;

                string line = Decode(payload, args.RawSignalStrengthInDBm);
                if (heard.Add(line)) Console.WriteLine(line);
            }
        };

        watcher.Start();
        Thread.Sleep(TimeSpan.FromSeconds(10));
        watcher.Stop();

        if (heard.Count == 0)
        {
            Console.WriteLine("Ничего не поймано. Реклама идёт, когда крышка кейса открыта или");
            Console.WriteLine("наушники в ушах; закрытый кейс в кармане молчит.");
        }

        return 0;
    }

    private static string Decode(byte[] p, short rssi)
    {
        // Community layout for message 0x07 (proximity pairing): model at 3..4, status
        // at 5, pod battery nibbles at 6, charging bits + case level at 7.
        ushort model = (ushort)((p[3] << 8) | p[4]);

        int left = p[6] >> 4;
        int right = p[6] & 0x0F;
        int @case = p[7] & 0x0F;
        int chargeBits = p[7] >> 4;

        static string Pod(int nibble) => nibble > 10 ? "—" : $"{nibble * 10}%";

        return $"model 0x{model:X4}  rssi {rssi}  " +
               $"L {Pod(left)}{((chargeBits & 0x1) != 0 ? "⚡" : "")}  " +
               $"R {Pod(right)}{((chargeBits & 0x2) != 0 ? "⚡" : "")}  " +
               $"кейс {Pod(@case)}{((chargeBits & 0x4) != 0 ? "⚡" : "")}";
    }
}
