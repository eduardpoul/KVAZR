using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// When a switch writes to the hardware, and when it must not.
///
/// A switch has three ways of moving and only one of them is a person: the page builds
/// it with an opening position, a reading lands on it, or somebody clicks it. Only the
/// last is an instruction, and the difference matters more here than on any other
/// control — this is the one whose write, on a microphone, pulses the mute flag through
/// its opposite so the device has a transition to follow.
/// </summary>
public sealed class ToggleWriteTests
{
    private sealed class Recorder : IDeviceSession
    {
        public List<(string Id, CapabilityValue Value)> Writes { get; } = [];

        public string DeviceKey => "test:device";

        public DeviceSnapshot Snapshot { get; } = new()
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
        };

        public event EventHandler<DeviceSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(IReadOnlyList<string> ids, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task RefreshAllAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string id, CapabilityValue value, CancellationToken token = default)
        {
            Writes.Add((id, value));
            return Task.FromResult(true);
        }
    }

    private static CapabilityState Switch(bool on) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.MicSystemOn,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Toggle,
            Access = CapabilityAccess.ReadWrite,
            Title = "Microphone",
        },
        Value = CapabilityValue.Of(on),
    };

    /// <summary>
    /// Building the page is not an instruction.
    ///
    /// The observable property fires its change handler for any move, and a switch whose
    /// opening position is "on" is a move from the field's default. So every page built
    /// over a live microphone wrote "on" to it — and that write is a pulse: mute, then
    /// unmute, because the flag was already where it was being asked for. A microphone
    /// turned off and then looked at again came back on.
    /// </summary>
    [Fact]
    public void BuildingASwitchThatIsOn_WritesNothing()
    {
        Harness.Bind();
        var session = new Recorder();

        _ = new ToggleCapabilityViewModel(Switch(true), session);
        Harness.Pump();

        Assert.Empty(session.Writes);
    }

    /// <summary>A reading is not an instruction either.</summary>
    [Fact]
    public void AReadingThatMovesTheSwitch_WritesNothing()
    {
        Harness.Bind();
        var session = new Recorder();
        var toggle = new ToggleCapabilityViewModel(Switch(false), session);
        Harness.Pump();

        toggle.Apply(Switch(true));
        Harness.Pump();

        Assert.True(toggle.IsOn);
        Assert.Empty(session.Writes);
    }

    /// <summary>
    /// And a hand on it is - which is also what keeps the two above from passing for the
    /// wrong reason. Writes are held until the control has laid out, so a test that never
    /// pumped the dispatcher would find every write suppressed and every one of these
    /// green whatever the code did.
    /// </summary>
    [Fact]
    public void AClick_Writes()
    {
        Harness.Bind();
        var session = new Recorder();
        var toggle = new ToggleCapabilityViewModel(Switch(false), session);
        Harness.Pump();

        toggle.IsOn = true;

        Assert.Equal([(CapabilityIds.MicSystemOn, CapabilityValue.Of(true))], session.Writes);
    }
}
