using Kvazr.App.ViewModels.Devices;
using Kvazr.Core.Devices;
using Xunit;

namespace Kvazr.App.Tests;

/// <summary>
/// A device that is not connected shows no charge.
///
/// It used to: a pair of AirPods under "not connected" carried 100% and 90% beside them
/// and "updated 20:50" under that — a charge that was not the charge now, next to a
/// sentence saying the device was not there. What a disconnected device says instead is
/// when it was last here.
/// </summary>
public sealed class OfflineChargeTests
{
    private static readonly DateTimeOffset Seen = new(2026, 10, 14, 20, 50, 0, TimeSpan.Zero);

    private sealed class Fixed(DeviceSnapshot snapshot) : IDeviceSession
    {
        public string DeviceKey => snapshot.Info.Key;

        public DeviceSnapshot Snapshot => snapshot;

        public event EventHandler<DeviceSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(IReadOnlyList<string> ids, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task RefreshAllAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string id, CapabilityValue value, CancellationToken token = default) =>
            Task.FromResult(true);
    }

    private static CapabilityState Charge(string id, string title, int percent) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = title,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },
        Value = CapabilityValue.Of(percent),
    };

    private static DeviceSnapshot AirPods(DeviceAvailability availability, DateTimeOffset? lastSeen = null) => new()
    {
        Info = new DeviceInfo
        {
            Key = "system:airpods",
            DriverId = "system",
            Vendor = "Apple",
            Model = "Edik’s AirPods",
            Class = DeviceClass.Earbuds,
        },
        Availability = availability,
        LastSeenAt = lastSeen,
        UpdatedAt = Seen,
        Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
        {
            [CapabilityIds.PowerBattery] = Charge(CapabilityIds.PowerBattery, "Charge", 90),
            [CapabilityIds.PowerBatteryLeft] = Charge(CapabilityIds.PowerBatteryLeft, "Left", 100),
            [CapabilityIds.PowerBatteryRight] = Charge(CapabilityIds.PowerBatteryRight, "Right", 90),
        },
    };

    [Fact]
    public void Connected_TheChargeIsShown_HalfByHalf()
    {
        DeviceSnapshot here = AirPods(DeviceAvailability.Active);

        Assert.Equal(BatteryDisplay.Charge, BatteryReadout.Mode(here));
        Assert.Equal(2, BatteryParts.Of(here).Count);
    }

    [Fact]
    public void NotConnected_NoChargeIsShown_AnywhereItWouldBeDrawn()
    {
        DeviceSnapshot gone = AirPods(DeviceAvailability.Offline, Seen);

        Assert.Equal(BatteryDisplay.Offline, BatteryReadout.Mode(gone));
        Assert.Empty(BatteryParts.Of(gone));

        var page = new DevicePageViewModel(new Fixed(gone));
        Assert.Equal(BatteryDisplay.Offline, page.BatteryMode);
        Assert.False(page.HasBatteryParts);
    }

    [Fact]
    public void NotConnected_ThePageSaysWhenItWasLastHere()
    {
        var page = new DevicePageViewModel(new Fixed(AirPods(DeviceAvailability.Offline, Seen)));

        Assert.NotNull(page.StateNotice);
        Assert.Contains("20:50", page.StateNotice, StringComparison.Ordinal);
    }

    /// <summary>Never reachable in this run, so there is no time to give — and no reading to excuse.</summary>
    [Fact]
    public void NeverConnected_ThePageSaysOnlyThatItIsNotHere()
    {
        var page = new DevicePageViewModel(new Fixed(AirPods(DeviceAvailability.Offline)));

        Assert.NotNull(page.StateNotice);
        Assert.DoesNotContain("20:50", page.StateNotice, StringComparison.Ordinal);
    }
}
