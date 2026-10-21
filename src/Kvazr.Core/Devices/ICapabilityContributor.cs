namespace Kvazr.Core.Devices;

/// <summary>
/// Adds capabilities to a device somebody else found.
///
/// The registry answers one question — who owns this piece of hardware — and answers it
/// by shadowing: the driver that understands the device best keeps it and the rest are
/// dropped. That is right for finders, and wrong for everything that only knows one
/// thing about a device it did not find.
///
/// Core Audio knows the volume of a headset whose battery is Audeze's business. The HID
/// battery usage knows the charge of a keyboard nobody has a protocol for. The Flydigi
/// dock knows what its gamepad has left. None of them can be the owning driver — they
/// would have to invent a name, a class and a transport they know nothing about — and
/// under shadowing alone each of them either loses its rows or takes the whole device
/// away from the driver that actually understands it.
///
/// So the two questions are separated. <see cref="IDeviceDriver"/> finds and owns;
/// a contributor is handed a device that already exists and says whether it has
/// anything to add. The session holds the owning connection plus whatever attached,
/// concatenates their capabilities and routes each read and write to whoever declared
/// the id.
/// </summary>
public interface ICapabilityContributor
{
    /// <summary>Stable name for logs, e.g. "audio" or "hid.battery".</summary>
    string ContributorId { get; }

    /// <summary>
    /// Whether this contributor has anything to say about the device, and a connection
    /// carrying it if so. Null means "not mine", which is the ordinary answer and never
    /// an error — an audio contributor is asked about every mouse on the machine.
    ///
    /// Matching is on <see cref="DeviceInfo.HardwareIds"/>, the same set the registry
    /// already uses to merge a dongle with a cable. Matching on the model name instead
    /// would break on the first device the operating system names differently from the
    /// catalogue, which is most of them.
    ///
    /// Called on every discovery sweep until it attaches, so it must be cheap and must
    /// not hold anything open while deciding.
    /// </summary>
    /// <exception cref="IOException">
    /// The contributor recognised the device and could not open it. Logged and retried
    /// on the next sweep; the device itself is unaffected.
    /// </exception>
    Task<IDeviceConnection?> AttachAsync(DeviceInfo device, CancellationToken cancellationToken = default);
}
