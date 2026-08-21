using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using Maxwell.Core.Devices;

namespace Maxwell.Core.Catalogue;

/// <summary>
/// One model, and how to recognise it.
///
/// Keyed by a slug rather than by a product id, because a Bluetooth device has no product
/// ids at all and a model that ships under several of them is still one model to the
/// person holding it. The QuadCast S is the case that forced this: two USB product ids,
/// one microphone.
/// </summary>
public sealed record CatalogueEntry
{
    /// <summary>Stable key, e.g. "hyperx-quadcast-s". Also the image name.</summary>
    public required string Slug { get; init; }

    public required string Vendor { get; init; }

    /// <summary>The full name, maker included, as a person would write it.</summary>
    public required string Model { get; init; }

    public DeviceClass Class { get; init; } = DeviceClass.Unknown;

    /// <summary>USB ids that identify this model, as "046d:c547".</summary>
    public IReadOnlyList<string> Usb { get; init; } = [];

    /// <summary>
    /// Fragments of a Bluetooth name that identify this model, matched case-insensitively.
    /// A weaker key than a product id and the only one Bluetooth offers, which is why a
    /// fragment here has to be specific enough to be wrong rarely — "AirPods" is fine,
    /// "Pro" would match half the world.
    /// </summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>
    /// True when nobody has held this device against the entry. The catalogue may grow
    /// faster than the hardware anyone here owns, and the interface already has a word
    /// for that: an untested capability carries an amber mark rather than a claim.
    /// </summary>
    public bool Unverified { get; init; }
}

/// <summary>What the catalogue can be asked.</summary>
public interface IDeviceCatalogue
{
    /// <summary>
    /// The entry for a device, or null when nothing matches — which is the ordinary case
    /// and must stay usable. Everything the catalogue adds is an improvement on what the
    /// system already reported, never a precondition for showing the device at all.
    /// </summary>
    CatalogueEntry? Match(IReadOnlySet<string> hardwareIds, string reportedName);

    /// <summary>
    /// Who made a device, from its USB vendor id. Separate from <see cref="Match"/>
    /// because it answers for hardware no entry covers, which is most hardware.
    /// </summary>
    string? Vendor(IReadOnlySet<string> hardwareIds);
}

/// <summary>
/// The catalogue as shipped: a JSON document embedded in this assembly.
///
/// Data rather than code, deliberately and permanently. Adding a model must never mean
/// adding a class — the moment it does, the hundredth device costs what the first one
/// did. When this grows past what belongs in a binary it becomes a file fetched from a
/// repository, and nothing above this interface has to notice.
/// </summary>
public sealed class DeviceCatalogue : IDeviceCatalogue
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // The class is written as a word in the file, because a catalogue is edited by
        // people and "Microphone" is checkable at a glance where 3 is not.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, CatalogueEntry> _byUsb = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CatalogueEntry> _byName = [];
    private readonly Dictionary<string, string> _vendors = new(StringComparer.OrdinalIgnoreCase);

    public DeviceCatalogue()
    {
        Document document = Load();

        Entries = document.Devices;

        foreach (CatalogueEntry entry in document.Devices)
        {
            foreach (string id in entry.Usb) _byUsb[$"usb:{id}"] = entry;
            if (entry.Names.Count > 0) _byName.Add(entry);
        }

        foreach ((string vid, string name) in document.Vendors) _vendors[vid] = name;
    }

    public IReadOnlyList<CatalogueEntry> Entries { get; }

    public CatalogueEntry? Match(IReadOnlySet<string> hardwareIds, string reportedName)
    {
        ArgumentNullException.ThrowIfNull(hardwareIds);
        ArgumentNullException.ThrowIfNull(reportedName);

        // Product ids first. They are exact, and a name match that disagreed with one
        // would be a name match that is wrong.
        foreach (string id in hardwareIds)
        {
            if (_byUsb.TryGetValue(id, out CatalogueEntry? byId)) return byId;
        }

        foreach (CatalogueEntry entry in _byName)
        {
            foreach (string fragment in entry.Names)
            {
                if (reportedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return entry;
            }
        }

        return null;
    }

    public string? Vendor(IReadOnlySet<string> hardwareIds)
    {
        ArgumentNullException.ThrowIfNull(hardwareIds);

        foreach (string id in hardwareIds)
        {
            if (!id.StartsWith("usb:", StringComparison.Ordinal)) continue;

            // "usb:046d:c547" — the vendor is the middle field.
            string[] parts = id.Split(':');
            if (parts.Length == 3 && _vendors.TryGetValue(parts[1], out string? vendor)) return vendor;
        }

        return null;
    }

    private static Document Load()
    {
        Assembly assembly = typeof(DeviceCatalogue).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream("Maxwell.Core.Catalogue.devices.json");

        if (stream is null)
        {
            // An absent catalogue is survivable by design, so it must not be fatal. Every
            // device still appears with whatever the system reported about it.
            return Document.Empty;
        }

        return JsonSerializer.Deserialize<Document>(stream, Options) ?? Document.Empty;
    }

    private sealed record Document(
        IReadOnlyList<CatalogueEntry> Devices,
        IReadOnlyDictionary<string, string> Vendors)
    {
        public static readonly Document Empty = new([], new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

/// <summary>
/// A catalogue that knows nothing, for tests and for proving the point: with this in
/// place every device must still be listed, named and classified as well as the operating
/// system managed on its own.
/// </summary>
public sealed class EmptyCatalogue : IDeviceCatalogue
{
    public static readonly EmptyCatalogue Instance = new();

    private EmptyCatalogue()
    {
    }

    public CatalogueEntry? Match(IReadOnlySet<string> hardwareIds, string reportedName) => null;

    public string? Vendor(IReadOnlySet<string> hardwareIds) => null;
}
