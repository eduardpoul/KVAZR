// Windows only, and only on the framework that projects WinRT: there is no Win32 way to
// hear a Bluetooth advertisement. On the plain build this file is simply not there, and a
// host that finds no IProximityListener registers no Apple contributor — see AppleModule.
#if WINDOWS

using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;

using Windows.Devices.Bluetooth.Advertisement;

namespace Kvazr.Race.Windows;

/// <summary>
/// Keeps a Bluetooth radio listening, and remembers the last thing each model said.
///
/// Apple's earphones put their battery in a beacon rather than answering questions: there
/// is no service to connect to on Windows — the accessory protocol they do publish runs
/// over L2CAP, and Windows gives user-mode sockets RFCOMM only. So the only way to know
/// what an AirPod has left is to hear it say so.
///
/// One listener for the machine. Scanning is a radio doing work, and one watcher serving
/// every device costs the same as one watcher serving one.
///
/// A warning worth keeping, because it cost most of a day: this radio can stop returning
/// scan results while continuing to do everything else — connections hold, paired devices
/// enumerate, and the watcher reports itself Started while delivering nothing at all, from
/// any device, in either scanning mode. Turning Bluetooth off and on again clears it.
/// Nothing here can tell that state from an empty room, which is exactly why silence must
/// never be reported as "this device has no battery".
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsProximityListener : IProximityListener, IDisposable
{
    private readonly ConcurrentDictionary<ushort, ProximityReading> _heard = new();
    private readonly BluetoothLEAdvertisementWatcher _watcher;

    private bool _disposed;

    public WindowsProximityListener()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            // Active, so the scan response comes too. Passive was measured against it and
            // heard the same devices; active costs a little air and loses nothing.
            ScanningMode = BluetoothLEScanningMode.Active,
        };

        _watcher.Received += OnReceived;
        _watcher.Start();
    }

    public ProximityReading? Latest(ushort productId) =>
        _heard.TryGetValue(productId, out ProximityReading? reading) ? reading : null;

    private void OnReceived(BluetoothLEAdvertisementWatcher radio, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (BluetoothLEManufacturerData data in args.Advertisement.ManufacturerData)
        {
            if (data.CompanyId != AppleProximity.Company) continue;

            if (AppleProximity.Read(data.Data.ToArray(), args.RawSignalStrengthInDBm, DateTimeOffset.UtcNow)
                is not { } reading)
            {
                continue;
            }

            // The nearest one wins where two of a model are in the room, which is the best
            // that can be done: the beacon carries no serial, and its address is random.
            _heard.AddOrUpdate(
                reading.ProductId,
                reading,
                (_, held) => reading.SignalStrength >= held.SignalStrength || held.At < reading.At.AddSeconds(-30)
                    ? reading
                    : held);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher.Received -= OnReceived;

        try
        {
            _watcher.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Already stopped, or the radio went away underneath it. Nothing to do about
            // either at shutdown.
        }
    }
}

#endif
