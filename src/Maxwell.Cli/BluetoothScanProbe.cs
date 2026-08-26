using System.Runtime.Versioning;

using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Maxwell.Cli;

/// <summary>
/// Asks Windows to look around, through the door the operating system's own "add a
/// device" screen uses.
///
/// The advertisement watcher next door hears nothing on this machine — not from Apple,
/// from anything, in either scanning mode, with the radio on and every consent given, and
/// with both audio devices unpaired so the radio has nothing else to do. That is either a
/// broken listener or a radio that cannot scan, and one API cannot tell the two apart.
///
/// So this is the second opinion: a device watcher over the same radio, which reaches the
/// same operation through device enumeration rather than through the advertisement API.
/// Both silent means the radio will not scan. This one finding things means the fault is
/// in the listener, and that is a fixable kind of fault.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class BluetoothScanProbe
{
    public static int Run(int seconds)
    {
        Console.WriteLine($"[scan] перечисление устройств, {seconds} с на каждый вид");
        Console.WriteLine();

        int le = Watch("низкоэнергетические, не сопряжённые",
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), seconds);

        int classic = Watch("классические, не сопряжённые",
            BluetoothDevice.GetDeviceSelectorFromPairingState(false), seconds);

        Console.WriteLine();
        Console.WriteLine(le + classic > 0
            ? "  Радио сканирует. Значит глух именно слушатель рекламы."
            : "  Не нашлось ничего и здесь. Радио не сканирует — дело не в нашем коде.");

        return le + classic > 0 ? 0 : 1;
    }

    private static int Watch(string what, string selector, int seconds)
    {
        Console.WriteLine($"  {what}...");

        var found = new List<string>();
        var done = new ManualResetEventSlim(false);

        DeviceWatcher watcher = DeviceInformation.CreateWatcher(
            selector,

            // Asked for by name because the default set does not include them, and a row
            // without a signal strength cannot say how far away the thing is.
            ["System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.SignalStrength"],
            DeviceInformationKind.AssociationEndpoint);

        watcher.Added += (_, device) =>
        {
            object? address = device.Properties.GetValueOrDefault("System.Devices.Aep.DeviceAddress");
            object? signal = device.Properties.GetValueOrDefault("System.Devices.Aep.SignalStrength");

            lock (found)
            {
                found.Add($"    {address ?? "—",-20} {signal,4}  {device.Name}");
            }
        };

        watcher.EnumerationCompleted += (_, _) => done.Set();
        watcher.Stopped += (_, _) => done.Set();

        watcher.Start();
        done.Wait(TimeSpan.FromSeconds(seconds));

        // Kept running past the first sweep on purpose: enumeration completes as soon as
        // the cached devices are listed, and the ones actually in the air arrive after it.
        Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0, seconds - 2)));

        DeviceWatcherStatus status = watcher.Status;
        watcher.Stop();

        lock (found)
        {
            foreach (string line in found) Console.WriteLine(line);
            Console.WriteLine($"    найдено {found.Count}, состояние {status}");

            return found.Count;
        }
    }
}
