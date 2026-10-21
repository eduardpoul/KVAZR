namespace Kvazr.Core.Devices;

/// <summary>
/// The single owner of one device.
///
/// Every read and write for that device goes through here. RACE, HID++ and most vendor
/// protocols answer the wrong question when requests overlap, so access is serialised
/// behind one gate — this is the rule the whole device layer exists to enforce, and no
/// view model may hold a connection of its own.
/// </summary>
public interface IDeviceSession
{
    string DeviceKey { get; }

    /// <summary>The latest picture. Never null; replaced wholesale, never mutated.</summary>
    DeviceSnapshot Snapshot { get; }

    event EventHandler<DeviceSnapshot>? SnapshotChanged;

    /// <summary>Reads the named capabilities. Unknown or unreadable ids are skipped.</summary>
    Task RefreshAsync(IReadOnlyList<string> capabilityIds, CancellationToken cancellationToken = default);

    /// <summary>Reads everything the device declares. Used when a page opens, not on a timer.</summary>
    Task RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks whether the device is answering, and settles the claim either way.
    ///
    /// Apart from the reads because it is the one question a read cannot answer: an empty
    /// value is not evidence of a dead device — half the rows on a microphone are
    /// legitimately empty — and a device that has gone quiet has nothing worth reading at
    /// all. Cheap, and cheapest in the case that matters: a device that is there answers
    /// at once.
    /// </summary>
    Task ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one capability and reports whether the device took it. A refusal is not an
    /// exception: the previous reading is restored and the failure is left on the
    /// capability for the control to show.
    /// </summary>
    Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default);
}
