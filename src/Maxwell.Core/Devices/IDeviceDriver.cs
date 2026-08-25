namespace Maxwell.Core.Devices;

/// <summary>
/// How much a driver knows about what it finds. Order matters: these are compared.
/// </summary>
public enum DriverTier
{
    /// <summary>
    /// Whatever the operating system already knows, for every device on the machine.
    /// No protocol, no vendor knowledge, no catalogue entry — a name, a class, a link
    /// and sometimes a battery. Thin, but thin for everything equally, which is what
    /// makes the long tail of devices tractable at all.
    /// </summary>
    System,

    /// <summary>
    /// A protocol shared by a whole class of hardware regardless of who made it — VIA
    /// and QMK, HID++, XInput, USB Audio. One implementation covers hundreds of models,
    /// which is why these are worth more than any single vendor integration.
    /// </summary>
    Class,

    /// <summary>
    /// One vendor's own protocol, reverse-engineered. Expensive per family and cheap per
    /// model afterwards, provided the models stay data and only the protocol is code.
    /// </summary>
    Vendor,
}

/// <summary>
/// Knows how to find and talk to one family of devices — Audeze over RACE, Logitech
/// over HID++, a QMK keyboard over VIA.
///
/// A driver translates its protocol into capabilities at its own edge and stays
/// strongly typed inside. That boundary is intentionally thin: the capability model is
/// currently derived from exactly one real device, so it is almost certainly wrong in
/// some way that only a second real driver will reveal. Keeping the translation layer
/// small is what makes being wrong cheap.
/// </summary>
public interface IDeviceDriver
{
    /// <summary>Stable id used in <see cref="DeviceInfo.DriverId"/>, e.g. "audeze.race".</summary>
    string DriverId { get; }

    /// <summary>
    /// How much this driver knows. When two drivers find the same physical device — and
    /// they will, because the system driver finds everything — the higher tier keeps it
    /// and the lower one is dropped. That is what lets a driver that finds every device
    /// coexist with one that understands a single family properly.
    /// </summary>
    DriverTier Tier => DriverTier.Vendor;

    /// <summary>
    /// Devices of this family currently attached. Cheap enough to call on a device-change
    /// notification; must not open or lock anything.
    /// </summary>
    Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the device for reading and writing.</summary>
    /// <exception cref="IOException">The device could not be opened.</exception>
    Task<IDeviceConnection> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default);
}

/// <summary>
/// An open channel to one device.
///
/// Implementations may assume calls are serialised — the session above guarantees it,
/// because RACE and most vendor protocols do not tolerate overlapping requests and
/// answer the wrong question when they overlap.
/// </summary>
public interface IDeviceConnection : IDisposable
{
    /// <summary>
    /// What this device offers, fixed for the lifetime of the connection. Ranges whose
    /// source is <see cref="RangeSource.Device"/> are declared here but resolved by the
    /// session once their backing capability has been read.
    /// </summary>
    IReadOnlyList<Capability> Capabilities { get; }

    /// <summary>Transports as they stand, refreshed on reconnect.</summary>
    IReadOnlyList<TransportInfo> Transports { get; }

    /// <summary>
    /// The one reading that means "the device itself is here and awake", where the driver
    /// knows which that is.
    ///
    /// Only a driver can know. A Maxwell on its dongle answers a battery percentage with
    /// the headset switched off and in a drawer — the dongle remembers it — and answers
    /// its firmware version out of the same memory, while the amp gain and the ChatMix go
    /// quiet the moment the headset does. Measured, with the headset in hand. A session
    /// looking for a lively-looking row would have picked one of the first two and called
    /// a drawer a head.
    ///
    /// Null means there is nothing so decisive, which is the ordinary case: the session
    /// then falls back to asking a few of the device's own rows and calling it silent only
    /// when every one of them is.
    /// </summary>
    string? Heartbeat => null;

    /// <summary>
    /// Reads one capability. Returns <see cref="CapabilityValue.Absent"/> when the
    /// device declines to answer, which is a legitimate outcome and not an error: a
    /// detached boom microphone simply does not respond to its level read.
    /// </summary>
    Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one capability. Returns false when the device refuses the value; the
    /// caller restores the previous reading and surfaces the refusal at the control.
    /// Validation of ranges and forbidden values happens inside the driver, where the
    /// protocol-specific ceilings are known.
    /// </summary>
    Task<bool> WriteAsync(string capabilityId, CapabilityValue value, CancellationToken cancellationToken = default);
}

/// <summary>
/// A connection that can say when something moved on the device, instead of waiting to be
/// asked.
///
/// Optional, because most of what a headset knows has to be polled: its settings live
/// behind a request-and-reply protocol that volunteers nothing. But the controls under the
/// wearer's fingers are ordinary HID — the volume wheel and the mute button announce
/// themselves the instant they are used, on interfaces that have nothing to do with the
/// vendor protocol.
///
/// Ignoring that is what made the panel lag ten seconds behind the earcup: the value had
/// changed, the device had said so, and the application was waiting for its next timer to
/// come round and ask. A driver that implements this turns the wait into a refresh of the
/// handful of readings the event could have affected.
/// </summary>
public interface IDeviceNotifier
{
    /// <summary>
    /// Raised when the device reports activity of its own accord. Carries the capability
    /// ids worth re-reading — the event says something happened, not what it now is.
    /// </summary>
    event EventHandler<IReadOnlyList<string>>? Activity;
}
