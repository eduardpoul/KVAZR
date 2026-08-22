namespace Maxwell.Core.Devices;

/// <summary>
/// What kind of thing the device is. Drives nothing in the protocol layer — it exists
/// so the shell can pick an icon and a default image, and so the device list can be
/// grouped in a way a person recognises.
/// </summary>
public enum DeviceClass
{
    /// <summary>
    /// Not a failure and not a placeholder. A device that says nothing about itself
    /// still gets a row, a page and an icon — the design draws that case on purpose,
    /// because it is what most of the world looks like before anyone teaches us about it.
    /// </summary>
    Unknown,

    Headset,
    Earbuds,
    Microphone,
    Speaker,
    Mouse,
    Keyboard,
    Gamepad,

    /// <summary>
    /// A wireless receiver, shown as itself because at the system level that is all
    /// there is to see. A Logitech receiver arrives as one container serving both a
    /// keyboard and a mouse; only the vendor protocol can say what is actually paired to
    /// it, so until a driver at that tier speaks up, "a receiver" is the honest answer.
    /// </summary>
    Receiver,
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

    /// <summary>
    /// The same channel without its product id, e.g. "USB-C". The artboards use this in
    /// every list and the full label only in a device's own hero: the id distinguishes
    /// two ways into one device, which is a question the page it belongs to can raise
    /// and a list of eight devices cannot.
    /// </summary>
    public string ShortLabel
    {
        get
        {
            int at = Label.IndexOf(" 0x", StringComparison.Ordinal);
            return at > 0 ? Label[..at] : Label;
        }
    }

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

    /// <summary>
    /// Catalogue key for this model, as a slug like "audeze-maxwell" — not a file name.
    /// It resolves in three steps: a curated picture shipped with the build, then the
    /// silhouette for <see cref="Class"/>, then whatever the user picked themselves.
    ///
    /// Deliberately not the USB ids. Bluetooth devices have none, and a model that ships
    /// under several product ids is still one model to the person holding it.
    /// </summary>
    public string? ImageKey { get; init; }

    /// <summary>
    /// Everything that identifies this device to the operating system: "usb:3329:4b1e",
    /// "bt:034275a08236", "container:{guid}".
    ///
    /// This exists so the same headset found twice does not appear twice. The system
    /// driver finds every device on the machine and a vendor driver finds one of them
    /// properly; without a way to tell that those two are the same object, the Maxwell
    /// would sit in the list beside itself — once with sixteen capabilities and once
    /// with a battery reading.
    /// </summary>
    public IReadOnlySet<string> HardwareIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<TransportInfo> Transports { get; init; } = [];

    /// <summary>
    /// The name as a person would write it, maker included exactly once.
    ///
    /// Two things make this less obvious than it looks. The maker is often unknown, since
    /// Windows reports "(Standard system devices)" for most peripherals and the system
    /// driver refuses to pass that on — so the model has to be able to stand alone rather
    /// than trailing an empty vendor and a stray space. And the model frequently already
    /// carries the brand, whether it came from a catalogue entry or from what the device
    /// calls itself on the bus, which is how "Flydigi Flydigi Charger Dock 2" happens.
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Vendor)
        || Model.StartsWith(Vendor, StringComparison.CurrentCultureIgnoreCase)
            ? Model
            : $"{Vendor} {Model}";

    /// <summary>The channel that answers configuration commands, when one is present.</summary>
    public TransportInfo? ControlTransport =>
        Transports.FirstOrDefault(t => t.IsPresent && t.CarriesControl);
}
