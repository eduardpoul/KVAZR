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
        Console.WriteLine($"[scan] {seconds} с на каждую попытку");
        Console.WriteLine();

        // Devices already paired, asked for twice: once by a call that returns an answer,
        // once by a watcher that raises events. Windows finds these instantly either way,
        // so a difference between the two columns is not about the radio at all — it is
        // about whether WinRT events reach this process. Everything that has worked here
        // so far was an async call; everything silent was an event.
        Console.WriteLine("  уже сопряжённые — их система знает наверняка:");
        int calledLe = Called("низкоэнергетические", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
        int watchedLe = Watch("низкоэнергетические", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), seconds);

        Console.WriteLine();
        Console.WriteLine("  вокруг, не сопряжённые — здесь нужен настоящий поиск:");
        int calledNear = Called("низкоэнергетические", BluetoothLEDevice.GetDeviceSelectorFromPairingState(false));
        int watchedNear = Watch("низкоэнергетические", BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), seconds);
        int calledClassic = Called("классические", BluetoothDevice.GetDeviceSelectorFromPairingState(false));
        int watchedClassic = Watch("классические", BluetoothDevice.GetDeviceSelectorFromPairingState(false), seconds);

        Console.WriteLine();

        if (calledLe > 0 && watchedLe == 0)
        {
            Console.WriteLine("  Вызов отвечает, наблюдатель молчит — на том же самом наборе устройств.");
            Console.WriteLine("  Значит события WinRT до этого процесса не доходят. Это наша беда, а не радио.");
        }
        else if (calledLe == 0 && watchedLe == 0)
        {
            Console.WriteLine("  Молчат оба даже про сопряжённые. Дело глубже, чем поиск.");
        }
        else
        {
            Console.WriteLine("  События доходят. Тогда молчание про окружающие — про сам поиск.");
        }

        return calledNear + watchedNear + calledClassic + watchedClassic > 0 ? 0 : 1;
    }

    /// <summary>The asking kind: one call, one answer, no events involved.</summary>
    private static int Called(string what, string selector)
    {
        try
        {
            DeviceInformationCollection found = DeviceInformation
                .FindAllAsync(selector, [], DeviceInformationKind.AssociationEndpoint)
                .AsTask().Result;

            Console.WriteLine($"    вызовом      {what,-22} {found.Count}");

            foreach (DeviceInformation device in found.Take(8))
            {
                Console.WriteLine($"      {device.Name}");
            }

            return found.Count;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    вызовом      {what,-22} не вышло: {ex.GetType().Name} {ex.Message}");
            return 0;
        }
    }

    private static int Watch(string what, string selector, int seconds)
    {

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
            Console.WriteLine($"    наблюдателем {what,-22} {found.Count}   состояние {status}");
            foreach (string line in found) Console.WriteLine(line);

            return found.Count;
        }
    }
}
