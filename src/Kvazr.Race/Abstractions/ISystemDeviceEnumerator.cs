namespace Kvazr.Race.Abstractions;

/// <summary>Which kind of link a system device node sits behind.</summary>
public enum SystemDeviceLink
{
    Unknown,
    Usb,
    Bluetooth,
    BluetoothLe,
}

/// <summary>
/// One device node exactly as the operating system describes it, with nothing inferred.
///
/// A physical device shows up as several of these — a mouse arrives as a HID node and a
/// USB node, a headset as a Hands-Free node and a couple of profile nodes — which is why
/// <see cref="ContainerId"/> matters more than any other field here. Windows already
/// works out which nodes belong to one piece of hardware and stamps them with a shared
/// container id; grouping on it is far more reliable than matching product names, which
/// is what the Bluetooth monitor had to do when it only ever looked for one headset.
/// </summary>
public sealed record SystemDeviceNode
{
    public required string InstanceId { get; init; }

    /// <summary>The physical device this node belongs to, assigned by Windows.</summary>
    public required Guid ContainerId { get; init; }

    public required SystemDeviceLink Link { get; init; }

    /// <summary>
    /// Friendly name. Present on nearly every node and useful on almost none of them:
    /// a Bluetooth headset spreads across a dozen nodes called "Avrcp Transport",
    /// "Hands-Free AG" and "BTNOTIFYR", and a mouse arrives as "USB Input Device".
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// What the hardware calls itself on the bus — the USB iProduct string, or the
    /// Bluetooth friendly name. This is the one a person recognises, and it is the
    /// reason the obvious <see cref="Name"/> is not what gets shown.
    /// </summary>
    public string? BusReportedName { get; init; }

    public string? Manufacturer { get; init; }

    /// <summary>
    /// Whether this node's device is connected right now, where the node has an opinion.
    ///
    /// Bluetooth only, and only on the node named after the device itself: its service
    /// nodes report false while the thing is playing music. Null everywhere else, which
    /// is not the same as false — a USB node does not answer this question because the
    /// question does not apply, and a device with nothing but nulls is present in the
    /// ordinary way.
    /// </summary>
    public bool? IsConnected { get; init; }

    /// <summary>
    /// Setup class of the node. Says whether this is a human interface device, an audio
    /// endpoint or a USB hub, which is how peripherals get told apart from the plumbing
    /// they hang off without keeping a list of names to ignore.
    /// </summary>
    public Guid ClassGuid { get; init; }

    /// <summary>
    /// True for the node that represents the Bluetooth device itself rather than one of
    /// its profiles — instance ids of the form <c>BTHENUM\DEV_034275A08236\…</c>. It is
    /// the only node in the group that carries the name the user gave the device.
    /// </summary>
    public bool IsDeviceRoot { get; init; }

    /// <summary>0..100 where the node reports it, null where it does not.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>
    /// Bluetooth Class of Device. The major and minor fields inside it are what say
    /// "headset" or "gamepad" without anyone having to recognise the model.
    /// </summary>
    public uint? ClassOfDevice { get; init; }

    /// <summary>Bluetooth address as Windows formats it, e.g. "6c2995 1bae07".</summary>
    public string? Address { get; init; }

    public ushort? VendorId { get; init; }

    public ushort? ProductId { get; init; }

    /// <summary>When this node last appeared, which is when the device last connected.</summary>
    /// <summary>
    /// The product revision a Bluetooth device reports about itself, where it reports one.
    ///
    /// Out of the Device Information Service, which is a standard: a low-energy device
    /// states its maker, its identifiers and its revision there, and Windows lays all four
    /// out as device properties. It is the nearest thing a peripheral has to a firmware
    /// version without anybody speaking its protocol.
    /// </summary>
    public ushort? Revision { get; init; }

    public DateTimeOffset? ArrivedAt { get; init; }
}

/// <summary>
/// Lists what the operating system currently sees, with no notion of which devices are
/// interesting. Everything above this decides that.
/// </summary>
public interface ISystemDeviceEnumerator
{
    /// <summary>
    /// Present device nodes across the enumerators that carry peripherals. Cheap enough
    /// to call on a device-change notification; opens nothing and locks nothing.
    /// </summary>
    IReadOnlyList<SystemDeviceNode> Enumerate();
}
