using System.Runtime.Versioning;

using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;

namespace Kvazr.Core.Drivers.Flydigi;

/// <summary>
/// Adds the charge of a Flydigi pad to the gamepad the system already found.
///
/// A contributor rather than a driver, for the reason the HyperX one gives: the system
/// tier already gets the pad's name, class and leads right, and folds its dongle and dock
/// into it; what it cannot do is ask the pad anything. XInput has no answer either — the
/// dongle reports itself wired with no battery — and the pad's own channel does.
///
/// Matched on the dongle's usb id, because the dongle is the way to the pad: it is the
/// collection the question goes to, and it is in the system only while the pad is
/// switched on. The dock is folded into the same device but does not answer the question.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FlydigiContributor : ICapabilityContributor
{
    /// <summary>The Apex 5's dongle. Deliberately one model: it is the only pad these bytes were checked against.</summary>
    internal const string Dongle = "usb:37d7:2501";

    private const ushort DongleProduct = 0x2501;

    private readonly IHidEnumerator _enumerator;

    public FlydigiContributor(IHidEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        _enumerator = enumerator;
    }

    public string ContributorId => "flydigi";

    public Task<IDeviceConnection?> AttachAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!device.HardwareIds.Contains(Dongle)) return Task.FromResult<IDeviceConnection?>(null);

        // Asked of the collection once here, so that a dongle without it — another
        // firmware, another mode — attaches nothing rather than a row that never fills.
        return Task.FromResult<IDeviceConnection?>(
            Find(_enumerator) is null ? null : new FlydigiConnection(() => Find(_enumerator)));
    }

    /// <summary>The dongle's command collection, wherever it is plugged in today.</summary>
    internal static HidInterface? Find(IHidEnumerator enumerator) =>
        enumerator.EnumerateAll().FirstOrDefault(i =>
            i.VendorId == FlydigiV2.Vendor
            && i.ProductId == DongleProduct
            && i.UsagePage == FlydigiV2.VendorPage
            && i.OutputReportLength > 0);
}
