using Maxwell.App.ViewModels.Capabilities;
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

        public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string id, CapabilityValue value, CancellationToken token = default) =>
            Task.FromResult(true);
    }

    private static CapabilityState Row(
        string id,
        CapabilityDomain domain,
        string title,
        string? enabledBy = null) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = domain,
            Primitive = CapabilityPrimitive.Range,
            Title = title,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
            EnabledBy = enabledBy,
        },
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        Value = CapabilityValue.Of(50),
    };

    private static CapabilityState Switch(string id, CapabilityDomain domain, string title, bool on) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = domain,
            Primitive = CapabilityPrimitive.Toggle,
            Title = title,
        },
        Value = CapabilityValue.Of(on),
    };

    /// <summary>
    /// The QuadCast as this desk reports it: a capture endpoint and a render one, on a
    /// cable that carries sound and no settings at all.
    /// </summary>
    private static DevicePageViewModel Microphone(TransportKind link = TransportKind.UsbCable, bool on = true)
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
                [CapabilityIds.AudioSystemOn] =
                    Switch(CapabilityIds.AudioSystemOn, CapabilityDomain.Audio, "Monitoring", on),
                [CapabilityIds.AudioSystemVolume] = Row(
                    CapabilityIds.AudioSystemVolume, CapabilityDomain.Audio, "Monitoring volume",
                    CapabilityIds.AudioSystemOn),
                [CapabilityIds.MicSystemOn] =
                    Switch(CapabilityIds.MicSystemOn, CapabilityDomain.Microphone, "Microphone", on),
                [CapabilityIds.MicSystemLevel] = Row(
                    CapabilityIds.MicSystemLevel, CapabilityDomain.Microphone, "Recording level",
                    CapabilityIds.MicSystemOn),
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

    // ---------- the switch and what it governs ----------

    [Fact]
    public void TheSwitchComesBeforeTheLevelItGoverns()
    {
        CapabilitySectionViewModel mic = Microphone().Sections
            .Single(s => s.Domain == CapabilityDomain.Microphone);

        // A condition read after the thing it governs is a slider people drag first and
        // understand second.
        Assert.Equal(
            [CapabilityIds.MicSystemOn, CapabilityIds.MicSystemLevel],
            mic.Items.Select(i => i.Descriptor.Id));
    }

    [Fact]
    public void WithTheSwitchOff_TheLevelCannotBeDragged()
    {
        CapabilitySectionViewModel mic = Microphone(on: false).Sections
            .Single(s => s.Domain == CapabilityDomain.Microphone);

        CapabilityViewModel level = mic.Items.Single(i => i.Descriptor.Id == CapabilityIds.MicSystemLevel);

        Assert.True(level.IsGated);
        Assert.False(level.CanEdit);
    }

    [Fact]
    public void PressingTheSwitch_FreesTheLevelAtOnce()
    {
        CapabilitySectionViewModel mic = Microphone(on: false).Sections
            .Single(s => s.Domain == CapabilityDomain.Microphone);

        var toggle = (ToggleCapabilityViewModel)mic.Items.Single(i => i.Descriptor.Id == CapabilityIds.MicSystemOn);
        CapabilityViewModel level = mic.Items.Single(i => i.Descriptor.Id == CapabilityIds.MicSystemLevel);

        // Not when the reading comes back a few hundred milliseconds later: for those
        // milliseconds the slider would be live over a device that is not listening.
        toggle.IsOn = true;

        Assert.False(level.IsGated);
    }

    /// <summary>
    /// The slider's own mute icon writes the floor and remembers the position, which is an
    /// imitation for protocols that have no mute. Beside a real switch it would be a second
    /// mute on the same row, and the two would disagree the moment either was used.
    /// </summary>
    [Fact]
    public void ALevelWithARealSwitch_DoesNotAlsoCarryTheImitationOne()
    {
        CapabilitySectionViewModel sound = Microphone().Sections
            .Single(s => s.Domain == CapabilityDomain.Audio);

        var volume = (RangeCapabilityViewModel)sound.Items.Single(
            i => i.Descriptor.Id == CapabilityIds.AudioSystemVolume);

        Assert.False(volume.CanMute);
    }

    [Fact]
    public void TheProtocolIsNamedOnceForThePage()
    {
        DevicePageViewModel page = Microphone();

        Assert.Equal("Core Audio", page.PageProtocol?.Text);
        Assert.NotNull(page.PageNote);
    }
}
