using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// What a microphone's page says about itself.
///
/// Three rules, and all three were wrong first in the same way: the page was written with
/// a headset in mind and then handed a device that is not one. A microphone leads with its
/// capsule, its playback endpoint is a headphone socket rather than "sound", and having no
/// control channel is its ordinary condition rather than a fault to explain.
/// </summary>
public sealed class MicrophonePageTests
{
    private sealed class Fixed(DeviceSnapshot snapshot) : IDeviceSession
    {
        public string DeviceKey => snapshot.Info.Key;

        public DeviceSnapshot Snapshot => snapshot;

        /// <summary>Never raised: nothing here is about what happens when a reading lands.</summary>
        public event EventHandler<DeviceSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(IReadOnlyList<string> ids, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task RefreshAllAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string id, CapabilityValue value, CancellationToken token = default) =>
            Task.FromResult(true);
    }

    private static CapabilityState Row(string id, CapabilityDomain domain, string title) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = domain,
            Primitive = CapabilityPrimitive.Range,
            Title = title,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },
        Value = CapabilityValue.Of(50),
    };

    /// <summary>
    /// The QuadCast as this desk reports it: a capture endpoint and a render one, on a
    /// cable that carries sound and no settings at all.
    /// </summary>
    private static DevicePageViewModel Microphone(TransportKind link = TransportKind.UsbCable)
    {
        var snapshot = new DeviceSnapshot
        {
            Info = new DeviceInfo
            {
                Key = "system:quadcast",
                DriverId = "system",
                Vendor = "HyperX",
                Model = "QuadCast S",
                Class = DeviceClass.Microphone,
                Transports =
                [
                    new TransportInfo { Kind = link, Label = "USB", IsPresent = true, CarriesControl = false },
                ],
            },
            Availability = DeviceAvailability.Active,
            Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
            {
                [CapabilityIds.AudioSystemVolume] =
                    Row(CapabilityIds.AudioSystemVolume, CapabilityDomain.Audio, "Monitoring volume"),
                [CapabilityIds.MicSystemLevel] =
                    Row(CapabilityIds.MicSystemLevel, CapabilityDomain.Microphone, "Recording level"),
            },
        };

        return new DevicePageViewModel(new Fixed(snapshot));
    }

    [Fact]
    public void TheMicrophoneComesFirst()
    {
        DevicePageViewModel page = Microphone();

        // The enum orders audio before microphone, which is right for a headset and
        // backwards here: the capsule is the device, the socket is an extra.
        Assert.Equal(
            [CapabilityDomain.Microphone, CapabilityDomain.Audio],
            page.Sections.Select(s => s.Domain));
    }

    [Fact]
    public void PlaybackOnAMicrophone_IsNamedAfterTheSocketItComesOutOf()
    {
        DevicePageViewModel page = Microphone();

        CapabilitySectionViewModel playback = page.Sections.Single(s => s.Domain == CapabilityDomain.Audio);

        Assert.Equal("Headphones in the microphone", playback.Title);
    }

    /// <summary>
    /// The line that said "Connected over Bluetooth: only the charge is available" over a
    /// microphone on a cable, whose every row was working.
    /// </summary>
    [Fact]
    public void ADeviceOnACable_IsNotToldItIsOnBluetooth()
    {
        Assert.Null(Microphone().StateNotice);
    }

    [Fact]
    public void ADeviceReachableOnlyOverBluetooth_StillSaysSo()
    {
        // The case the sentence was written for, and it survives: the link carries sound
        // and no settings, and there is a cable that would carry both.
        Assert.NotNull(Microphone(TransportKind.Bluetooth).StateNotice);
    }

    [Fact]
    public void TheProtocolIsNamedOnceForThePage()
    {
        DevicePageViewModel page = Microphone();

        Assert.Equal("Core Audio", page.PageProtocol?.Text);
        Assert.NotNull(page.PageNote);
    }
}
