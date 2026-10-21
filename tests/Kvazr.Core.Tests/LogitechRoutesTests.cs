using Kvazr.Core.Devices;
using Kvazr.Core.Drivers.Logitech;
using Kvazr.Race.Windows;
using Xunit;

namespace Kvazr.Core.Tests;

/// <summary>
/// One mouse is one device, by whichever route it is reached.
///
/// Keyed by route, a mouse that went from its receiver onto its charging cable became two
/// rows — the cable one live and the radio one left behind as a disconnected twin — and
/// the receiver, hidden only while the mouse spoke through it, came back as a row of its
/// own. These pin what the driver tells the registry about one sighting: every route as a
/// transport, each up or down, and the receiver's id claimed whenever its pairing table
/// names the mouse.
/// </summary>
public sealed class LogitechRoutesTests
{
    private const string Key = "hidpp:unit:2121B490";
    private const ushort Mouse = 0xC09B;
    private const ushort Receiver = 0xC54D;

    private static HidPlusPlusDriver.Route Path(byte index) => new($"path-{index}", 20, 20, index);

    private static HidPlusPlusDriver.Sighting Remembered() => new(Key)
    {
        Name = "PRO X 2",
        Type = HidPlusPlusReader.Kind.Mouse,
        Receiver = Receiver,
        Slot = 1,
    };

    [Fact]
    public void OnItsCable_TheMouseKeepsItsReceiver_AsALeadThatIsDown()
    {
        HidPlusPlusDriver.Sighting seen = Remembered();
        seen.Cable = Mouse;
        seen.CableRoute = Path(HidPlusPlusReader.DirectDevice);

        DeviceInfo info = HidPlusPlusDriver.Describe(seen, catalogue: null);

        Assert.Equal(Key, info.Key);
        Assert.Contains("usb:046d:c09b", info.HardwareIds);
        Assert.Contains("usb:046d:c54d", info.HardwareIds);
        Assert.Collection(
            info.Transports,
            cable =>
            {
                Assert.Equal(TransportKind.UsbCable, cable.Kind);
                Assert.True(cable.IsPresent);
            },
            dongle =>
            {
                Assert.Equal(TransportKind.Dongle, dongle.Kind);
                Assert.False(dongle.IsPresent);
            });
    }

    [Fact]
    public void OnTheAir_TheDongleIsTheLiveLead()
    {
        HidPlusPlusDriver.Sighting seen = Remembered();
        seen.AirRoute = Path(1);

        DeviceInfo info = HidPlusPlusDriver.Describe(seen, catalogue: null);

        TransportInfo dongle = Assert.Single(info.Transports);
        Assert.Equal(TransportKind.Dongle, dongle.Kind);
        Assert.True(dongle.IsPresent);
        Assert.Contains("usb:046d:c54d", info.HardwareIds);
    }

    /// <summary>
    /// Switched off, the mouse is still the mouse its receiver is for: listed under its own
    /// name with every lead down, which the session reads as offline, and still holding the
    /// receiver's id so the receiver does not take its place in the list.
    /// </summary>
    [Fact]
    public void SwitchedOff_TheMouseIsStillListed_WithEveryLeadDown()
    {
        DeviceInfo info = HidPlusPlusDriver.Describe(Remembered(), catalogue: null);

        Assert.Equal(Key, info.Key);
        Assert.Equal("PRO X 2", info.Model);
        Assert.Equal(DeviceClass.Mouse, info.Class);
        Assert.Contains("usb:046d:c54d", info.HardwareIds);
        Assert.All(info.Transports, t => Assert.False(t.IsPresent));
        Assert.NotEmpty(info.Transports);
    }

    [Fact]
    public void TheLiveRoute_IsTheCable_WhenThereIsOne()
    {
        HidPlusPlusDriver.Sighting seen = Remembered();
        seen.AirRoute = Path(1);
        seen.CableRoute = Path(HidPlusPlusReader.DirectDevice);

        Assert.Equal(HidPlusPlusReader.DirectDevice, seen.Live?.Index);
    }
}
