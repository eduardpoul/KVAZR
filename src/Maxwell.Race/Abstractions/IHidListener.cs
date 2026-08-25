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
/// A vendor register that answers questions: write a command, read the reply out of the
/// same place.
///
/// The other half of a device like the QuadCast. Its status channel announces changes and
/// refuses to be asked; this one is asked and volunteers nothing. A page needs both —
/// one to fill itself when it opens, the other to stay right afterwards.
/// </summary>
public interface IHidVendorPipe : IDisposable
{
    /// <summary>
    /// Writes a command and returns the first answer that is not the command echoing
    /// back, or null if nothing came within the wait.
    ///
    /// The echo is not an error and not an answer: the register holds what was written to
    /// it until the device replaces it, so reading immediately hands back the question.
    /// </summary>
    byte[]? Ask(byte[] command, TimeSpan within);
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

    /// <summary>
    /// Opens a register that can be asked. Null when there is none, or when it will not
    /// open for writing — which is a real possibility and never fatal, since everything
    /// a pipe supplies can also be waited for.
    /// </summary>
    IHidVendorPipe? OpenPipe(ushort vendorId, ushort usagePage);
}
