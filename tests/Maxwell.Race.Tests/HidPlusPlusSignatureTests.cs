using Maxwell.Race.Protocol;
using Xunit;

namespace Maxwell.Race.Tests;

/// <summary>
/// Whose answer is whose, when two readers ask one device at the same time.
///
/// This is the bug that named a mouse "Logitech b" with a control character after it. A
/// feature lookup names the feature it wants in the request, and the answer carries no
/// echo of what was asked — so two lookups in flight are identical questions with
/// interchangeable answers. The name lookup was handed the battery's index and went on to
/// read the name out of the charge reading, which was 98%, which is the letter b.
///
/// The protocol's own remedy is the nibble beside the function: the reader signs its
/// request and the device signs the answer back. These are the rules that makes it work.
/// </summary>
public sealed class HidPlusPlusSignatureTests
{
    private const byte Device = 0x01;

    [Fact]
    public void TwoReadersAskingForTwoFeatures_SendDifferentRequests()
    {
        byte[] name = HidPlusPlus.GetFeature(20, Device, HidPlusPlus.FeatureDeviceName, softwareId: 3);
        byte[] battery = HidPlusPlus.GetFeature(20, Device, HidPlusPlus.FeatureUnifiedBattery, softwareId: 4);

        // The whole difficulty in one line: but for the signature these two requests are
        // the same three bytes, because a lookup is always to index zero, function zero.
        Assert.Equal(name[1..3], battery[1..3]);
        Assert.NotEqual(name[3], battery[3]);
    }

    [Fact]
    public void AReaderTakesOnlyItsOwnAnswer()
    {
        // What the device sends back to each of them: the same shape, the same feature
        // index in it, differing only in who it is addressed to.
        byte[] mine = [0x11, Device, HidPlusPlus.RootIndex, HidPlusPlus.Marked(0x00, 3), 0x06];
        byte[] theirs = [0x11, Device, HidPlusPlus.RootIndex, HidPlusPlus.Marked(0x00, 4), 0x03];

        Assert.True(HidPlusPlus.Answers(mine, Device, HidPlusPlus.RootIndex, 0x00, softwareId: 3));
        Assert.False(HidPlusPlus.Answers(theirs, Device, HidPlusPlus.RootIndex, 0x00, softwareId: 3));
    }

    [Fact]
    public void WhatTheDeviceVolunteers_IsNobodysAnswer()
    {
        // Software id zero is the device speaking unprompted — a wheel turning, a button,
        // a connection changing. It answers no question, including one signed with zero.
        byte[] notification = [0x11, Device, 0x06, HidPlusPlus.Marked(0x00, 0), 0x62];

        Assert.False(HidPlusPlus.Answers(notification, Device, 0x06, 0x00, softwareId: 3));
    }

    [Fact]
    public void TheUnsignedFormsStillSpeakForTheApplication()
    {
        // The probes call these, and they are single-threaded by nature. They keep the
        // one shared id rather than growing a parameter nobody there would vary.
        Assert.Equal(
            HidPlusPlus.GetFeature(20, Device, HidPlusPlus.FeatureDeviceName, HidPlusPlus.SoftwareId),
            HidPlusPlus.GetFeature(20, Device, HidPlusPlus.FeatureDeviceName));

        Assert.Equal(
            HidPlusPlus.Ping(20, Device, HidPlusPlus.SoftwareId),
            HidPlusPlus.Ping(20, Device));
    }
}
