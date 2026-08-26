using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audio;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// Which of a device's audio endpoints earn rows.
///
/// Usually all of them: a microphone with a headphone socket on its base is genuinely two
/// things, and both belong on its page. Not always: a headset paired over Bluetooth while
/// sitting on its dongle enumerates two capture endpoints, and they are one capsule
/// reached two ways. Drawn as they come, that was two switches, two levels and two meters
/// for a thing there is one of — and one of every pair dead, because only one link carries
/// at a time.
/// </summary>
public sealed class AudioEndpointChoiceTests
{
    private static readonly Guid Container = new("11111111-2222-3333-4444-555555555555");

    private static readonly DeviceInfo Headset = new()
    {
        Key = "test:headset",
        DriverId = "test",
        Vendor = "Test",
        Model = "Headset",
        Class = DeviceClass.Headset,
    };

    private static AudioEndpoint Endpoint(string name, AudioFlow flow, bool inUse = false) => new()
    {
        Id = name,
        Name = name,
        ContainerId = Container,
        Flow = flow,
        IsDefault = inUse,
    };

    [Fact]
    public void OneCapsuleOnTwoLinks_IsOneSetOfRows()
    {
        IReadOnlyList<AudioRow> rows = AudioCapabilities.Rows(
            [
                Endpoint("Headset (Test BT)", AudioFlow.Capture, inUse: true),
                Endpoint("Microphone (Chat-Test)", AudioFlow.Capture),
            ],
            Headset);

        // One switch, one level, one meter — and unlabelled, because there is nothing to
        // tell apart from.
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.DoesNotContain("·", row.Descriptor.Title, StringComparison.Ordinal));
    }

    [Fact]
    public void TheOneWindowsIsUsing_IsTheOneShown()
    {
        IReadOnlyList<AudioRow> rows = AudioCapabilities.Rows(
            [
                Endpoint("Headset (Test BT)", AudioFlow.Capture),
                Endpoint("Microphone (Chat-Test)", AudioFlow.Capture, inUse: true),
            ],
            Headset);

        Assert.All(rows, row => Assert.Equal("Microphone (Chat-Test)", row.EndpointId));
    }

    /// <summary>
    /// Nothing marked in use — no default of that kind on the machine at all. Then the
    /// several are drawn as they are and named apart, which is the older behaviour and
    /// the right one when there is no better answer.
    /// </summary>
    [Fact]
    public void WithNothingInUse_TheSeveralAreDrawnApart()
    {
        IReadOnlyList<AudioRow> rows = AudioCapabilities.Rows(
            [
                Endpoint("Headset (Test BT)", AudioFlow.Capture),
                Endpoint("Microphone (Chat-Test)", AudioFlow.Capture),
            ],
            Headset);

        Assert.Equal(6, rows.Count);
    }

    /// <summary>
    /// A microphone's own row states a fact; an output's offers a switch.
    ///
    /// Both read the same way, and for an output that is the whole story. For a
    /// microphone the switch was a promise the page cannot keep: a headset muted at the
    /// earcup goes on sending room noise, and twenty seconds of flipping moved no
    /// register, no HID report and no flag. What is left worth saying is what the stream
    /// is doing, and that is a reading rather than a control.
    /// </summary>
    [Fact]
    public void AMicrophonesOwnRow_IsAReadingRatherThanASwitch()
    {
        IReadOnlyList<AudioRow> rows = AudioCapabilities.Rows(
            [
                Endpoint("Microphone (Test)", AudioFlow.Capture, inUse: true),
                Endpoint("Speakers (Test)", AudioFlow.Render, inUse: true),
            ],
            Headset);

        Capability microphone = rows.Single(r =>
            r.Control == AudioControl.OnOff && r.Descriptor.Domain == CapabilityDomain.Microphone).Descriptor;

        Assert.Equal(CapabilityPrimitive.Readout, microphone.Primitive);
        Assert.False(microphone.IsWritable);

        // And it says which of the two states it is in with a word of its own: a row
        // reading "Microphone - yes" answers a question nobody asked.
        Assert.Equal(["Muted", "On"], microphone.Choices.OrderBy(c => c.Index).Select(c => c.Label));

        Capability sound = rows.Single(r =>
            r.Control == AudioControl.OnOff && r.Descriptor.Domain == CapabilityDomain.Audio).Descriptor;

        Assert.Equal(CapabilityPrimitive.Toggle, sound.Primitive);
        Assert.True(sound.IsWritable);
    }

    /// <summary>
    /// Two kinds are not two of a kind. A microphone's capsule and the socket on its base
    /// are a capture and a render endpoint, and collapsing has nothing to say about them.
    /// </summary>
    [Fact]
    public void ACaptureAndARender_AreBothKept()
    {
        IReadOnlyList<AudioRow> rows = AudioCapabilities.Rows(
            [
                Endpoint("Microphone (Test)", AudioFlow.Capture, inUse: true),
                Endpoint("Speakers (Test)", AudioFlow.Render, inUse: true),
            ],
            Headset);

        Assert.Equal(3 + 2, rows.Count);
    }
}
