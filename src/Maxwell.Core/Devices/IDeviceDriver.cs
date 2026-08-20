namespace Maxwell.Core.Devices;

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
