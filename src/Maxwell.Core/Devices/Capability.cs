namespace Maxwell.Core.Devices;

/// <summary>Bounds for a <see cref="CapabilityPrimitive.Range"/> control.</summary>
public sealed record CapabilityRange
{
    public required int Min { get; init; }

    public required int Max { get; init; }

    public int Step { get; init; } = 1;

    /// <summary>Suffix shown next to the number, e.g. "%", "Hz", "min".</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Where the slider snaps and where a centre notch is drawn. Earcup balance is
    /// centred on 128 and ChatMix is notched at 0, 10 and 20 — both read wrong as a
    /// plain slider because the meaningful position is not the middle of the track.
    /// </summary>
    public IReadOnlyList<int> Detents { get; init; } = [];

    public RangeSource Source { get; init; } = RangeSource.Static;

    /// <summary>
    /// Capability whose reading supplies <see cref="Max"/> when <see cref="Source"/>
    /// is <see cref="RangeSource.Device"/>.
    /// </summary>
    public string? MaxFromCapability { get; init; }
}

/// <summary>One option of a <see cref="CapabilityPrimitive.Choice"/> control.</summary>
public sealed record CapabilityChoice(int Index, string Label)
{
    /// <summary>Secondary line, for options that need a word of explanation.</summary>
    public string? Hint { get; init; }

    /// <summary>
    /// A name for a drawing of this option, where one exists and helps.
    ///
    /// For the cases where the hardware itself is labelled with a picture rather than a
    /// word: the selector on a microphone's back is four rings, and somebody comparing the
    /// screen with the device is matching shapes, not reading. The interface owns what the
    /// name draws; a driver only says which one applies, and a name it does not recognise
    /// draws nothing rather than breaking the row.
    /// </summary>
    public string? Glyph { get; init; }
}

/// <summary>
/// One part of a composite primitive: a channel of a <see cref="CapabilityPrimitive.RangeGroup"/>,
/// a band of a <see cref="CapabilityPrimitive.Curve"/>, a zone of a
/// <see cref="CapabilityPrimitive.Lighting"/> rig, a mouse button, a key position, an
/// onboard profile slot, a host slot.
///
/// Kept apart from <see cref="CapabilityChoice"/> on purpose, and the distinction is
/// worth the second type: choices are alternatives, of which exactly one holds at a
/// time, while members are parts that each carry their own reading simultaneously. A
/// ButtonMap needs both at once — its members are the buttons, its choices are the
/// actions any of them can be given — so folding the two into one list would have left
/// that capability undescribable.
/// </summary>
/// <param name="Index">
/// The member's position in the reading vector, numbered from zero and dense. A driver
/// that skips a number would silently shift every member after it against the values it
/// reported, so the catalog tests enforce it.
/// </param>
/// <param name="Label">What the part is called on the device: "Left", "1 kHz", "Button 4".</param>
public sealed record CapabilityMember(int Index, string Label)
{
    /// <summary>Secondary line: what the slot holds, or why it is empty.</summary>
    public string? Hint { get; init; }

    /// <summary>
    /// Bounds for this member alone. Null falls back to the capability's own
    /// <see cref="Capability.Range"/>, which is what a curve wants — ten bands sharing
    /// one gain scale — while a sensor that resolves X and Y separately wants its own.
    /// </summary>
    public CapabilityRange? Range { get; init; }

    /// <summary>
    /// The slot exists but holds nothing: a free host slot, an onboard profile never
    /// written. Drawn as an invitation to fill it rather than as something to select,
    /// because selecting an empty profile would apply nothing and look like a failure.
    /// </summary>
    public bool IsEmpty { get; init; }
}

/// <summary>
/// What a device can do, described so the shell can draw it without knowing which
/// device it is. Supplied by the driver once per connection and immutable thereafter;
/// the changing part lives in <see cref="CapabilityState"/>.
///
/// The attribute set is not decoration. Each field exists because leaving it out
/// produced either a lying interface or a dangerous one — see docs/CAPABILITIES.md §6.
/// </summary>
public sealed record Capability
{
    /// <summary>Catalog id, e.g. "mix.chat". See <see cref="CapabilityIds"/>.</summary>
    public required string Id { get; init; }

    public required CapabilityDomain Domain { get; init; }

    public required CapabilityPrimitive Primitive { get; init; }

    /// <summary>Label shown on the control row.</summary>
    public required string Title { get; init; }

    /// <summary>Second line under the title: units, caveats, where the value can change.</summary>
    public string? Subtitle { get; init; }

    public CapabilityAccess Access { get; init; } = CapabilityAccess.ReadWrite;

    /// <summary>
    /// What the driver claims before any write has been attempted. The effective value
    /// is <see cref="CapabilityState.Status"/>, which can be upgraded at runtime.
    /// </summary>
    public CapabilityStatus DeclaredStatus { get; init; } = CapabilityStatus.Verified;

    public CapabilityRisk Risk { get; init; } = CapabilityRisk.Safe;

    /// <summary>
    /// The value can change without the application doing anything: the wheel on the
    /// earcup moves ChatMix, a button on a mouse cycles DPI. Such capabilities have to
    /// be polled and the UI has to yield to the device rather than assume it is the
    /// only writer.
    /// </summary>
    public bool IsVolatile { get; init; }

