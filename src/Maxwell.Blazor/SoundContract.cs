namespace Maxwell.Blazor;

/// <summary>
/// What the web-rendered section needs to know, and nothing else. The application
/// adapts its capability view models to this; the component never sees them.
/// </summary>
public interface ISoundSectionSource
{
    /// <summary>"ЗВУК" — the section heading, already in the design's case.</summary>
    string Heading { get; }

    IReadOnlyList<ISoundRow> Rows { get; }

    bool IsDark { get; }

    /// <summary>Raised on any change worth redrawing for. May come from any thread.</summary>
    event Action? Changed;
}

/// <summary>One row of the section: a capability flattened to what the HTML draws.</summary>
public interface ISoundRow
{
    string Title { get; }

    string? Subtitle { get; }

    /// <summary>"range" | "choice" | "readout" | "blocked". A string because the web
    /// side switches on it in markup, and an enum would be one more shared type.</summary>
    string Kind { get; }

    string Display { get; }

    double Minimum { get; }

    double Maximum { get; }

    double Value { get; }

    bool HasValue { get; }

    bool CanEdit { get; }

    IReadOnlyList<string> Options { get; }

    int SelectedIndex { get; }

    /// <summary>The line under the control, or null. Same one line the native row shows.</summary>
    string? Hint { get; }

    /// <summary>The amber dot: command known, never confirmed on live hardware.</summary>
    bool IsUntested { get; }

    /// <summary>Why the control is out of reach, shown in its place. Only for "blocked".</summary>
    string Unreachable { get; }

    /// <summary>A slider let go. Routed through the same write path as the native page.</summary>
    void SetValue(double value);

    /// <summary>A dropdown choice. Same path as the native page.</summary>
    void Choose(int index);
}
