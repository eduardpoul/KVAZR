using System.Collections.ObjectModel;
using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>
/// One section of a device page.
///
/// Sections come from the capability domain and their order comes from the enum, not
/// from the driver. That is the whole reason a mouse and a headset read as one
/// application: neither page was written, both were assembled the same way, and an
/// empty section simply does not appear.
/// </summary>
public sealed class CapabilitySectionViewModel : IDisposable
{
    private bool _disposed;

    public CapabilitySectionViewModel(CapabilityDomain domain, IEnumerable<CapabilityViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Domain = domain;
        Items = [.. items];
    }

    public CapabilityDomain Domain { get; }

    public ObservableCollection<CapabilityViewModel> Items { get; }

    public string Title => TitleOf(Domain);

    /// <summary>
    /// The label above the section's card. Upper case because the artboard has it that
    /// way, and it has it that way because a section label is a signpost rather than a
    /// heading — it should be findable while scanning the page and invisible while
    /// reading a row.
    /// </summary>
    public string Heading => Title.ToUpperInvariant();

    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// True when anything in the section can actually be reached right now. A headset on
    /// its dongle has an Audio section whose every control is out of reach until a cable
    /// goes in, and artboard 5b greys the whole section in the navigation rather than
    /// letting somebody walk into a page of dead controls.
    /// </summary>
    public bool IsReachable => Items.Any(i => i.IsAvailable);

    /// <summary>
    /// Why the section is out of reach, short enough to sit in the navigation beside its
    /// name. Empty when it is reachable. Taken from the capabilities themselves rather
    /// than written per section: they already know what they need, and a second copy of
    /// that knowledge would be a second thing to keep true.
    /// </summary>
    public string Reason =>
        IsReachable
            ? string.Empty
            : Items.Where(i => !i.IsAvailable)
                .Select(i => i.Descriptor.RequirementHint)
                .FirstOrDefault(r => !string.IsNullOrEmpty(r)) ?? string.Empty;

    /// <summary>
    /// True when the section holds something buried on purpose — a factory reset, a
    /// re-pairing. Such a section is collapsed behind an expander, because the design
    /// asks for these to be dug for rather than stumbled into.
    /// </summary>
    public bool IsSensitive => Items.Any(i => i.Descriptor.Risk == CapabilityRisk.Hidden);

    /// <summary>
    /// The section names the design uses. Kept here rather than on the enum so the core
    /// stays free of interface language and of any one language at all.
    /// </summary>
    public static string TitleOf(CapabilityDomain domain) => domain switch
    {
        // "Общее" rather than "Питание": the charge is drawn in the hero, so what is left
        // here is the device's own particulars — which edition it is, its firmware, when
        // it switches itself off.
        CapabilityDomain.Power => "Общее",
        CapabilityDomain.Link => "Связь",
        CapabilityDomain.Audio => "Звук",
        CapabilityDomain.Microphone => "Микрофон",
        CapabilityDomain.Mix => "Игра и чат",
        CapabilityDomain.Pointing => "Указатель",
        CapabilityDomain.Buttons => "Кнопки",
        CapabilityDomain.Keys => "Клавиши",
        CapabilityDomain.Light => "Подсветка",
        CapabilityDomain.Profiles => "Профили",
        CapabilityDomain.Maintenance => "Питание и обслуживание",
        _ => domain.ToString(),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (CapabilityViewModel item in Items) item.Dispose();
        Items.Clear();
    }
}
