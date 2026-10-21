using Kvazr.Core.Catalogue;
using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;

namespace Kvazr.Core.Drivers.System;

/// <summary>
/// Finds every peripheral the operating system can see, and claims to understand none of
/// them.
///
/// This is the floor the application stands on. It needs no protocol, no vendor
/// knowledge and no catalogue entry, so a device appears in the list the first time it
/// is plugged in rather than the first time somebody teaches us about it. What it offers
/// is thin — a name, a class, a link, and a battery where the device volunteers one —
/// but it is thin for everything equally, and that is what makes the long tail of
/// hardware tractable instead of endless.
///
/// It runs at <see cref="DriverTier.System"/>, which means the registry drops whatever it
/// finds that a better driver has already claimed. That is the intended arrangement, not
/// a workaround: this driver is meant to be almost entirely shadowed on a machine with
/// good driver coverage, and to be the only thing standing on a machine without any.
/// </summary>
public sealed class SystemDeviceDriver : IDeviceDriver
{
    private readonly ISystemDeviceEnumerator _enumerator;
    private readonly IDeviceCatalogue _catalogue;

    public SystemDeviceDriver(ISystemDeviceEnumerator enumerator, IDeviceCatalogue? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _enumerator = enumerator;

        // Optional on purpose. Everything the catalogue adds is an improvement on what
        // the system already reported, so running without one has to work — and the test
        // that proves it passes EmptyCatalogue.
        _catalogue = catalogue ?? EmptyCatalogue.Instance;
    }

    public string DriverId => "system";

    public DriverTier Tier => DriverTier.System;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<Candidate>();

        foreach (IGrouping<Guid, SystemDeviceNode> container in _enumerator.Enumerate().GroupBy(n => n.ContainerId))
        {
            List<SystemDeviceNode> nodes = [.. container];
            if (!SystemDeviceProfile.IsPeripheral(nodes)) continue;

            HashSet<string> ids = SystemDeviceProfile.HardwareIds(container.Key, nodes);
            string reported = SystemDeviceProfile.Name(nodes);

            found.Add(new Candidate(container.Key, nodes, ids, reported, _catalogue.Match(ids, reported)));
        }

        return Task.FromResult<IReadOnlyList<DeviceInfo>>(Assemble(found));
    }

    /// <summary>
    /// Turns the containers into devices, joining any that the catalogue says are one.
    ///
    /// The QuadCast S is why this exists: Windows sees two USB devices with two product
    /// ids and is right to, since nothing it can read says otherwise. Only something with
    /// knowledge can say they are one microphone, and that is a catalogue entry — so the
    /// join happens here, after matching, rather than anywhere inside the enumerator.
    /// </summary>
    private List<DeviceInfo> Assemble(List<Candidate> found)
    {
        var devices = new List<DeviceInfo>();

        foreach (IGrouping<string, Candidate> model in found.GroupBy(c => c.Entry?.Slug ?? $"container:{c.Container:D}"))
        {
            List<Candidate> parts = [.. model];
            Candidate first = parts[0];
            CatalogueEntry? entry = first.Entry;

            List<SystemDeviceNode> nodes = [.. parts.SelectMany(p => p.Nodes)];
            var ids = new HashSet<string>(parts.SelectMany(p => p.Ids), StringComparer.Ordinal);

            DeviceClass observed = SystemDeviceProfile.Classify(nodes);

            bool viaDongle = entry is not null && entry.DongleUsb.Any(d => ids.Contains($"usb:{d}"));

            // The entry is a guess and the device is a fact — except through a dongle.
            // A dongle's descriptors describe the dongle: the NuPhy receiver presents
            // keyboard, mouse and consumer collections whatever is paired to it, so the
            // observation is an artefact of emulation, and the entry that named this id
            // a dongle is the only thing that knows what stands behind it.
            DeviceClass deviceClass = viaDongle && entry!.Class != DeviceClass.Unknown
                ? entry.Class
                : observed != DeviceClass.Unknown ? observed : entry?.Class ?? DeviceClass.Unknown;

            HashSet<string>? dongleIds = entry is { DongleUsb.Count: > 0 }
                ? new HashSet<string>(entry.DongleUsb, StringComparer.OrdinalIgnoreCase)
                : null;
            HashSet<string>? dockIds = entry is { DockUsb.Count: > 0 }
                ? new HashSet<string>(entry.DockUsb, StringComparer.OrdinalIgnoreCase)
                : null;

            devices.Add(new DeviceInfo
            {
                Key = KeyOf(entry, parts),
                DriverId = DriverId,

                // Vendor from the catalogue, or from the vendor-id table, or nothing.
                // Never from DEVPKEY_Device_Manufacturer: that is the author of the
                // driver package, and it reads "Microsoft" for a Logitech receiver.
                Vendor = entry?.Vendor ?? _catalogue.Vendor(ids) ?? string.Empty,
                Maker = SystemDeviceProfile.Maker(parts.SelectMany(p => p.Nodes).ToList()),
                Revision = SystemDeviceProfile.Revision(parts.SelectMany(p => p.Nodes).ToList()),
                Model = entry?.Model ?? first.Reported,
                Class = deviceClass,
                ImageKey = entry?.Slug,
                HardwareIds = ids,
                Transports = SystemDeviceProfile.Transports(nodes, deviceClass, dongleIds, dockIds),
                Auxiliary = entry?.Hidden ?? false,
            });
        }

        return devices;
    }

    /// <summary>
    /// The key, which has to survive a restart — and, the half that was missed, a lead
    /// coming or going.
    ///
    /// A container is the right key for hardware that is one container, or several that
    /// always arrive together: Windows gives it the same id every time it sees it, and a
    /// slug only exists while an entry matches. When several were joined the lowest id
    /// was taken, so the choice did not depend on enumeration order. That holds only while
    /// the same containers are present, and a device whose entry names a dongle or a dock
    /// is exactly one whose containers come and go on their own. The APEX 5 sat in the
    /// list as its dock while the pad was off; switched on, its dongle arrived with a lower
    /// container id, the key moved with it, and the gamepad became two — a live one at the
    /// top and the dock-only one left under DISCONNECTED.
    ///
    /// So such a device is keyed by its entry. It was matched by usb id, which does not
    /// drift the way a Bluetooth name can, and the entry is the one thing every one of its
    /// leads agrees on.
    /// </summary>
    private static string KeyOf(CatalogueEntry? entry, List<Candidate> parts) =>
        entry is { } routed && (routed.DongleUsb.Count > 0 || routed.DockUsb.Count > 0)
            ? $"system:{routed.Slug}"
            : $"system:{parts.Min(p => p.Container):D}";

    /// <summary>One container, and what the catalogue made of it.</summary>
    private sealed record Candidate(
        Guid Container,
        List<SystemDeviceNode> Nodes,
        HashSet<string> Ids,
        string Reported,
        CatalogueEntry? Entry);

    public Task<IDeviceConnection> ConnectAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Task.FromResult<IDeviceConnection>(new SystemDeviceConnection(_enumerator, device));
    }
}
