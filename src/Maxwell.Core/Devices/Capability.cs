namespace Maxwell.Core.Devices;

/// <summary>Bounds for a <see cref="CapabilityPrimitive.Range"/> control.</summary>
public sealed record CapabilityRange
{
    public required int Min { get; init; }

    public required int Max { get; init; }

    public int Step { get; init; } = 1;

    /// <summary>Suffix shown next to the number, e.g. "%", "Гц", "мин".</summary>
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
    /// False when a write cannot be undone. Maxwell's gain mirror latches on the first
    /// write and only a factory reset puts it back, so the warning has to come before
    /// the write, not after it.
    /// </summary>
    public bool IsReversible { get; init; } = true;

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

    /// <summary>Standing caution shown with the control, e.g. a damage ceiling.</summary>
    public string? Note { get; init; }

    public bool IsWritable => Access is CapabilityAccess.Write or CapabilityAccess.ReadWrite;

    public bool IsReadable => Access is CapabilityAccess.Read or CapabilityAccess.ReadWrite;
}
