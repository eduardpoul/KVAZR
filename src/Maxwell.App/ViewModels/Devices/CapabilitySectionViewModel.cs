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
///
/// The device's class reaches one thing here, and only naming: a render endpoint on a
/// microphone is not "sound", it is the headphone socket on its base, and the artboard
/// calls it so.
/// </summary>
public sealed class CapabilitySectionViewModel : IDisposable
{
    private bool _disposed;

    public CapabilitySectionViewModel(
        CapabilityDomain domain,
        IEnumerable<CapabilityViewModel> items,
        DeviceClass owner = DeviceClass.Unknown)
    {
        ArgumentNullException.ThrowIfNull(items);

        Domain = domain;
        Owner = owner;
        Items = [.. items];
    }

    public CapabilityDomain Domain { get; }

    /// <summary>What kind of device this section belongs to. Reaches the title and nothing else.</summary>
    public DeviceClass Owner { get; }

    public ObservableCollection<CapabilityViewModel> Items { get; }

    public string Title => TitleOf(Domain, Owner);

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

    // No "is this section sensitive" any more.
    //
    // It was true when the section held a factory reset or a re-pairing, and it collapsed
    // the whole section behind an expander so those would be dug for rather than stumbled
    // into. What it actually buried was the auto-off timer and the firmware version, which
    // shared a section with the reset and are not dangerous at all — and it took the
    // section's heading with them, so the block did not look like the others on the page.
    // The row that is dangerous still says so and still asks before it acts, which is
    // where that protection belongs.

    /// <summary>
    /// The section names the design uses. Kept here rather than on the enum so the core
    /// stays free of interface language and of any one language at all.
    /// </summary>
    public static string TitleOf(CapabilityDomain domain, DeviceClass owner = DeviceClass.Unknown)
    {
        // Playback on a microphone is the socket on its base, and calling that section
        // "Sound" on a page about a microphone reads as a second, mysterious volume.
        if (domain == CapabilityDomain.Audio && owner == DeviceClass.Microphone)
        {
            return Maxwell.App.Localization.Localizer.Current[
                "domain.audio.microphone",
                Maxwell.App.Localization.Localizer.Current.Say("Headphones in the microphone")];
        }

        return Maxwell.App.Localization.Localizer.Current[
            $"domain.{domain}".ToLowerInvariant(), Written(domain)];
    }

    /// <summary>The source wording, which is what the catalogue is keyed by.</summary>
    private static string Written(CapabilityDomain domain) => domain switch
    {
        // "General" rather than "Power": the charge is drawn in the hero, so what is left
        // here is the device's own particulars — which edition it is, its firmware, when
        // it switches itself off.
        CapabilityDomain.Power => Maxwell.App.Localization.Localizer.Current.Say("General"),
        CapabilityDomain.Link => Maxwell.App.Localization.Localizer.Current.Say("Connection"),
        CapabilityDomain.Audio => Maxwell.App.Localization.Localizer.Current.Say("Sound"),
        CapabilityDomain.Microphone => Maxwell.App.Localization.Localizer.Current.Say("Microphone"),
        CapabilityDomain.Mix => Maxwell.App.Localization.Localizer.Current.Say("Game and chat"),
        CapabilityDomain.Pointing => Maxwell.App.Localization.Localizer.Current.Say("Pointer"),
        CapabilityDomain.Buttons => Maxwell.App.Localization.Localizer.Current.Say("Buttons"),
        CapabilityDomain.Keys => Maxwell.App.Localization.Localizer.Current.Say("Keys"),
        CapabilityDomain.Light => Maxwell.App.Localization.Localizer.Current.Say("Lighting"),
        CapabilityDomain.Profiles => Maxwell.App.Localization.Localizer.Current.Say("Profiles"),
        CapabilityDomain.Maintenance => Maxwell.App.Localization.Localizer.Current.Say("Power and maintenance"),
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
