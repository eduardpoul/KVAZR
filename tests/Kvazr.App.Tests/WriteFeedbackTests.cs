using Kvazr.App.ViewModels.Capabilities;
using Kvazr.Core.Devices;
using Xunit;

using static Kvazr.App.Tests.Harness;

namespace Kvazr.App.Tests;

/// <summary>
/// What a row is allowed to say and do while a write is on its way to the device.
///
/// Measured on the equaliser, one drag of one band: "не сохранено" at 479 ms,
/// "применяется" at 892 ms, "не сохранено" again at 1509 ms, and the save icon greyed out
/// and back over the same six hundred milliseconds. Three visible changes for one gesture,
/// none of them anything the person could act on — the owner of the headset described it
/// as the icons twitching and asked for the device work to happen quietly.
/// </summary>
public sealed class WriteFeedbackTests
{
    [Fact]
    public void ASwitchShowsWhatWasPickedRatherThanWhatTheDeviceStillReports()
    {
        Bind();

        var session = new Recorder();
        var vm = (ChoiceCapabilityViewModel)CapabilityViewModelFactory.Create(
            Presets(0, "Выключено", "Низкое", "Высокое"), session);
        Pump();

        vm.Selected = vm.Options[1];

        // What the session publishes the instant the write starts, on this very thread:
        // still the option from before.
        vm.Apply(Presets(0, "Выключено", "Низкое", "Высокое") with { IsWriting = true });
        Pump();

        Assert.Equal("Низкое", vm.Selected!.Label);
    }

    [Fact]
    public void AToggleShowsWhatWasPickedRatherThanWhatTheDeviceStillReports()
    {
        Bind();

        var session = new Recorder();
        var vm = (ToggleCapabilityViewModel)CapabilityViewModelFactory.Create(Toggle(false), session);
        Pump();

        vm.IsOn = true;

        vm.Apply(Toggle(false) with { IsWriting = true });
        Pump();

        Assert.True(vm.IsOn);
    }

    [Fact]
    public void AnOrdinaryWriteIsNotWorthAnnouncing()
    {
        Bind();

        var session = new Recorder();
        CapabilityViewModel vm = CapabilityViewModelFactory.Create(Range(100), session);
        Pump();

        // The session's first publish: the write is out, the device has not answered, and
        // the value on the row is still the one from before.
        vm.Apply(Range(100) with { IsWriting = true });
        Pump();

        Assert.False(vm.IsWriting);

        // And the answer, a fraction of a second later.
        vm.Apply(Range(120));
        Pump();

        Assert.False(vm.IsWriting);
    }

    [Fact]
    public void AWriteInFlightDoesNotLockTheControl()
    {
        Bind();

        var session = new Recorder();
        var vm = (RangeCapabilityViewModel)CapabilityViewModelFactory.Create(Range(100), session);
        Pump();

        vm.Apply(Range(100) with { IsWriting = true });
        Pump();

        // Two things are wrong with locking it, and only one of them is the grey. Writes
        // for one device are serialised by the session, so a second one queues safely;
        // refusing it here threw the edit away instead, without saying anything.
        Assert.True(vm.CanEdit);

        vm.Value = 130;
        Pump();

        Assert.Equal(130, vm.Value);
    }
}
