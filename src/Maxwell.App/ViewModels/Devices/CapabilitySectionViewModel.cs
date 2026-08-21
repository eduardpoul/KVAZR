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
        CapabilityDomain.Power => "Питание",
        CapabilityDomain.Link => "Связь",
        CapabilityDomain.Audio => "Звук",
        CapabilityDomain.Microphone => "Микрофон",
        CapabilityDomain.Mix => "Игра и чат",
        CapabilityDomain.Pointing => "Указатель",
        CapabilityDomain.Buttons => "Кнопки",
        CapabilityDomain.Keys => "Клавиши",
        CapabilityDomain.Light => "Подсветка",
        CapabilityDomain.Profiles => "Профили",
        CapabilityDomain.Maintenance => "Дополнительно",
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
