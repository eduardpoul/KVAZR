namespace Maxwell.Core.Devices;

/// <summary>
/// What kind of thing the device is. Drives nothing in the protocol layer — it exists
/// so the shell can pick an icon and a default image, and so the device list can be
/// grouped in a way a person recognises.
/// </summary>
public enum DeviceClass
{
    Unknown,
    Headset,
    Mouse,
    Keyboard,
}

/// <summary>Which physical link a channel to the device runs over.</summary>
public enum TransportKind
{
    Unknown,
    UsbCable,
    Dongle,
    Bluetooth,
}

/// <summary>
/// One channel to a device. A device can have several at once and they are not
/// interchangeable: the Maxwell carries audio over its dongle while answering
/// configuration only over the cable, and the device page shows both rows rather
/// than making the user pick one.
/// </summary>
public sealed record TransportInfo
{
    public required TransportKind Kind { get; init; }

    /// <summary>Shown verbatim in the UI, e.g. "USB-C 0x4B1E".</summary>
    public required string Label { get; init; }

    public bool IsPresent { get; init; }

    /// <summary>True when settings can be read and written over this channel.</summary>
    public bool CarriesControl { get; init; }

    /// <summary>True when audio flows over this channel.</summary>
    public bool CarriesAudio { get; init; }
}

/// <summary>
/// Everything about a device that does not change while it is plugged in. The
/// <see cref="Key"/> has to survive a reconnect and an application restart, because
/// profiles, the confirmed-capability set and the "primary device" choice are all
/// stored against it.
/// </summary>
public sealed record DeviceInfo
{
    /// <summary>Stable identity, e.g. "audeze.race:3329-4b1e". Used as a settings key.</summary>
    public required string Key { get; init; }

    /// <summary>Which driver owns this device, e.g. "audeze.race".</summary>
    public required string DriverId { get; init; }

    public required string Vendor { get; init; }

    public required string Model { get; init; }

    public DeviceClass Class { get; init; } = DeviceClass.Unknown;

    /// <summary>Name of the bundled image asset, without a path or extension.</summary>
    public string? ImageKey { get; init; }

    public IReadOnlyList<TransportInfo> Transports { get; init; } = [];

    public string DisplayName => $"{Vendor} {Model}";

    /// <summary>The channel that answers configuration commands, when one is present.</summary>
    public TransportInfo? ControlTransport =>
        Transports.FirstOrDefault(t => t.IsPresent && t.CarriesControl);
}