    /// <summary>
    /// The value moves many times a second and is worth reading only while somebody is
    /// watching it: an input level, a stick position, a live temperature.
    ///
    /// Deliberately not a faster <see cref="IsVolatile"/>. Volatile means "poll it so the
    /// page does not disagree with the hardware"; live means "this is a measurement, and
    /// between glances there is nothing to remember". So these are left out of ordinary
    /// sweeps entirely — including the full read a device gets when it is first seen —
    /// and read only by the fast lane, only for the device whose page is open.
    ///
    /// The exclusion is not an optimisation. Reading a microphone's level means opening
    /// the microphone, which lights the in-use indicator in the taskbar; a full sweep that
    /// took one sample of it would flash that indicator for every device the application
    /// discovers, which is not a thing anybody should have to explain.
    /// </summary>
    public bool IsLive { get; init; }

    /// <summary>
    /// The id of a switch that has to be on for this control to do anything: the sound
    /// over its volume, the lighting over its brightness.
    ///
    /// Declared by the driver rather than worked out by the page, because only the driver
    /// knows whether two capabilities are one control split in two or two independent
    /// ones. A page that guessed by matching names would eventually grey a slider that
    /// works perfectly well.
    ///
    /// It says nothing about order. The interface draws the switch first because a
    /// condition belongs before the thing it governs, but that is a decision about
    /// reading, and this is a fact about the hardware.
    /// </summary>
    public string? EnabledBy { get; init; }

    /// <summary>
    /// False when a write cannot be undone. Maxwell's gain mirror latches on the first
    /// write and only a factory reset puts it back, so the warning has to come before
    /// the write, not after it.
    /// </summary>
    public bool IsReversible { get; init; } = true;

    /// <summary>
    /// A heading of this row's own, when the domain is not where it belongs on screen.
    ///
    /// Sections are domains, which is right nearly always: a volume and a mute are both
    /// sound whoever supplies them. What it does not fit is a row that only exists
    /// because a particular device was recognised — a polar selector, a lighting mode —
    /// which shares a domain with the baseline rows and would otherwise be filed among
    /// them as though Windows had known about it all along.
    ///
    /// The label is the driver's to choose and it is drawn as given. Nothing here says
    /// which tier a row came from, because that is a fact about this application's
    /// insides and not about the user's microphone.
    /// </summary>
    public string? Section { get; init; }

    public RequirementKind Requirement { get; init; } = RequirementKind.None;

    /// <summary>
    /// The sentence shown beside the disabled control when <see cref="Requirement"/> is
    /// unmet. The driver writes it because the driver is what knows why. Explaining the
    /// reason next to the control, rather than in a banner at the top of the page, is a
    /// rule of the design system.
    /// </summary>
    public string? RequirementHint { get; init; }

    public CapabilityRange? Range { get; init; }

    public IReadOnlyList<CapabilityChoice> Choices { get; init; } = [];

    /// <summary>
    /// A capability whose reading is this one's list of options, for a choice whose
    /// options are themselves something the device stores and lets us change.
    ///
    /// The mouse case is the reason it exists: the five resolutions a Logitech keeps on
    /// board are the options of its resolution row and are also a row of their own,
    /// editable. Declared once at connection the two drifted apart the moment one was
    /// edited. The reading is expected to be a vector of numbers, and each becomes an
    /// option labelled with <see cref="ChoiceUnit"/>.
    /// </summary>
    public string? ChoicesFromCapability { get; init; }

    /// <summary>What to write after a number that became an option. "DPI", "Hz".</summary>
    public string? ChoiceUnit { get; init; }

    /// <summary>
    /// The parts of a composite primitive, in the order they are drawn. Empty for the
    /// scalar ones, which are their own single part.
    /// </summary>
    public IReadOnlyList<CapabilityMember> Members { get; init; } = [];

    /// <summary>
    /// How many layers a <see cref="CapabilityPrimitive.KeyMap"/> holds. One elsewhere.
    ///
    /// A layer is not a member: members are the key positions, and every layer has the
    /// same ones. The count sits here rather than being inferred from the reading
    /// because a keyboard that has not answered yet still has to draw its layer tabs.
    /// </summary>
    public int LayerCount { get; init; } = 1;

    /// <summary>
    /// How many key positions sit in a row, for the primitives drawn as a physical
    /// picture. Zero lets them flow.
    ///
    /// Geometry belongs to the descriptor because it is a fact about the hardware, and
    /// letting the control guess it from the member count would put the row breaks of a
    /// 75-key board wherever the panel happened to be wide.
    /// </summary>
    public int GridColumns { get; init; }

    /// <summary>Standing caution shown with the control, e.g. a damage ceiling.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Read for another row's sake, and never shown as one of its own.
    ///
    /// The sidetone ceiling is the case: the level slider takes its maximum from it, so it
    /// has to be read and kept — but printing "Sidetone ceiling  31" under a slider that
    /// already reads "9 / 31" is the same number twice, and the second one looks like a
    /// setting because it is sitting in a list of settings.
    /// </summary>
    public bool IsSupporting { get; init; }

    /// <summary>
    /// Known, reachable, and deliberately not offered.
    ///
    /// Different from <see cref="CapabilityStatus.Unavailable"/>, which says we have no
    /// way to invoke the function: this says we have one and are not handing it over yet.
    /// The factory reset is the case it exists for — the command is in the protocol notes,
    /// it has never been run against this headset, and running it wipes the owner's
    /// settings including a channel balance that was corrected by hand. The control stays
    /// on the page, so the device is described honestly; it just cannot be pressed.
    /// </summary>
    public bool IsWithheld { get; init; }

    public bool IsWritable => Access is CapabilityAccess.Write or CapabilityAccess.ReadWrite;

    public bool IsReadable => Access is CapabilityAccess.Read or CapabilityAccess.ReadWrite;
}
