namespace Maxwell.Race.Abstractions;

/// <summary>
/// An ear on one HID collection: whatever the device volunteers, as it arrives.
///
/// Separate from <see cref="IHidTransport"/>, which is a request-and-answer channel for a
/// protocol that answers. This is for the other kind of device — the one that says nothing
/// when asked and announces things when they happen. A QuadCast is that: its mute panel
/// and its polar selector are pushed the instant a hand moves them, and reading the same
/// report on demand is refused.
/// </summary>
public interface IHidListener : IDisposable
{
    /// <summary>
    /// The next report, or null if none arrived within the wait. A null is the ordinary
    /// answer — most of the time nothing is happening — and never an error.
    /// </summary>
    byte[]? Listen(TimeSpan wait);
}

/// <summary>
/// Opens an ear on a collection chosen by vendor and usage page.
///
/// By page rather than by path because a path is a machine-specific string that changes
/// when a device is moved to another port, and the thing being asked for is "the channel
/// this maker announces state on", which the page names.
/// </summary>
public interface IHidListenerFactory
{
    /// <summary>Null when nothing on this machine matches, which is not an error.</summary>
    /// <exception cref="IOException">It matched and would not open.</exception>
    IHidListener? Open(ushort vendorId, ushort usagePage);
}
