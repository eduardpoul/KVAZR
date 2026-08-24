using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.HyperX;

/// <summary>
/// Adds what a HyperX microphone knows about itself to whatever driver found it.
///
/// A contributor rather than a driver, and that is the whole design. Owning the device
/// would mean taking it from the system tier and answering for its name, its class and
/// its endpoints — all of which the system already gets right — in exchange for one row
/// nobody else can supply. The audio contributor makes the same trade for volume.
///
/// Matched on the usb ids the operating system already carries. The microphone appears as
/// two USB functions: 0951:171d is the audio side, which is the one Windows names and the
/// one the page is built from, and 0951:171f is the control side, where the status channel
/// lives. Either is enough to recognise the device; the channel is then found by its usage
/// page rather than by walking back to a sibling function, because a page is what the
/// protocol is actually addressed by.
/// </summary>
public sealed class HyperXContributor : ICapabilityContributor
{
    /// <summary>
    /// The QuadCast's two functions. Deliberately not "any HyperX product": this
    /// contributor knows one status channel and the sub-commands on it, and a Cloud
    /// headset would answer differently or not at all.
    /// </summary>
    private static readonly string[] Known =
    [
        "usb:0951:171d",
        "usb:0951:171f",
    ];

    private readonly IHidListenerFactory _listeners;

    public HyperXContributor(IHidListenerFactory listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);

        _listeners = listeners;
    }

    public string ContributorId => "hyperx";

    public Task<IDeviceConnection?> AttachAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!device.HardwareIds.Any(id => Known.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            return Task.FromResult<IDeviceConnection?>(null);
        }

        IHidListener? listener = _listeners.Open(HyperXConnection.Vendor, HyperXConnection.StatusPage);

        // Recognised and there is no channel: an older model, or one whose control
        // function the operating system has not enumerated. Not an error, and not a
        // reason to hold up the rest of the device.
        return Task.FromResult<IDeviceConnection?>(
            listener is null ? null : new HyperXConnection(listener));
    }
}
