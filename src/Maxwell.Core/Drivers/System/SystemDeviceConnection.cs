using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.System;

/// <summary>
/// A device seen only through the operating system: one capability if it reports a
/// battery, and none at all if it does not.
///
/// A device with no capabilities is a legitimate outcome here, not a broken connection.
/// The design draws it deliberately — an unidentified Bluetooth device that answers
/// nothing still gets a row and a page, because "connected, and there is nothing to
/// configure" is information a person can act on, while an empty list is not.
///
/// There is nothing to open and nothing to hold: the properties are read straight out of
/// the device tree on every call. That costs a device-tree walk per read, which is more
/// than a protocol driver pays, and buys back never holding a handle on hardware this
/// driver does not understand.
/// </summary>
internal sealed class SystemDeviceConnection : IDeviceConnection
{
    private static readonly Capability BatteryCapability = new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charge",
        Subtitle = "The system reports it itself",
        Access = CapabilityAccess.Read,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private readonly ISystemDeviceEnumerator _enumerator;
    private readonly DeviceInfo _device;

    public SystemDeviceConnection(ISystemDeviceEnumerator enumerator, DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(device);

        _enumerator = enumerator;
        _device = device;

        // Declared from what the device was actually offering when the connection was
        // opened. Advertising a battery that never answers would put a reading on the
        // page that stays a dash forever, which reads as a fault rather than as a device
        // that simply does not report one.
        Capabilities = Battery() is not null ? [BatteryCapability] : [];
    }

    public IReadOnlyList<Capability> Capabilities { get; }

    public IReadOnlyList<TransportInfo> Transports => _device.Transports;

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (capabilityId != CapabilityIds.PowerBattery) return Task.FromResult(CapabilityValue.Absent);

        return Task.FromResult(Battery() is { } percent ? CapabilityValue.Of(percent) : CapabilityValue.Absent);
    }

    /// <summary>Nothing here is writable, and nothing pretends to be.</summary>
    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public void Dispose()
    {
    }

    /// <summary>
    /// The charge, from whichever node carries it — and only while that node's lead is up.
    ///
    /// The whole container is searched rather than a remembered node, because on a
    /// Bluetooth headset the charge sits on the Hands-Free node rather than the one named
    /// after the device, and the node that had it last time may not be present this time.
    ///
    /// Windows remembers a Bluetooth device's last known charge and goes on answering it
    /// after the device has gone — with a timestamp beside it, which is how the memory
    /// can be told from a reading. Measured on a keyboard switched from Bluetooth to its
    /// dongle: the Bluetooth nodes still said forty-five per cent, every one of them said
    /// not connected, and the property's own stamp was a quarter of an hour old. The page
    /// showed forty-five per cent under "updated" and the minute of the last sweep.
    ///
    /// One device can hold several leads and only one of them carries anything, so the
    /// question is not whether the device has a charge but whether this lead can read it.
    /// A node that says nothing about being connected — every USB one — is taken as it
    /// always was: silence there is not a denial.
    /// </summary>
    private int? Battery() =>
        _enumerator.Enumerate()
            .Where(n => _device.HardwareIds.Contains($"container:{n.ContainerId:D}"))
            .Where(n => n.IsConnected != false)
            .Select(n => n.BatteryPercent)
            .FirstOrDefault(b => b.HasValue);
}
