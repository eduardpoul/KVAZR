using System.Globalization;

using Kvazr.Core.Catalogue;
using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;

namespace Kvazr.Core.Drivers.Keyboard;

/// <summary>
/// Gives a keyboard the settings it keeps inside itself, wherever it came from.
///
/// A contributor rather than a driver, and for the same reason the audio tier is one:
/// the system already names the keyboard, classes it and knows how it is attached, and
/// VIA can do none of those — it has no command for a name. What it has is what the
/// keyboard stores, so it adds rows to whatever the owner found rather than competing
/// to be the owner.
///
/// Matched by usage page, not by maker. VIA answers on 0xFF60 whatever brand is on the
/// case, which is the whole point of a class protocol; the device's own usb ids are used
/// only to be sure the collection belongs to this keyboard and not to another on the
/// same desk.
/// </summary>
public sealed class ViaContributor : ICapabilityContributor
{
    private readonly IHidReportPipeFactory _pipes;
    private readonly IDeviceCatalogue? _catalogue;

    /// <param name="catalogue">
    /// Where the per-model data lives, and the effect names are the only thing taken from
    /// it. The wire carries an effect as an index and nothing else, and the same index
    /// names a different effect on a board built with a different set of animations —
    /// VIA's own client has the same problem and answers it the same way, with a
    /// definition per model. Optional: without one the effect row is simply not offered.
    /// </param>
    public ViaContributor(IHidReportPipeFactory pipes, IDeviceCatalogue? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(pipes);

        _pipes = pipes;
        _catalogue = catalogue;
    }

    public string ContributorId => "via";

    public Task<IDeviceConnection?> AttachAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        foreach ((ushort vendor, ushort product) in UsbIds(device))
        {
            IHidReportPipe? pipe = _pipes.Open(vendor, product, Via.UsagePage, Via.Usage);
            if (pipe is null) continue;

            // Asked before it is offered. A collection on the right usage page is a
            // promise rather than a fact — a keyboard can carry the page and answer
            // nothing on it — and a page of rows that never fill is worse than no rows.
            byte[]? version = pipe.Request(
                Via.Request(pipe.ReportLength, Via.GetProtocolVersion),
                reply => Via.Answers(reply, Via.GetProtocolVersion),
                TimeSpan.FromMilliseconds(600));

            if (version is null || Via.Declined(version) || Via.ProtocolVersion(version) is not > 0)
            {
                pipe.Dispose();
                continue;
            }

            CatalogueEntry? entry = _catalogue?.Match(device.HardwareIds, device.Model);

            return Task.FromResult<IDeviceConnection?>(
                new ViaConnection(
                    pipe,
                    device.Transports,
                    entry?.LightingEffects ?? [],
                    entry?.ColourlessEffects ?? []));
        }

        return Task.FromResult<IDeviceConnection?>(null);
    }

    /// <summary>The usb ids this device carries, as the system tier wrote them.</summary>
    private static IEnumerable<(ushort Vendor, ushort Product)> UsbIds(DeviceInfo device)
    {
        foreach (string id in device.HardwareIds)
        {
            if (!id.StartsWith("usb:", StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = id.Split(':');
            if (parts.Length != 3) continue;

            if (ushort.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort vendor)
                && ushort.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort product))
            {
                yield return (vendor, product);
            }
        }
    }
}
