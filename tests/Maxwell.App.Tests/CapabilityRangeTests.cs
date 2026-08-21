using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// A range with no reading must not look like a range reading zero.
///
/// This is the one place where a control has no way to be honest on its own: a slider
/// handed nothing sits at its minimum, and a volume slider at its minimum is the screen
/// stating the volume is zero. It happens exactly when the headset is asleep on its
/// dongle — which is exactly when somebody might believe it.
/// </summary>
public sealed class CapabilityRangeTests
{
    private static CapabilityState State(CapabilityValue value) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.AudioVolume,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Общая громкость",
            Range = new CapabilityRange { Min = 0, Max = 150 },
        },
        Value = value,
    };

    [Fact]
    public void AnUnreadRangeHasNoValueToShow()
    {
        var vm = (RangeCapabilityViewModel)CapabilityViewModelFactory.Create(State(CapabilityValue.Absent), null);

        Assert.False(vm.HasValue);
        Assert.Equal("—", vm.Display);
    }

    [Fact]
    public void AReadRangeShowsIt()
    {
        var vm = (RangeCapabilityViewModel)CapabilityViewModelFactory.Create(State(CapabilityValue.Of(147)), null);

        Assert.True(vm.HasValue);
        Assert.Equal(147, vm.Value);
    }

    [Fact]
    public void ZeroIsAReading_AndIsNotAbsence()
    {
        // The distinction the whole value hierarchy exists for: a device that answered
        // zero has answered, and must not be drawn the same as one that said nothing.
        var vm = (RangeCapabilityViewModel)CapabilityViewModelFactory.Create(State(CapabilityValue.Of(0)), null);

        Assert.True(vm.HasValue);
        Assert.Equal(0, vm.Value);
    }
}
