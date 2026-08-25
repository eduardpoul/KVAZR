using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.Infrastructure;
using Maxwell.App.ViewModels.Capabilities;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Maxwell.App.Imaging;
using Maxwell.Core.Devices;
using Maxwell.Core.Polling;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>
/// A device page, assembled from what the device says it can do.
///
/// There is exactly one of these for every device of every vendor. No page is written
/// per model, per class or per driver — the sections are the capability domains, the
/// rows are the capabilities, and a device that lacks a domain simply has no such
/// section. This is what the whole capability model was for.
/// </summary>
public sealed partial class DevicePageViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceSession _session;
    private readonly IPollingCoordinator? _polling;
    private readonly IConfirmationPrompt? _confirm;

    /// <summary>
    /// The wearer's saved equaliser curves, or null where nothing has any — the design
    /// previews and the tests, which build a page with no application around it.
    /// </summary>
    private readonly CurvePresetLibrary? _presets;

    /// <summary>
    /// What the tray panel carries beyond its usual rows. Null in the previews and the
    /// tests, which have no settings behind them and no panel to fill.
    /// </summary>
    private readonly Platform.TrayExtras? _extras;
    private readonly Dictionary<string, CapabilityViewModel> _byId = new(StringComparer.Ordinal);

    /// <summary>
    /// Capabilities the hero already shows, so they earn no row of their own. They are
    /// still declared, read and polled — the charge in particular feeds the hero, the tray
    /// and the low-battery alert — this only decides what the page draws twice.
    /// </summary>
    private static readonly HashSet<string> DrawnInHero = new(StringComparer.Ordinal)
    {
        CapabilityIds.PowerBattery,
        CapabilityIds.PowerCharging,
    };

    private bool _disposed;

    public DevicePageViewModel(
        IDeviceSession session,
        IPollingCoordinator? polling = null,
        IConfirmationPrompt? confirm = null,
        CurvePresetLibrary? presets = null,
        Platform.TrayExtras? extras = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _polling = polling;
        _confirm = confirm;
        _presets = presets;
        _extras = extras;

        Snapshot = session.Snapshot;
        Rebuild(session.Snapshot);

        _session.SnapshotChanged += OnSnapshotChanged;
    }

    [ObservableProperty]
    public partial DeviceSnapshot Snapshot { get; private set; }

    public ObservableCollection<CapabilitySectionViewModel> Sections { get; } = [];

    /// <summary>
    /// The one section being looked at, or null for the whole device.
    ///
    /// Both are in the artboards and they are different screens: 5a is the device, with
    /// its name as the heading and every section below it; 8a is one section, with the
    /// section as the heading and the device demoted to a subtitle. Picking a section in
    /// the navigation moves between them.
    /// </summary>
    private CapabilitySectionViewModel? _section;

    public CapabilitySectionViewModel? Section
    {
        get => _section;
        set
        {
            if (ReferenceEquals(_section, value)) return;

            _section = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleSections));
            OnPropertyChanged(nameof(Heading));
            OnPropertyChanged(nameof(Subheading));
            OnPropertyChanged(nameof(IsWholeDevice));
        }
    }

    /// <summary>What the page draws: one section, or all of them.</summary>
    public IEnumerable<CapabilitySectionViewModel> VisibleSections =>
        Section is { } one ? [one] : Sections;

    public bool IsWholeDevice => Section is null;

    /// <summary>The device on the whole-device page, the section's name on a section.</summary>
    public string Heading => Section?.Title ?? Title;

    /// <summary>
    /// Under the heading. On a section this is where the device goes, because the reader
    /// has to be able to tell which device's power section they are looking at when two
    /// are attached.
    /// </summary>
    public string Subheading => Section is null ? string.Empty : Title;

    public string DeviceKey => _session.DeviceKey;

    /// <summary>
    /// Through the catalogue like the same name in the device list, and for the same
    /// reason: the stand-ins carry "(mock)" in their model names, and a page headed in
    /// one language above a list that used the other looked like two different devices.
    /// </summary>
    public string Title => Maxwell.App.Localization.Localizer.Current.Say(Snapshot.Info.DisplayName);

    public IReadOnlyList<TransportInfo> Transports => Snapshot.Info.Transports;

    /// <summary>
    /// The channels this device is on right now, each its own chip. Only the live ones:
    /// a transport that is not present is not a fact about the device today, and listing
    /// it beside the ones that are makes the reader work out which is which.
    /// </summary>
    public IReadOnlyList<TransportInfo> LiveTransports =>
        [.. Snapshot.Info.Transports.Where(t => t.IsPresent)];

    /// <summary>
    /// Firmware, spelled out beside the transports as the artboard has it. Absent rather
    /// than "unknown" when the device has not said: an empty space asks no questions,
    /// while the word "unknown" invites the reader to go looking for a version that was
    /// never going to be there.
    /// </summary>
    public string Firmware =>
        Snapshot.Get(CapabilityIds.DeviceFirmware)?.Value is TextValue { Value.Length: > 0 } version
            ? $"{Maxwell.App.Localization.Localizer.Current["device.firmware", "Firmware"]} {version.Value}"
            : string.Empty;

    public bool HasFirmware => Firmware.Length > 0;

    /// <summary>
    /// Everything that was possible to read, for a device that offers nothing to change.
    ///
    /// Artboard 11c: a device connected at the Windows level that answers no known
    /// request still gets a page, and the page says what little is known rather than
    /// being blank. "Connected, and there is nothing here to configure" is something a
    /// person can act on; an empty screen is something they file a bug about.
    /// </summary>
    public IReadOnlyList<DeviceFact> Facts
    {
        get
        {
            var facts = new List<DeviceFact>();

            if (Address is { } address) facts.Add(new DeviceFact(Maxwell.App.Localization.Localizer.Current["fact.address", "Address"], address));

            // Only when it is known. A row reading "not established" is this application
            // reporting on itself, and the reader came here to learn about a device.
            if (Snapshot.Info.Class != DeviceClass.Unknown)
            {
                facts.Add(new DeviceFact(
                    Maxwell.App.Localization.Localizer.Current["fact.class", "Device class"], ClassName));
            }

            if (Snapshot.Info.Maker is { Length: > 0 } maker)
            {
                facts.Add(new DeviceFact(
                    Maxwell.App.Localization.Localizer.Current["fact.maker", "Manufacturer"], maker));
            }

            if (Snapshot.Info.Revision is { Length: > 0 } revision)
            {
                facts.Add(new DeviceFact(
                    Maxwell.App.Localization.Localizer.Current["fact.revision", "Revision"], revision));
            }

            foreach (string id in Snapshot.Info.HardwareIds.Where(i => i.StartsWith("usb:", StringComparison.Ordinal)))
            {
                facts.Add(new DeviceFact(Maxwell.App.Localization.Localizer.Current["fact.usb_id", "USB identifier"], id[4..].ToUpperInvariant()));
            }

            // The same, for the same reason. "Catalogue entry: none" tells the reader
            // that this application has not heard of their device, which is true and is
            // none of their business — the page beneath already shows exactly what is
            // known, with or without a catalogue behind it.
            if (Snapshot.Info.ImageKey is { Length: > 0 } slug)
            {
                facts.Add(new DeviceFact(
                    Maxwell.App.Localization.Localizer.Current["fact.catalogue", "Catalogue entry"], slug));
            }

            return facts;
        }
    }

    /// <summary>
    /// True when the device declared nothing to change. The page then leads with what is
    /// known rather than with an empty column of section headings.
    /// </summary>
    public bool HasNothingToChange => Sections.Count == 0;

    private string? Address =>
        Snapshot.Info.HardwareIds
            .FirstOrDefault(i => i.StartsWith("bt:", StringComparison.Ordinal)) is { } bt
            ? string.Join(':', Enumerable.Range(0, 6).Select(i => bt.Substring(3 + (i * 2), 2).ToUpperInvariant()))
            : null;

    private string ClassName => Snapshot.Info.Class switch
    {
        DeviceClass.Headset => Name("headset", Maxwell.App.Localization.Localizer.Current.Say("Headset")),
        DeviceClass.Earbuds => Name("earbuds", Maxwell.App.Localization.Localizer.Current.Say("Earbuds")),
        DeviceClass.Microphone => Name("microphone", Maxwell.App.Localization.Localizer.Current.Say("Microphone")),
        DeviceClass.Speaker => Name("speaker", Maxwell.App.Localization.Localizer.Current.Say("Speaker")),
        DeviceClass.Mouse => Name("mouse", Maxwell.App.Localization.Localizer.Current.Say("Mouse")),
        DeviceClass.Keyboard => Name("keyboard", Maxwell.App.Localization.Localizer.Current.Say("Keyboard")),
        DeviceClass.Gamepad => Name("gamepad", Maxwell.App.Localization.Localizer.Current.Say("Gamepad")),
        DeviceClass.Receiver => Name("receiver", Maxwell.App.Localization.Localizer.Current.Say("Receiver")),
        _ => Name("undetermined", Maxwell.App.Localization.Localizer.Current.Say("not established")),
    };

    /// <summary>A device class in the language in force; the English is the fallback.</summary>
    private static string Name(string key, string english) =>
        Maxwell.App.Localization.Localizer.Current[$"device.class.{key}", english];

    public string? ImageKey => Snapshot.Info.ImageKey;

    /// <summary>
    /// The curated photograph for this model, or null when there is none — which is the
    /// ordinary case. The tile falls back to <see cref="Glyph"/>, not to an empty box.
    /// </summary>
    public Bitmap? Photo => DeviceImages.Photo(Snapshot.Info.ImageKey);

    /// <summary>The silhouette for this device's class. Every class has one.</summary>
    public string IconKey => Web.DeviceIcon.KeyFor(Snapshot.Info);

    /// <summary>Battery as text. A dash, never a zero, when nothing has been read.</summary>
    public string Battery => Snapshot.BatteryPercent is { } percent ? $"{percent}%" : "—";

    public bool IsCharging => Snapshot.IsCharging;

    /// <summary>The one-word class, for the grey type pill in the hero.</summary>
    public string TypeLabel => ClassName;

    /// <summary>The battery percentage as a fraction, for drawing the glyph's fill.</summary>
    public double BatteryFraction => Math.Clamp((Snapshot.BatteryPercent ?? 0) / 100.0, 0, 1);

    /// <summary>
    /// How the hero draws power. A number when we have one; a struck-through empty cell
    /// with a reason when the class ought to have a battery and this link will not report
    /// it; nothing at all for a mains device that was never going to.
    /// </summary>
    /// <summary>
    /// An offline device draws no battery cell at all. "No charge reading" is a statement
    /// about a live link that will not report one, and putting it on a device that is
    /// simply not here would blame the link for the absence — the notice at the top
    /// already says what is actually going on.
    /// </summary>
    public BatteryDisplay BatteryMode => BatteryReadout.Mode(Snapshot) switch
    {
        BatteryDisplay.Absent when Snapshot.Availability != DeviceAvailability.Active => BatteryDisplay.Mains,
        var mode => mode,
    };

    private bool HasDongle => Snapshot.Info.Transports.Any(t => t is { Kind: TransportKind.Dongle, IsPresent: true });

    /// <summary>Why the battery cell is empty, phrased for the class and the link it is on.</summary>
    public string NoBatteryReason => Snapshot.Info.Class switch
    {
        DeviceClass.Gamepad => Maxwell.App.Localization.Localizer.Current.Say("The dongle reports \"wired, no battery\""),
        // Was: "the dongle does not give a battery level, it appears once paired over
        // Bluetooth". Measured false — this dongle answers the charge all day, and
        // answers it with the headset switched off — so the sentence was a confident
        // claim about the hardware made on the strength of one reading being empty. What
        // is actually true at this moment is that one reading was empty.
        _ when HasDongle => Maxwell.App.Localization.Localizer.Current.Say("No charge has come back on this link yet"),
        _ => Maxwell.App.Localization.Localizer.Current.Say("The charge cannot be read over this link"),
    };

    /// <summary>
    /// The blue/purple hero pill of Turn 15. A device answered by a vendor or class
    /// protocol carries settings past the plain class baseline, and the pill says which
    /// protocol earned them; a device the system alone describes has no pill and reads as
    /// the basic page it is.
    /// </summary>
    public HeroPill? Extended => Snapshot.Info.DriverId switch
    {
        "audeze.race" => new HeroPill(Maxwell.App.Localization.Localizer.Current.Say("Extended · Audeze protocol"), HeroPillKind.Vendor),
        "logitech.hidpp" => new HeroPill(Maxwell.App.Localization.Localizer.Current.Say("Extended · HID++"), HeroPillKind.Hidpp),
        _ => null,
    };

    /// <summary>
    /// The protocol pill beside a section heading — Turn 14's whole organising idea, that
    /// a class page is a stack of protocols. Null on a vendor device, where the hero pill
    /// already names the source and a per-section one would only repeat it.
    /// </summary>
    public ProtocolPill? ProtocolFor(CapabilityDomain domain) => Snapshot.Info.DriverId switch
    {
        "audeze.race" => null,
        "logitech.hidpp" => domain is CapabilityDomain.Mix
            ? null
            : new ProtocolPill("HID++", ProtocolPillKind.Hidpp),
        _ => domain switch
        {
            CapabilityDomain.Audio or CapabilityDomain.Microphone or CapabilityDomain.Mix =>
                new ProtocolPill("Core Audio", ProtocolPillKind.CoreAudio),
            _ => null,
        },
    };

    /// <summary>
    /// The one protocol this page is built on, said once at the top.
    ///
    /// It used to be a pill above every section, which on a microphone meant reading
    /// "Core Audio · applied at once, with no Apply" twice on a page with two sections —
    /// and a page with five would have said it five times. A fact about the whole page
    /// belongs at the top of the page.
    ///
    /// Null when the sections disagree, and then each of them keeps its own pill: the
    /// heading is there to say where a row came from, and a single pill over a page of
    /// two protocols would be wrong about half of it.
    /// </summary>
    public ProtocolPill? PageProtocol
    {
        get
        {
            ProtocolPill[] named =
            [
                .. VisibleSections
                    .Select(s => ProtocolFor(s.Domain))
                    .Where(p => p is not null)
                    .Select(p => p!)
                    .Distinct(),
            ];

            return named.Length == 1 ? named[0] : null;
        }
    }

    /// <summary>The aside that goes with <see cref="PageProtocol"/>, on the same terms.</summary>
    public string? PageNote =>
        PageProtocol is null
            ? null
            : VisibleSections.Select(s => SectionNote(s.Domain)).FirstOrDefault(n => n is not null);

    /// <summary>The grey aside beside a section pill: how its writes reach the device.</summary>
    public string? SectionNote(CapabilityDomain domain) =>
        Snapshot.Info.DriverId != "audeze.race"
            && domain is CapabilityDomain.Audio or CapabilityDomain.Microphone
                ? Maxwell.App.Localization.Localizer.Current.Say("applied at once, with no Apply")
                : null;

    /// <summary>
    /// The honest-limits line at the foot of a class page — what this level cannot read
    /// and where it would come from — so an empty spot reads as a known boundary rather
    /// than a missing feature. Absent on an extended device, which withholds nothing
    /// worth apologising for.
    /// </summary>
    public string? LimitsNote => Snapshot.Info.DriverId switch
    {
        "audeze.race" => null,
        "logitech.hidpp" =>
            Maxwell.App.Localization.Localizer.Current.Say("Mice from other makers will not have these rows: HID++ is Logitech's protocol, and a Razer or a Glorious keeps its name, class and connection."),
        _ => Snapshot.Info.Class switch
        {
            DeviceClass.Headset or DeviceClass.Earbuds =>
                Maxwell.App.Localization.Localizer.Current.Say("The Bluetooth codec, the hardware volume and the charge over a dongle cannot be read at the baseline — only the vendor's protocol gives them."),
            DeviceClass.Microphone =>
                Maxwell.App.Localization.Localizer.Current.Say("The hardware gain is analogue: it works before the converter, and nothing on the machine can read it."),
            DeviceClass.Keyboard =>
                Maxwell.App.Localization.Localizer.Current.Say("Over the dongle the keyboard gives neither charge nor VIA — layout, layers and lighting are edited over the cable."),
            DeviceClass.Gamepad =>
                Maxwell.App.Localization.Localizer.Current.Say("Only genuine Xbox controllers report charge over XInput; button remapping and profiles are the vendor's own road."),
            DeviceClass.Speaker =>
                Maxwell.App.Localization.Localizer.Current.Say("Hardware effects and a speaker's equaliser are the vendor's own road. At the baseline this is the same sound page a headset gets."),
            _ => null,
        },
    };

    /// <summary>
    /// The one line at the top explaining a degraded device. Absent when everything
    /// works, and never phrased as an error — a headset reachable only over its dongle
    /// is being used exactly as intended.
    /// </summary>
    public string? StateNotice => Snapshot switch
    {
        // Answering over Bluetooth, and only over Bluetooth. The Maxwell publishes no HID
        // service on that link, so the short list of capabilities is the truth about it
        // rather than something failing.
        //
        // The condition used to be "no control channel at all", which was written with a
        // headset in mind and was wrong about every class device: a USB microphone has no
        // control channel either — everything it offers comes from Windows — and it was
        // being told, on a cable, that it was on Bluetooth and should try a cable.
        { Availability: DeviceAvailability.Active, Info.ControlTransport: null } when OnBluetoothOnly =>
            Maxwell.App.Localization.Localizer.Current.Say("Connected over Bluetooth: only the charge is available. Settings work over the dongle or the cable"),

        _ => Snapshot.Availability switch
        {
        DeviceAvailability.Unreachable =>
            Maxwell.App.Localization.Localizer.Current.Say("The headset is not answering. Switch it on or bring it closer — control works over the dongle and over the cable alike"),
        DeviceAvailability.Offline =>
            Maxwell.App.Localization.Localizer.Current.Say("The device is not connected. The last known readings are shown"),
            _ => null,
        },
    };

    /// <summary>
    /// Every live link is Bluetooth. Said as "every" rather than "any", because a headset
    /// with its dongle in and a Bluetooth pairing besides is not on Bluetooth in the sense
    /// the notice means — its settings are reachable and there is nothing to explain.
    /// </summary>
    private bool OnBluetoothOnly =>
        LiveTransports.Count > 0 && LiveTransports.All(t => t.Kind == TransportKind.Bluetooth);

    /// <summary>"updated 14:41", or when the device was last seen if it is gone.</summary>
    public string? Freshness => Snapshot switch
    {
        { Availability: DeviceAvailability.Offline, LastSeenAt: { } seen } => Maxwell.App.Localization.Localizer.Current.Say(
            "last seen — {0}", seen.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
        { UpdatedAt: { } updated } => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            Maxwell.App.Localization.Localizer.Current["dash.updated", "updated {0}"],
            updated.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
        _ => null,
    };

    /// <summary>
    /// Tells the scheduler this page is open, so the device is read often enough and its
    /// volatile values — the wheel, the DPI button — stay in step with the hardware.
    /// </summary>
    public void Activate()
    {
        _polling?.SetAttention(PollingAttention.Focused, DeviceKey);
        _polling?.RequestRefresh(DeviceKey);
    }

    public void Deactivate() => _polling?.SetAttention(PollingAttention.Idle);

    private void OnSnapshotChanged(object? sender, DeviceSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() => Apply(snapshot));

    private void Apply(DeviceSnapshot snapshot)
    {
        Snapshot = snapshot;

        // Rebuild only when the set of capabilities actually changed — a reconnect can
        // bring a different list, a poll never does. Rebuilding on every reading would
        // throw away control state mid-drag.
        if (!SameShape(snapshot))
        {
            Rebuild(snapshot);
        }
        else
        {
            foreach ((string id, CapabilityState state) in snapshot.Capabilities)
            {
                if (_byId.TryGetValue(id, out CapabilityViewModel? vm)) vm.Apply(state);
            }
        }

        RaiseHeader();
    }

    /// <summary>
    /// The capability ids the current sections were built from.
    ///
    /// Held separately rather than inferred from the view models, because not every
    /// capability gets one: the charge and the charging flag are drawn in the hero and
    /// have no row. Comparing the snapshot against the view models therefore found two
    /// missing every time and rebuilt the whole page on every poll — which threw away the
    /// scroll position, every control's state and the identity of every element in the
    /// tree, several times a minute. Nothing above this line could have looked right while
    /// that was happening, and a curve could not be dragged at all.
    /// </summary>
    private readonly HashSet<string> _shape = new(StringComparer.Ordinal);

    private bool SameShape(DeviceSnapshot snapshot) =>
        snapshot.Capabilities.Count == _shape.Count && snapshot.Capabilities.Keys.All(_shape.Contains);

    /// <summary>
    /// The section that says what the device is, for the classes where the enum's order
    /// disagrees. Null everywhere else, and then nothing moves — a headset already leads
    /// with sound, and a mouse's page follows the artboard.
    /// </summary>
    private static CapabilityDomain? Leading(DeviceClass kind) => kind switch
    {
        DeviceClass.Microphone => CapabilityDomain.Microphone,
        _ => null,
    };

    /// <summary>
    /// Which section may ask to be carried in the panel.
    ///
    /// One today: a microphone's headphone socket, whose two rows are half of what the
    /// panel can hold and which are worth that to somebody who monitors themselves and
    /// nothing at all to somebody who has never used the socket. The rows the panel
    /// always carries are not a question and are not asked about.
    /// </summary>
    private static bool OffersTraySwitch(CapabilityDomain domain, DeviceClass owner) =>
        domain == CapabilityDomain.Audio && owner == DeviceClass.Microphone;

    private void OnTrayChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(Sections));

    private void Rebuild(DeviceSnapshot snapshot)
    {
        // Everything is built off to the side and published in one go at the end.
        //
        // Sections is observable and the page redraws on each change, so filling it a
        // section at a time meant the card list was rendered several times half-built —
        // and the equaliser, whose header borrows the preset picker from another view
        // model, was drawn without it until the last step. That is what made the dropdown
        // flicker in and out on every rebuild.
        var built = new List<CapabilitySectionViewModel>();
        var byId = new Dictionary<string, CapabilityViewModel>(StringComparer.Ordinal);

        foreach (CapabilityDomain domain in snapshot.Domains())
        {
            List<CapabilityViewModel> items = [];

            // Rows whose driver asked for a heading of their own, kept aside by it. A
            // dictionary rather than a second pass over the snapshot, so the order the
            // driver published them in survives.
            var labelled = new Dictionary<string, List<CapabilityViewModel>>(StringComparer.Ordinal);

            foreach (CapabilityState state in snapshot.InDomain(domain))
            {
                // Already drawn at the top of the page, in a size that can be read across
                // a desk. Repeating them as rows underneath said the same three things
                // twice and left the section looking like a list of things the hero had
                // already answered.
                if (DrawnInHero.Contains(state.Id)) continue;

                // Read, kept, and not drawn. It exists to give another control its
                // bounds, and the snapshot still carries it for that.
                if (state.Descriptor.IsSupporting) continue;

                CapabilityViewModel vm = CapabilityViewModelFactory.Create(
                    state, _session, HintDetail.Normal, _confirm);
                byId[state.Id] = vm;

                // The preset picker is drawn inside the equaliser card rather than as a
                // row beneath it, so it is handed over and skipped here.
                if (state.Id == CapabilityIds.AudioEqPreset) continue;

                if (state.Descriptor.Section is { Length: > 0 } heading)
                {
                    if (!labelled.TryGetValue(heading, out List<CapabilityViewModel>? group))
                    {
                        labelled[heading] = group = [];
                    }

                    group.Add(vm);
                    continue;
                }

                items.Add(vm);
            }

            if (items.Count > 0)
            {
                built.Add(new CapabilitySectionViewModel(domain, items, snapshot.Info.Class)
                {
                    Tray = OffersTraySwitch(domain, snapshot.Info.Class) && _extras is { } extras
                        ? new TrayToggleViewModel(extras, snapshot.Info.Key)
                        : null,
                });
            }

            // After the domain's own rows, because they are the ones the device is
            // usually opened for: a recording level is reached for far more often than
            // the selector on the back, whatever else is true about where each came from.
            foreach ((string heading, List<CapabilityViewModel> group) in labelled)
            {
                built.Add(new CapabilitySectionViewModel(domain, group, snapshot.Info.Class)
                {
                    Label = heading,
                });
            }
        }

        // A control that depends on a switch is joined to it here, where every row of the
        // page exists at once. The driver said which switch; this only finds it.
        foreach (CapabilityViewModel vm in byId.Values)
        {
            if (vm.Descriptor.EnabledBy is not { Length: > 0 } gate) continue;
            if (byId.GetValueOrDefault(gate) is not ToggleCapabilityViewModel toggle) continue;

            vm.GateWith(toggle);
        }

        if (byId.GetValueOrDefault(CapabilityIds.AudioEqGraphic) is CurveCapabilityViewModel curve)
        {
            if (byId.GetValueOrDefault(CapabilityIds.AudioEqPreset) is ChoiceCapabilityViewModel slots)
            {
                curve.Slots = slots;
            }

            // After the slots, so the picker is built once with both halves in it.
            curve.Library = _presets;
        }

        // The section a device is named after comes first.
        //
        // Order is the enum's otherwise, and the enum is written headset-first — audio
        // before microphone — which is right for a headset and backwards for a
        // microphone, where the capsule is the whole point of the device and the
        // headphone socket is an extra. The artboard puts them that way round too.
        if (Leading(snapshot.Info.Class) is { } first)
        {
            built = [.. built.OrderBy(s => s.Domain == first ? 0 : 1)];
        }

        foreach (CapabilitySectionViewModel section in Sections)
        {
            if (section.Tray is { } gone) gone.PropertyChanged -= OnTrayChanged;
            section.Dispose();
        }
        Sections.Clear();
        _byId.Clear();

        foreach ((string id, CapabilityViewModel vm) in byId) _byId[id] = vm;
        foreach (CapabilitySectionViewModel section in built)
        {
            // The page redraws on this object's notifications, and the tray switch is not
            // this object — it is a small view model of its own hanging off a section. Its
            // change was reaching the setting and nothing else, so the switch moved in the
            // file and stayed put on the screen.
            if (section.Tray is { } tray) tray.PropertyChanged += OnTrayChanged;

            Sections.Add(section);
        }

        _shape.Clear();
        foreach (string id in snapshot.Capabilities.Keys) _shape.Add(id);
    }


    private void RaiseHeader()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ImageKey));
        OnPropertyChanged(nameof(Battery));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(VisibleSections));

        // Capabilities arrive with the first answer, not with the object. Until they do,
        // every device looks like one with nothing to change, and a page that never
        // revisits that shows the "nothing to configure" card over a working headset.
        OnPropertyChanged(nameof(Facts));
        OnPropertyChanged(nameof(HasNothingToChange));
        OnPropertyChanged(nameof(LiveTransports));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(HasFirmware));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(Transports));
        OnPropertyChanged(nameof(StateNotice));
        OnPropertyChanged(nameof(Freshness));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(BatteryMode));
        OnPropertyChanged(nameof(BatteryFraction));
        OnPropertyChanged(nameof(NoBatteryReason));
        OnPropertyChanged(nameof(Extended));
        OnPropertyChanged(nameof(LimitsNote));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _session.SnapshotChanged -= OnSnapshotChanged;

        foreach (CapabilitySectionViewModel section in Sections)
        {
            if (section.Tray is { } tray) tray.PropertyChanged -= OnTrayChanged;
            section.Dispose();
        }

        Sections.Clear();
        _byId.Clear();
        _shape.Clear();
    }
}

/// <summary>One line of "what we managed to read". Label on the left, value on the right.</summary>
public sealed record DeviceFact(string Label, string Value);

/// <summary>Which legend colour the extended hero pill takes.</summary>
public enum HeroPillKind
{
    /// <summary>Blue: a deeply-reversed vendor protocol.</summary>
    Vendor,

    /// <summary>Purple: a class protocol that still reaches past the baseline — HID++.</summary>
    Hidpp,
}

/// <summary>The blue/purple pill in a device hero that says the page is the extended one.</summary>
public sealed record HeroPill(string Text, HeroPillKind Kind);

/// <summary>Which class protocol a section's pill names, and thus which colour it takes.</summary>
public enum ProtocolPillKind
{
    CoreAudio,
    Hidpp,
    Via,
    XInput,
    System,
}

/// <summary>The protocol pill beside a section heading.</summary>
public sealed record ProtocolPill(string Text, ProtocolPillKind Kind);
