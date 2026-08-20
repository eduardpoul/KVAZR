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

    public bool HasItems => Items.Count > 0;

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
