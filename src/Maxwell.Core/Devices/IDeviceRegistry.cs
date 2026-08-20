namespace Maxwell.Core.Devices;

/// <summary>
/// Everything the application knows about attached hardware.
///
/// Runs discovery across every registered driver and owns one
/// <see cref="IDeviceSession"/> per device. Devices that go away are kept in the list
/// rather than forgotten — the list shows them dimmed with the time they were last
/// seen, which is what a person expects from a keyboard that is simply in a bag.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Present devices first, then the ones last seen earlier.</summary>
    IReadOnlyList<DeviceSnapshot> Devices { get; }

    /// <summary>Raised when a device appears, disappears, or any of them changes.</summary>
    event EventHandler? DevicesChanged;

    IDeviceSession? Session(string deviceKey);

    /// <summary>
    /// Re-runs discovery and reconciles the list. Cheap enough to call on a device
    /// arrival notification from the operating system.
    /// </summary>
    Task DiscoverAsync(CancellationToken cancellationToken = default);
}
