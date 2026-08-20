namespace Maxwell.Core.Devices;

/// <summary>
/// How reachable one device is right now.
///
/// Deliberately per-device, unlike the older application-wide state it replaces:
/// with several devices in the list there is no single answer, and "nothing is
/// plugged in" is simply an empty registry rather than a state a device can be in.
/// </summary>
public enum DeviceAvailability
{
    /// <summary>Known from a previous session, not present now. Shown dimmed, with the
    /// time of the last reading instead of zeroes.</summary>
    Offline,

    /// <summary>
    /// Present, but no channel that answers commands. The Maxwell sits here whenever
    /// only its dongle is connected: audio flows, every setting is unreachable. This
    /// is an ordinary mode of use, not a fault, and must never be drawn as an error.
    /// </summary>
    MonitorOnly,

    /// <summary>Present with a control channel. Everything the device offers works.</summary>
    Active,
}

/// <summary>
/// An immutable picture of one device: what it is, how reachable it is, and the
/// current state of every capability it declares. Replaced wholesale on each update
/// so that consumers never observe a half-applied change.
/// </summary>
public sealed record DeviceSnapshot
{
    public required DeviceInfo Info { get; init; }

    public required DeviceAvailability Availability { get; init; }

    public IReadOnlyDictionary<string, CapabilityState> Capabilities { get; init; } =
        new Dictionary<string, CapabilityState>();

    /// <summary>When the last successful reading came in. Shown as "обновлено 14:41".</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>When the device was last present, for the dimmed row in the device list.</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    public bool IsPresent => Availability != DeviceAvailability.Offline;

    /// <summary>True when settings can be changed, not merely observed.</summary>
    public bool CanControl => Availability == DeviceAvailability.Active;

    public CapabilityState? Get(string capabilityId) =>
        Capabilities.GetValueOrDefault(capabilityId);

    public bool Has(string capabilityId) => Capabilities.ContainsKey(capabilityId);

    /// <summary>
    /// Battery percent, or null when the device has no battery or has not reported one
    /// yet. Null is not zero: a flat battery and an unread battery look identical in an
    /// int and completely different to a person.
    /// </summary>
    public int? BatteryPercent =>
        Get(CapabilityIds.PowerBattery)?.Value is NumberValue n ? n.Value : null;

    public bool IsCharging =>
        Get(CapabilityIds.PowerCharging)?.Value.AsFlag() ?? false;

    /// <summary>
    /// Capabilities of one section, in declaration order. The shell asks domain by
    /// domain in a fixed order and skips whatever comes back empty — that is what makes
    /// a mouse and a headset lay out the same way without either page being written.
    /// </summary>
    public IReadOnlyList<CapabilityState> InDomain(CapabilityDomain domain) =>
        [.. Capabilities.Values.Where(c => c.Descriptor.Domain == domain)];

    /// <summary>Sections that have anything in them, in enum order.</summary>
    public IReadOnlyList<CapabilityDomain> Domains() =>
        [.. Capabilities.Values
            .Select(c => c.Descriptor.Domain)
            .Distinct()
            .OrderBy(d => (int)d)];
}
