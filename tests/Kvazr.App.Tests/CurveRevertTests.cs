using Kvazr.App.ViewModels.Capabilities;
using Kvazr.Core.Devices;
using Xunit;

using static Kvazr.App.Tests.Harness;

namespace Kvazr.App.Tests;

/// <summary>
/// Reverting a curve has to survive the settle that follows it.
///
/// Measured in the running application: pressing reset flattened the plot, and 433 ms
/// later — the moment the session announced the write had started — every band sprang
/// back to the edit, then sat there for thirteen seconds until an unrelated capability
/// moved and forced a redraw.
///
/// Neither half was the device. A composite keeps its own array of slot readings and
/// re-pushes it after every settle; the revert wrote its vector straight past that array,
/// so the array still held the edit and the settle painted it back. The thirteen seconds
/// were the second half: the session's confirmation of our own write arrives inside the
/// window that exists to ignore readings older than the write, so it was thrown away with
/// them, and nothing corrected the array until something else published.
/// </summary>
public sealed class CurveRevertTests
{
    [Fact]
    public void ASettleAfterRevertDoesNotRepaintTheEdit()
    {
        Bind();

        var session = new Recorder();
        var vm = (CurveCapabilityViewModel)CapabilityViewModelFactory.Create(Curve(0, 0, 0), session);
        Pump();

        // Band 1 dragged to the floor, then reset.
        vm.MovePointCommand.Execute(new CurvePoint(1, 0));
        Assert.Equal(-12, vm.Values[1]);

        vm.RevertCommand.Execute(null);
        Assert.Equal(0, vm.Values[1]);

        // What the session publishes the instant it starts writing: the value from before,
        // because the device has not taken the new one yet.
        vm.Apply(Curve(0, -12, 0) with { IsWriting = true });
        Pump();

        Assert.Equal(0, vm.Values[1]);
    }

    [Fact]
    public void OurOwnWriteComingBackConfirmedEndsTheEchoWindow()
    {
        Bind();

        var session = new Recorder();
        var vm = (RangeCapabilityViewModel)CapabilityViewModelFactory.Create(Range(100), session);
        Pump();

        vm.Value = 120;

        // The session's answer: the device took it. Nothing about this reading is stale.
        vm.Apply(Range(120));
        Pump();

        // The wheel on the earcup, turned a moment later. Before the window closed on the
        // confirmation this was swallowed as an echo and the panel trailed the hardware by
        // a second and a half.
        vm.Apply(Range(130));
        Pump();

        Assert.Equal(130, vm.Value);
    }
}
