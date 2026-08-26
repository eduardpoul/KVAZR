using System.Globalization;

using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.Apple;

/// <summary>
/// Adds the battery to a pair of Apple earphones the system already found.
///
/// A contributor rather than a driver, and for the usual reason: Windows gets the rest of
/// these right — the name, the class, the audio endpoints, the microphone — and the one
/// thing it does not know is the charge. It does not know it because these earphones never
/// tell it: a classic headset reports its charge through the hands-free indicator and
/// Windows lays that out as a device property, and AirPods simply do not send it. Their
/// charge rides a low-energy beacon instead, meant for Apple's own devices, and reading it
/// is all this does.
///
/// Matched on the product id, and the match is the interesting part. A beacon carries no
/// serial and its Bluetooth address is random and rotates, so nothing in it points at a
/// particular device — except the model bytes, which read the other way round are exactly
/// the product id Windows carries for the paired device. Windows says 004C:2013; the
/// beacon says 13 20.
/// </summary>
public sealed class AppleProximityContributor : ICapabilityContributor
{
    /// <summary>Apple, as the USB and Bluetooth registries both number them.</summary>
    private const ushort Apple = 0x004C;

    private readonly IProximityListener _listener;

    public AppleProximityContributor(IProximityListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        _listener = listener;
    }

    public string ContributorId => "apple.proximity";

    public Task<IDeviceConnection?> AttachAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (Product(device) is not { } product) return Task.FromResult<IDeviceConnection?>(null);

        // Attached whether or not anything has been heard yet. The beacon is not
        // continuous — earphones in ears with the case shut are silent, measured — so
        // waiting for one before offering the rows would mean a page that gains a battery
        // section the first time its owner opens the case and never before.
        return Task.FromResult<IDeviceConnection?>(
            new AppleProximityConnection(_listener, product, device.Transports));
    }

    /// <summary>
    /// The product id of an Apple device, from the ids the system already carries. Null
    /// for everything else, which is nearly every device it will be asked about.
    /// </summary>
    private static ushort? Product(DeviceInfo device)
    {
        foreach (string id in device.HardwareIds)
        {
            if (!id.StartsWith($"usb:{Apple:x4}:", StringComparison.OrdinalIgnoreCase)) continue;

            if (ushort.TryParse(
                id.AsSpan(id.LastIndexOf(':') + 1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out ushort product))
            {
                return product;
            }
        }

        return null;
    }
}
