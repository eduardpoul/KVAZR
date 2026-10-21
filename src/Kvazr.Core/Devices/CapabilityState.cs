namespace Kvazr.Core.Devices;

/// <summary>
/// A capability as it stands right now: the descriptor plus everything that changes.
///
/// This type is the model behind the design system's eight control states, and the
/// mapping is one to one — if a state cannot be expressed here, the UI cannot draw it:
///
/// <list type="table">
///   <item><term>Normal</term><description>nothing else is set</description></item>
///   <item><term>Unavailable now</term><description><see cref="IsAvailable"/> is false; show <see cref="Capability.RequirementHint"/> beside the control</description></item>
///   <item><term>Untested</term><description><see cref="Status"/> is <see cref="CapabilityStatus.Untested"/></description></item>
///   <item><term>Writing</term><description><see cref="IsWriting"/></description></item>
///   <item><term>Write failed</term><description><see cref="WriteError"/> is set and <see cref="Value"/> already holds the restored previous reading</description></item>
///   <item><term>Changed externally</term><description><see cref="ChangedExternally"/></description></item>
///   <item><term>Irreversible</term><description><c>Descriptor.IsReversible</c> is false; warn before writing</description></item>
///   <item><term>Dangerous</term><description><c>Descriptor.Risk</c></description></item>
/// </list>
/// </summary>
public sealed record CapabilityState
{
    public required Capability Descriptor { get; init; }

    public CapabilityValue Value { get; init; } = CapabilityValue.Absent;

    /// <summary>
    /// Effective status, which can be better than what the driver declared. A capability
    /// starts <see cref="CapabilityStatus.Untested"/> and is promoted to
    /// <see cref="CapabilityStatus.Verified"/> by the first write the device accepts.
    /// The promotion is persisted per device, so the amber mark disappears for good
    /// rather than coming back on the next launch.
    /// </summary>
    public CapabilityStatus Status { get; init; } = CapabilityStatus.Verified;

    /// <summary>
    /// Bounds with device-sourced values filled in. Null when the descriptor has no
    /// range, or when its ceiling has not been read yet — which is not the same as a
    /// range of zero and must not be drawn as one.
    /// </summary>
    public CapabilityRange? Range { get; init; }

    /// <summary>
    /// The options as they stand, for a choice whose list is itself a device reading.
    ///
    /// Same idea as <see cref="Range"/> above and for the same reason: a mouse's stored
    /// resolutions are the options of its resolution row and are also a capability of
    /// their own, editable. Declared once at connection they went stale the moment one was
    /// edited — the row went on offering a step the mouse no longer had. Empty means the
    /// descriptor's own list stands, which is the ordinary case.
    /// </summary>
    public IReadOnlyList<CapabilityChoice> Choices { get; init; } = [];

    /// <summary>What a choice control should actually offer.</summary>
    public IReadOnlyList<CapabilityChoice> Options =>
        Choices.Count > 0 ? Choices : Descriptor.Choices;

    /// <summary>The device meets the descriptor's precondition and can be operated.</summary>
    public bool IsAvailable { get; init; } = true;

    public bool IsWriting { get; init; }

    /// <summary>
    /// Set when the device refused the last write. The error belongs to this control,
    /// not to a page-wide banner, and <see cref="Value"/> has already been rolled back
    /// to the previous reading by the time it appears.
    /// </summary>
    public string? WriteError { get; init; }

    /// <summary>
    /// The last change came from the hardware rather than from us — the earcup wheel,
    /// a DPI button, a host-switch key. Flagged briefly so the UI can acknowledge the
    /// move instead of silently overwriting it.
    /// </summary>
    public bool ChangedExternally { get; init; }

    public string Id => Descriptor.Id;

    /// <summary>
    /// True when the user may operate the control at all.
    ///
    /// Deliberately says nothing about a write already running. Every write to one device
    /// is serialised behind the session's gate, so a second one queues rather than
    /// collides, and refusing it here cost more than it bought: the control greyed out for
    /// the half-second the headset took to answer — a strobe under any hand that moves two
    /// sliders in a row — and an edit made during that half-second was dropped without a
    /// word. The session's own check runs inside the gate, by which point the previous
    /// write has already finished, so it never depended on this term either.
    /// </summary>
    public bool CanWrite =>
        Descriptor.IsWritable
        && !Descriptor.IsWithheld
        && IsAvailable
        && Status != CapabilityStatus.Unavailable;
}
