using Kvazr.App;
using Kvazr.App.ViewModels.Capabilities;
using Kvazr.App.ViewModels.Devices;
using Kvazr.Core.Devices;
using Xunit;

namespace Kvazr.App.Tests;

/// <summary>
/// What an opened tray row offers, and why that is not simply "the first three".
///
/// The panel is 360 px wide and its height is the real ceiling: a slider costs two lines,
/// its caption and the track under it, while a dropdown or a segmented switch costs one.
/// Counting controls instead of lines stopped a Maxwell at ChatMix, volume and the
/// equaliser preset, and left the noise reduction — one line, and the thing you reach for
/// when someone says they can hear your keyboard — off a panel that had room for it.
/// </summary>
public sealed class FlyoutRowTests
{
    private sealed class Registry : IDeviceRegistry
    {
        public IReadOnlyList<DeviceSnapshot> Devices => [];

        public event EventHandler? DevicesChanged;

        public IDeviceSession? Session(string deviceKey) => null;

        public Task DiscoverAsync(CancellationToken cancellationToken = default)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private static CapabilityState Range(string id, string title, string? enabledBy = null) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Access = CapabilityAccess.ReadWrite,
            Title = title,
            Range = new CapabilityRange { Min = 0, Max = 20 },
            EnabledBy = enabledBy,
        },
        Value = CapabilityValue.Of(10),
    };

    private static CapabilityState Choice(string id, string title, params string[] options) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Choice,
            Access = CapabilityAccess.ReadWrite,
            Title = title,
            Choices = [.. options.Select((name, i) => new CapabilityChoice(i, name))],
        },
        Value = CapabilityValue.Choice(0),
    };

    private static DeviceSnapshot Maxwell() => new()
    {
        Info = new DeviceInfo
        {
            Key = "audeze.race:maxwell",
            DriverId = "audeze.race",
            Vendor = "Audeze",
            Model = "Maxwell",
            Class = DeviceClass.Headset,
        },
        Availability = DeviceAvailability.Active,
        Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
        {
            [CapabilityIds.MixChat] = Range(CapabilityIds.MixChat, "ChatMix"),
            [CapabilityIds.AudioVolume] = Range(CapabilityIds.AudioVolume, "Общая громкость"),
            [CapabilityIds.AudioEqPreset] = Choice(CapabilityIds.AudioEqPreset, "Пресет эквалайзера", "Audeze", "Bass"),
            [CapabilityIds.MicNoiseReduction] =
                Choice(CapabilityIds.MicNoiseReduction, "Шумоподавление", "Выключено", "Низкое", "Высокое"),
            [CapabilityIds.MicSidetone] = Choice(CapabilityIds.MicSidetone, "Сайдтон", "Выкл", "Вкл"),
            [CapabilityIds.MicSystemOn] =
                Switch(CapabilityIds.MicSystemOn, CapabilityDomain.Microphone, "Микрофон"),
            [CapabilityIds.MicSystemLevel] =
                Range(CapabilityIds.MicSystemLevel, "Уровень записи", CapabilityIds.MicSystemOn),
        },
    };

    /// <summary>
    /// A headset's panel, filled to the line.
    ///
    /// The microphone's switch and level are the reason the budget is nine rather than
    /// six: ChatMix, volume, preset and noise reduction spend six between them, so
    /// wherever the two stood in the order they arrived to find nothing left. They are
    /// here now, under the device's own microphone setting.
    /// </summary>
    [Fact]
    public void AHeadsetsPanel_HoldsTheMicrophoneAndStopsAtTheSidetone()
    {
        using var row = new FlyoutRowViewModel(Maxwell(), new Registry());
        row.ToggleCommand.Execute(null);

        Assert.Equal(
            [
                CapabilityIds.MixChat,
                CapabilityIds.AudioVolume,
                CapabilityIds.AudioEqPreset,
                CapabilityIds.MicNoiseReduction,
                CapabilityIds.MicSystemOn,
                CapabilityIds.MicSystemLevel,
            ],
            row.Quick.Select(q => q.Id));

        // Sidetone would be a tenth line. It is near the end of the order for a reason —
        // a sidetone is set once and left — so it is still the one that goes.
        Assert.DoesNotContain(row.Quick, q => q.Id == CapabilityIds.MicSidetone);
    }

    /// <summary>
    /// A level under a switch that is off cannot be dragged, in the panel as on the page.
    ///
    /// The page joins every control to the switch the driver named, once all its rows
    /// exist. The panel built its rows and never did, so a microphone's recording level
    /// sat there fully draggable under a switch that was off — while the same slider on
    /// the page beside it was greyed. Two answers to one question, from one reading.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ALevelInThePanel_FollowsTheSwitchAboveIt(bool micOn, bool gated)
    {
        using var row = new FlyoutRowViewModel(QuadCast(micOn), new Registry(), Extras(monitoring: false));
        row.ToggleCommand.Execute(null);

        CapabilityViewModel level = row.Quick.Single(q => q.Id == CapabilityIds.MicSystemLevel);

        Assert.Equal(gated, level.IsGated);
    }

    /// <summary>And it follows it afterwards, not only at the moment the panel was built.</summary>
    [Fact]
    public void TheLevel_GreysWhenTheSwitchIsTurnedOff()
    {
        using var row = new FlyoutRowViewModel(QuadCast(), new Registry(), Extras(monitoring: false));
        row.ToggleCommand.Execute(null);

        var mute = (ToggleCapabilityViewModel)row.Quick.Single(q => q.Id == CapabilityIds.MicSystemOn);
        CapabilityViewModel level = row.Quick.Single(q => q.Id == CapabilityIds.MicSystemLevel);

        Assert.False(level.IsGated);

        mute.IsOn = false;

        Assert.True(level.IsGated);
    }

    // ---------- a microphone ----------

    private static CapabilityState Switch(string id, CapabilityDomain domain, string title, bool on = true) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = domain,
            Primitive = CapabilityPrimitive.Toggle,
            Access = CapabilityAccess.ReadWrite,
            Title = title,
        },
        Value = CapabilityValue.Of(on),
    };

    private static DeviceSnapshot QuadCast(bool micOn = true) => new()
    {
        Info = new DeviceInfo
        {
            Key = "system:quadcast",
            DriverId = "system",
            Vendor = "HyperX",
            Model = "QuadCast S",
            Class = DeviceClass.Microphone,
        },
        Availability = DeviceAvailability.Active,
        Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
        {
            [CapabilityIds.MicSystemOn] =
                Switch(CapabilityIds.MicSystemOn, CapabilityDomain.Microphone, "Microphone", micOn),
            [CapabilityIds.MicSystemLevel] =
                Range(CapabilityIds.MicSystemLevel, "Recording level", CapabilityIds.MicSystemOn),
            [CapabilityIds.AudioSystemOn] =
                Switch(CapabilityIds.AudioSystemOn, CapabilityDomain.Audio, "Monitoring"),
            [CapabilityIds.AudioSystemVolume] =
                Range(CapabilityIds.AudioSystemVolume, "Monitoring volume", CapabilityIds.AudioSystemOn),

            // The real one has these, and they are why this fixture carries them: with
            // the capsule's two rows taking two lines, there is room left over and the
            // shared order would happily spend it here.
            [CapabilityIds.LightOn] = Switch(CapabilityIds.LightOn, CapabilityDomain.Light, "Lighting"),
            [CapabilityIds.LightBrightness] = Range(CapabilityIds.LightBrightness, "Brightness"),
        },
    };

    private static Platform.TrayExtras Extras(bool monitoring)
    {
        var store = new Harness.Store();
        var extras = new Platform.TrayExtras(store);

        if (monitoring) extras.SetMonitoringAsync("system:quadcast", true).GetAwaiter().GetResult();

        return extras;
    }

    [Fact]
    public void AMicrophoneLeadsWithItsOwnSwitchAndLevel()
    {
        using var row = new FlyoutRowViewModel(QuadCast(), new Registry(), Extras(monitoring: false));
        row.ToggleCommand.Execute(null);

        // The capsule is what the row would be opened for, exactly as it is what the page
        // leads with. The headphone socket is not offered until it is asked for.
        Assert.Equal(
            [CapabilityIds.MicSystemOn, CapabilityIds.MicSystemLevel],
            row.Quick.Select(q => q.Id));
    }

    [Fact]
    public void AskedFor_TheHeadphoneSocketComesToo()
    {
        using var row = new FlyoutRowViewModel(QuadCast(), new Registry(), Extras(monitoring: true));
        row.ToggleCommand.Execute(null);

        // Four rows, six lines. Room is not why this is a question rather than a default:
        // a headphone socket under a capsule is a second, unrelated pair of controls in
        // the place somebody reaches to mute themselves.
        Assert.Equal(
            [
                CapabilityIds.MicSystemOn,
                CapabilityIds.MicSystemLevel,
                CapabilityIds.AudioSystemOn,
                CapabilityIds.AudioSystemVolume,
            ],
            row.Quick.Select(q => q.Id));
    }

    /// <summary>
    /// The panel is for what somebody reaches for while doing something else: muting
    /// mid-sentence, pulling the level down, turning monitoring off. A microphone's
    /// lighting is chosen once and then looked at — it is how the thing on the desk
    /// looks, not something anybody adjusts during a call.
    ///
    /// It was only ever there because the capsule's two rows leave room for it, and room
    /// left over is a reason to leave it empty rather than to fill it.
    /// </summary>
    [Fact]
    public void AMicrophonesLighting_IsNotPanelWork()
    {
        using var row = new FlyoutRowViewModel(QuadCast(), new Registry(), Extras(monitoring: false));
        row.ToggleCommand.Execute(null);

        Assert.DoesNotContain(CapabilityIds.LightOn, row.Quick.Select(q => q.Id));
        Assert.DoesNotContain(CapabilityIds.LightBrightness, row.Quick.Select(q => q.Id));
    }

    /// <summary>
    /// A device with nothing to change says nothing about it.
    ///
    /// It used to fill the opened row with one sentence — "nothing to set, the device
    /// gives only what the row already shows" — which is a statement about this
    /// application, and it was the whole of what somebody got for clicking to see more.
    /// A reason is different: "not over this link" is about the device and can be acted
    /// on, so that one still speaks.
    /// </summary>
    [Fact]
    public void ADeviceWithNothingToChange_DoesNotExplainItself()
    {
        var mouse = new DeviceSnapshot
        {
            Info = new DeviceInfo
            {
                Key = "system:mouse",
                DriverId = "system",
                Vendor = "HP",
                Model = "Wireless Mouse",
                Class = DeviceClass.Mouse,
            },
            Availability = DeviceAvailability.Active,
        };

        using var row = new FlyoutRowViewModel(mouse, new Registry());
        row.ToggleCommand.Execute(null);

        Assert.True(row.HasNothing);
        Assert.False(row.HasReason);
        Assert.Empty(row.NothingSaid);
    }

    [Fact]
    public void WithNoSettingsBehindIt_TheRowStillOpens()
    {
        // The design previews build a panel with nothing behind it. It shows what a
        // microphone always shows and asks nobody anything.
        using var row = new FlyoutRowViewModel(QuadCast(), new Registry());
        row.ToggleCommand.Execute(null);

        Assert.Equal(
            [CapabilityIds.MicSystemOn, CapabilityIds.MicSystemLevel],
            row.Quick.Select(q => q.Id));
    }
}
