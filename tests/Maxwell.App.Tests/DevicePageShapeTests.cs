using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// A poll that changes nothing must not rebuild the page.
///
/// This looks like a performance detail and is not. Rebuilding replaces every view model
/// and every element in the tree, so the scroll position goes, whatever the pointer was
/// holding goes, and any control mid-interaction is thrown away — several times a minute,
/// silently. It stayed hidden for as long as nothing on the page needed dragging: the
/// symptom was an equaliser whose bands sprang back a moment after being moved, a preset
/// picker that flickered, and a page that scrolled itself to the top.
///
/// The cause was that the check compared the snapshot against the view models, and two
/// capabilities deliberately have none — the charge and the charging flag are drawn in the
/// hero. Two short, every time, so the answer was always "the shape changed".
/// </summary>
public sealed class DevicePageShapeTests
{
    private sealed class StillSession : IDeviceSession
    {
        public string DeviceKey => "test:device";

        public DeviceSnapshot Snapshot { get; set; } = Build();

        public event EventHandler<DeviceSnapshot>? SnapshotChanged;

        public void Poll() => SnapshotChanged?.Invoke(this, Snapshot);

        public Task RefreshAsync(IReadOnlyList<string> capabilityIds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RefreshAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string capabilityId, CapabilityValue value, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// A charge, which the hero draws and no row does, alongside one that gets a row.
    /// Both are needed: the bug only appears when the two counts differ.
    /// </summary>
    private static DeviceSnapshot Build() => new()
    {
        Info = new DeviceInfo
        {
            Key = "test:device",
            DriverId = "test",
            Vendor = "Test",
            Model = "Device",
            Class = DeviceClass.Headset,
        },
        Availability = DeviceAvailability.Active,
        Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
        {
            [CapabilityIds.PowerBattery] = new()
            {
                Descriptor = new Capability
                {
                    Id = CapabilityIds.PowerBattery,
                    Domain = CapabilityDomain.Power,
                    Primitive = CapabilityPrimitive.Readout,
                    Title = "Заряд",
                },
                Value = CapabilityValue.Of(80),
            },
            [CapabilityIds.AudioVolume] = new()
            {
                Descriptor = new Capability
                {
                    Id = CapabilityIds.AudioVolume,
                    Domain = CapabilityDomain.Audio,
                    Primitive = CapabilityPrimitive.Range,
                    Title = "Громкость",
                    Range = new CapabilityRange { Min = 0, Max = 150 },
                },
                Value = CapabilityValue.Of(100),
            },
        },
    };

    [Fact]
    public void RepeatedPolls_KeepTheSameSections()
    {
        var session = new StillSession();
        using var page = new DevicePageViewModel(session);

        object[] before = [.. page.Sections];
        Assert.NotEmpty(before);

        session.Poll();
        session.Poll();

        Assert.Equal(before.Length, page.Sections.Count);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], page.Sections[i]);
        }
    }
}
