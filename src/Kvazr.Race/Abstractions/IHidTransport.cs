namespace Kvazr.Race.Abstractions;

/// <summary>
/// An open byte channel to one HID interface. Deliberately tiny: everything above
/// this line is platform-neutral RACE logic, everything below is per-OS plumbing
/// (hid.dll on Windows, /dev/hidraw on Linux, IOKit on macOS).
/// </summary>
public interface IHidTransport : IDisposable
{
    MaxwellInterface Descriptor { get; }

    /// <summary>Push an output report. Returns false when the write is rejected.</summary>
    bool Write(byte[] report);

    /// <summary>
    /// Fetch the input report with the given ID. This is a polled control transfer,
    /// not an interrupt read — the Maxwell does not push RACE replies, and a blocking
    /// interrupt read on the same handle starves the control channel.
    /// Returns null when nothing readable came back.
    /// </summary>
    byte[]? ReadInputReport(byte reportId);
}

/// <summary>Discovers Maxwell HID interfaces present on the machine.</summary>
/// <summary>
/// One HID interface, whoever made it.
///
/// The usage page is what matters here rather than the vendor id: a keyboard speaking VIA
/// answers on 0xFF60 whatever brand is printed on it, and that is the whole reason one
/// implementation can serve hundreds of models.
/// </summary>
/// <param name="Path">What the host needs to reopen the device.</param>
public sealed record HidInterface(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,

    /// <summary>
    /// Size of a feature report, or zero where the collection has none.
    ///
    /// Worth its own field because it is a third channel rather than a detail of the
    /// other two: a device can carry its whole vendor protocol on feature reports and
    /// show nothing at all on the input and output pipes. The HyperX QuadCast is one —
    /// its lighting is written as 64-byte feature reports and its input reports carry
    /// only the mute button.
    /// </summary>
    int FeatureReportLength,
    string ProductName);

public interface IHidEnumerator
{
    IReadOnlyList<MaxwellInterface> Enumerate();

    /// <summary>
    /// Every HID interface on the machine. Vendor-neutral, because the class protocols —
    /// VIA, HID++ — are found by usage page rather than by who made the device.
    /// </summary>
    IReadOnlyList<HidInterface> EnumerateAll();

    /// <summary>RACE-capable interfaces only, dongles before cabled headsets.</summary>
    IReadOnlyList<MaxwellInterface> EnumerateRaceInterfaces();
}

/// <summary>Opens a transport for a discovered interface.</summary>
public interface IHidTransportFactory
{
    /// <summary>Opens the interface for read and write.</summary>
    /// <exception cref="IOException">The device could not be opened.</exception>
    IHidTransport Open(MaxwellInterface iface);
}

/// <summary>
/// Notifies when Maxwell hardware appears or disappears, so the app can react to a
/// cable being plugged in instead of making the user press Refresh.
/// </summary>
public interface IHidDeviceWatcher : IDisposable
{
    event EventHandler? DevicesChanged;

    void Start();
}

/// <summary>
/// One report from one mouse, as Raw Input delivers it.
///
/// The device handle rather than a path, because that is what the report carries and
/// what makes two mice on one desk tellable apart. Everything else is what a mouse
/// actually did: which of its buttons changed, how far the wheel turned, how far it
/// moved.
/// </summary>
public readonly record struct RawMouseReport(IntPtr Device, ushort Buttons, short Wheel, int DeltaX, int DeltaY);
