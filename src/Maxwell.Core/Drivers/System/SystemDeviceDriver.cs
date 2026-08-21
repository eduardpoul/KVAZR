using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.System;

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

    public SystemDeviceDriver(ISystemDeviceEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        _enumerator = enumerator;
    }

    public string DriverId => "system";

    public DriverTier Tier => DriverTier.System;

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<DeviceInfo>();

        foreach (IGrouping<Guid, SystemDeviceNode> container in _enumerator.Enumerate().GroupBy(n => n.ContainerId))
        {
            List<SystemDeviceNode> nodes = [.. container];
            if (!SystemDeviceProfile.IsPeripheral(nodes)) continue;

            DeviceClass deviceClass = SystemDeviceProfile.Classify(nodes);
            string name = SystemDeviceProfile.Name(nodes);

            devices.Add(new DeviceInfo
            {
                // Keyed on the container rather than on the product ids: a Bluetooth
                // device has no product ids at all, and the container is the one
                // identifier every device here is guaranteed to have.
                Key = $"system:{container.Key:D}",
                DriverId = DriverId,

                // No maker, deliberately. The obvious property for it —
                // DEVPKEY_Device_Manufacturer — is the manufacturer of the *driver
                // package*, not of the hardware, so it reads "Microsoft" for a Logitech
                // receiver and "(Standard USB Host Controller)" for a HyperX microphone.
                // The bus-reported name already carries the brand: "HyperX QuadCast S",
                // "NuPhy Air96 V2 Dongle", "Audeze Maxwell BT". Splitting a real maker
                // out of that needs a vendor-id table, which is catalogue work.
                Vendor = string.Empty,
                Model = name,
                Class = deviceClass,
                ImageKey = null,
                HardwareIds = SystemDeviceProfile.HardwareIds(container.Key, nodes),
                Transports = SystemDeviceProfile.Transports(nodes, deviceClass),
            });
        }

        return Task.FromResult<IReadOnlyList<DeviceInfo>>(devices);
    }

    public Task<IDeviceConnection> ConnectAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Task.FromResult<IDeviceConnection>(new SystemDeviceConnection(_enumerator, device));
    }
}
