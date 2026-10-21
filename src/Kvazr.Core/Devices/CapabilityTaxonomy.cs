namespace Kvazr.Core.Devices;

/// <summary>
/// The section a capability belongs to. Section order in the UI comes from this enum
/// rather than from the driver, so a headset and a mouse lay out the same way and the
/// shell reads as one application instead of two. Empty sections are hidden.
/// </summary>
public enum CapabilityDomain
{
    Power,
    Link,
    Audio,
    Microphone,
    Mix,

    // Named "Pointing" rather than "Pointer" only because the latter collides with a
    // framework type name and the analyzer rejects it. The section is "Pointer".
    Pointing,

    Buttons,
    Keys,
    Light,
    Profiles,
    Maintenance,
}

/// <summary>
/// How a capability is drawn. This list is deliberately closed: twelve primitives
/// cover every capability found across the standard HID battery pages, Logitech
/// HID++, VIA and Audeze RACE. A driver that needs a thirteenth is a signal that the
/// taxonomy is wrong, not that the driver is special — see docs/CAPABILITIES.md.
/// </summary>
public enum CapabilityPrimitive
{
    Readout,
    Toggle,
    Range,
    RangeGroup,
    Choice,
    Action,
    Curve,
    KeyMap,
    ButtonMap,
    Lighting,
    ProfileSet,
    HostSlots,
}

/// <summary>
/// How much we trust the command behind a capability.
///
/// <see cref="Untested"/> is the interesting one: the byte sequence was lifted from a
/// vendor binary but never written to real hardware. The UI marks it, and the mark
/// disappears permanently after the first successful write — which means the effective
/// status is a runtime value seeded from the driver, not a constant. See
/// <see cref="CapabilityState.Status"/>.
/// </summary>
public enum CapabilityStatus
{
    /// <summary>Read and written on live hardware.</summary>
    Verified,

    /// <summary>Command is known but has never been confirmed on a real device.</summary>
    Untested,

    /// <summary>The device has the function but we have no way to invoke it yet.</summary>
    Unavailable,
}

public enum CapabilityAccess
{
    /// <summary>
    /// Readable only. Not a fallback for "writing is hard" — some functions genuinely
    /// have no writer, such as Maxwell's microphone mute, which moves only when the
    /// boom arm is physically raised. Those render as indicators, never as switches.
    /// </summary>
    Read,

    Write,

    ReadWrite,
}

/// <summary>How much ceremony a write needs before it is allowed through.</summary>
public enum CapabilityRisk
{
    Safe,

    /// <summary>Requires explicit confirmation describing the consequence.</summary>
    Confirm,

    /// <summary>Confirmation plus burial in an expander under "Advanced".</summary>
    Hidden,
}

/// <summary>
/// A precondition on the device that makes a capability temporarily unusable.
///
/// This is not the same as absence: a capability the device does not have is never
/// rendered at all, while an unmet requirement renders the control disabled with the
/// reason beside it. Confusing the two produces a UI full of dead switches.
/// </summary>
public enum RequirementKind
{
    None,

    /// <summary>
    /// Needs the device to be answering. Not a statement about the lead: both the
    /// cable and the dongle carry control, and either can be present while the headset
    /// itself is switched off.
    /// </summary>
    ControlChannel,

    /// <summary>
    /// Needed the detachable boom microphone to be attached, and nothing declares it any
    /// more. The Maxwell was the only device that did, and it turned out not to report
    /// the boom by any route — see the note where its microphone rows used to be.
    /// </summary>
    BoomMicrophone,
}

/// <summary>Where the bounds of a range come from.</summary>
public enum RangeSource
{
    /// <summary>Fixed by the protocol and safe to hard-code.</summary>
    Static,

    /// <summary>
    /// Read out of the device. Maxwell's sidetone ceiling lives in its own register and
    /// the DPI steps of a mouse come from the device too — hard-coding either is a bug
    /// waiting for the next firmware.
    /// </summary>
    Device,
}
