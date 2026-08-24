using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audio;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// Core Audio is the widest door in the application — three categories of device draw
/// their baseline page from it — and every one of these cases is about a way it could
/// quietly attach the wrong rows to the wrong device.
///
/// The strings are the ones this desk actually produced: a headset dongle that presents
/// its game and chat mixes as two endpoints, a microphone with a headphone socket, and
/// four virtual endpoints that all share the container id meaning "no container".
/// </summary>
public sealed class AudioContributorTests
{
    private static readonly Guid Headset = new("a61215c5-cd1d-5c96-b079-f68036cbcdc6");
    private static readonly Guid Microphone = new("ee0f6396-b15b-58a1-8fde-69f0c57d7fb8");
    private static readonly Guid Nowhere = new("00000000-0000-0000-ffff-ffffffffffff");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoOutputsOnOneDevice_BecomeTwoRowsNamedAfterTheirChannel()
    {
        Fixture fixture = Desk();

        IDeviceConnection connection = await AttachAsync(fixture, MaxwellInfo);

        string[] ids = [.. connection.Capabilities.Select(c => c.Id)];

        // Collapsing the two into one "volume" would drive whichever endpoint matched
        // first and leave the other where it was — the complaint about every other
        // utility that does this.
        Assert.Contains(CapabilityIds.AudioSystemVolume, ids);
        Assert.Contains($"{CapabilityIds.AudioSystemVolume}.2", ids);

        string[] titles = [.. connection.Capabilities.Select(c => c.Title)];

        Assert.Contains("Windows volume · Chat", titles);
        Assert.Contains("Windows volume · Game", titles);
    }

    [Fact]
    public async Task ADeviceThatReportsOnlyUsbIds_StillFindsItsEndpoints()
    {
        Fixture fixture = Desk();

        // The Audeze driver found the headset through its HID interface and never looked
        // at a container, so matching on the container alone would leave the headset with
        // no volume at all. The endpoint's container is resolved back to usb ids first.
        IDeviceConnection connection = await AttachAsync(fixture, MaxwellInfo);

        Assert.NotEmpty(connection.Capabilities);
    }

    [Fact]
    public async Task ADeviceKeyedByContainer_FindsItsEndpointsToo()
    {
        Fixture fixture = Desk();

        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        Assert.Contains(CapabilityIds.MicSystemLevel, connection.Capabilities.Select(c => c.Id));
    }

    [Fact]
    public async Task PlaybackOnAMicrophone_IsTheHeadphoneSocketAndSaysSo()
    {
        Fixture fixture = Desk();

        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        Capability playback = connection.Capabilities.Single(c => c.Id == CapabilityIds.AudioSystemVolume);

        // Windows calls it "Speakers (HyperX QuadCast S)" because Windows has no word for
        // the socket on the base of a microphone. The page has one.
        Assert.Equal("Monitoring volume", playback.Title);
    }

    [Fact]
    public async Task VirtualEndpoints_AreNeverAttachedToAnything()
    {
        Fixture fixture = Desk();

        // Capture software, virtual cables and streaming sinks all report the container
        // that means "no container". Treating it as real merges every one of them into a
        // single imaginary peripheral.
        var pretender = new DeviceInfo
        {
            Key = "pretend",
            DriverId = "test",
            Vendor = "Nobody",
            Model = "Nothing",
            HardwareIds = new HashSet<string>(StringComparer.Ordinal) { $"container:{Nowhere:D}" },
        };

        Assert.Null(await fixture.Contributor.AttachAsync(pretender, Ct));
    }

    [Fact]
    public async Task ADeviceWithNoAudioAtAll_GetsNoConnection()
    {
        Fixture fixture = Desk();

        var mouse = new DeviceInfo
        {
            Key = "mouse",
            DriverId = "logitech.hidpp",
            Vendor = "Logitech",
            Model = "PRO X 2",
            HardwareIds = new HashSet<string>(StringComparer.Ordinal) { "usb:046d:c54d" },
        };

        Assert.Null(await fixture.Contributor.AttachAsync(mouse, Ct));
    }

    [Fact]
    public async Task AWrite_ReachesTheEndpointThatRowBelongsTo()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, MaxwellInfo);

        // Ordered by name, so ".2" is Game and the plain id is Chat.
        Assert.True(await connection.WriteAsync($"{CapabilityIds.AudioSystemVolume}.2", CapabilityValue.Of(70), Ct));

        Assert.Equal(70, fixture.Audio.Channels["game"].Volume);
        Assert.Equal(44, fixture.Audio.Channels["chat"].Volume);
    }

    [Fact]
    public async Task AnEndpointHeldByAnotherProcess_IsSkippedAndTheRestStillAttach()
    {
        Fixture fixture = Desk();
        fixture.Audio.Refuse.Add("chat");

        IDeviceConnection connection = await AttachAsync(fixture, MaxwellInfo);

        string[] titles = [.. connection.Capabilities.Select(c => c.Title)];

        Assert.Contains("Windows volume · Game", titles);
        Assert.DoesNotContain("Windows volume · Chat", titles);
    }

    [Fact]
    public async Task DisposingTheConnection_ClosesEveryEndpointItOpened()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, MaxwellInfo);

        connection.Dispose();

        // Its own three, and nobody else's: the microphone across the desk is not this
        // connection's to close.
        Assert.All(
            ((string[])["chat", "game", "boom"]).Select(id => fixture.Audio.Channels[id]),
            c => Assert.True(c.Disposed));

        Assert.False(fixture.Audio.Channels["quad-in"].Disposed);
    }

    [Fact]
    public async Task ReadingBack_ReportsWhatTheEndpointHolds()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        Assert.Equal(CapabilityValue.Of(100), await connection.ReadAsync(CapabilityIds.MicSystemLevel, Ct));

        // True for a microphone that is working. The endpoint's own knob is a mute and
        // reads false; the row is the other way up, and the driver is where that turns
        // over.
        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    // ---------- the capsule's own button, seen through the stream ----------

    /// <summary>
    /// The complaint this answers: muting a QuadCast at its touch panel changed nothing
    /// on its page. The button does not reach Core Audio and does not move Windows' mute
    /// — that was measured twice, and once more on the vendor HID page, where the device
    /// does announce it and where a driver for one manufacturer would have to go to hear
    /// it.
    ///
    /// The stream says the same thing for nothing: a muted capsule sends exact zeroes.
    /// </summary>
    [Fact]
    public async Task AMutedCapsule_TurnsTheSwitchOffWithoutWindowsKnowing()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);

        // Windows still says the endpoint is not muted, and it is right — it is not.
        Assert.False(fixture.Audio.Channels["quad-in"].Mute);

        await SilenceAsync(connection, meter, packets: 40);

        Assert.Equal(CapabilityValue.Of(false), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    [Fact]
    public async Task ALiveCapsule_LeavesTheSwitchAlone()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);
        meter.PacketsPerRead = 40;

        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>
    /// A quarter of a second of nothing is a dropout, not a decision. Flipping the switch
    /// on the first empty packet would have it blinking through every frame the machine
    /// ran long, which is worse than the fault this fixes.
    /// </summary>
    [Fact]
    public async Task AMomentOfSilence_IsNotEnoughToCallItMuted()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);

        await SilenceAsync(connection, meter, packets: 5);

        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>
    /// Nobody is listening, so there is nothing to go on — and "no evidence" must not be
    /// read as "muted". Otherwise every switch in the tray would show off, because the
    /// tray never opens a stream.
    /// </summary>
    [Fact]
    public async Task WithNothingStreaming_TheSwitchIsJustTheEndpoint()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        Assert.Empty(fixture.Audio.Listening);
        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>
    /// The headphone socket on the microphone's base has no capsule and no stream, and a
    /// silent one must never reach across to it.
    /// </summary>
    [Fact]
    public async Task TheHeadphoneSocket_IsNotJudgedByTheCapsule()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);
        await SilenceAsync(connection, meter, packets: 40);

        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.AudioSystemOn, Ct));
    }

    /// <summary>
    /// The one that was got wrong. Muting the endpoint in Windows puts a QuadCast's light
    /// out, because the device answers the flag in hardware; press the button on the
    /// device afterwards and the capsule comes back while the flag stays set. Asking the
    /// flag first drew a switch that said off above a meter reading 52%.
    ///
    /// The stream is the ground truth about whether a microphone is passing sound. The
    /// flag is a request somebody made earlier.
    /// </summary>
    [Fact]
    public async Task SoundComingThrough_BeatsWhateverTheMuteFlagSays()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);

        // Muted in Windows, and passing sound all the same.
        Assert.True(fixture.Audio.Channels["quad-in"].SetMute(true));
        meter.PacketsPerRead = 40;
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>
    /// Windows' own mute zeroes the capture stream too, so packets that arrived while it
    /// was on say nothing about the capsule. Keeping that verdict past the unmute left the
    /// switch reading off for a moment after the click that turned it on.
    /// </summary>
    [Fact]
    public async Task TurningItBackOn_DoesNotArgueWithTheClick()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);
        await SilenceAsync(connection, meter, packets: 40);

        Assert.True(await connection.WriteAsync(CapabilityIds.MicSystemOn, CapabilityValue.Of(true), Ct));

        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>
    /// The fault that made the switch unusable. With the capsule live and Windows' flag
    /// already set to muted — which is where the device's own button leaves them —
    /// asking for "off" wrote the flag the value it already held, the device was sent
    /// nothing, and the switch sprang back on.
    ///
    /// A pulse through the opposite value gives the device a transition to follow. The
    /// value it passes through is the one the microphone is already in, so nothing is
    /// exposed on the way.
    /// </summary>
    [Fact]
    public async Task AFlagAlreadyWhereItIsAsked_IsPulsedSoTheDeviceHearsIt()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);
        FakeChannel capsule = fixture.Audio.Channels["quad-in"];

        // Muted in Windows, live at the device: the state the button leaves behind.
        _ = capsule.SetMute(true);
        meter.PacketsPerRead = 40;
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);
        capsule.Mutes.Clear();

        Assert.True(await connection.WriteAsync(CapabilityIds.MicSystemOn, CapabilityValue.Of(false), Ct));

        Assert.Equal([false, true], capsule.Mutes);
        Assert.True(capsule.Mute);
    }

    /// <summary>
    /// For the moment after a click, the click wins. The packets in flight describe the
    /// microphone as it was — seventy milliseconds of them, measured — and the verdict
    /// waits a quarter of a second of silence before it will call anything muted, so a
    /// switch that believed the stream at that moment argued with the hand that moved it.
    /// </summary>
    [Fact]
    public async Task RightAfterAClick_TheSwitchShowsWhatWasClicked()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);
        meter.PacketsPerRead = 40;
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        Assert.True(await connection.WriteAsync(CapabilityIds.MicSystemOn, CapabilityValue.Of(false), Ct));

        // The stream has not caught up and says so, loudly: forty packets of sound.
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        Assert.Equal(CapabilityValue.Of(false), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>
    /// And it ends the moment the stream agrees, rather than running the clock down: a
    /// switch that worked has to feel immediate.
    /// </summary>
    [Fact]
    public async Task OnceTheStreamAgrees_TheHoldIsOver()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await ListenAsync(connection, fixture);

        Assert.True(await connection.WriteAsync(CapabilityIds.MicSystemOn, CapabilityValue.Of(false), Ct));

        await SilenceAsync(connection, meter, packets: 40);
        Assert.Equal(CapabilityValue.Of(false), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));

        // The device is switched back on at its own button, and the switch follows without
        // waiting out the hold that has already been satisfied.
        meter.Empty = false;
        meter.Level = 0.1;
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        Assert.Equal(CapabilityValue.Of(true), await connection.ReadAsync(CapabilityIds.MicSystemOn, Ct));
    }

    /// <summary>Opens the stream and returns the meter behind it.</summary>
    private static async Task<FakeMeter> ListenAsync(IDeviceConnection connection, Fixture fixture)
    {
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        return Assert.Single(fixture.Audio.Listening);
    }

    /// <summary>Delivers a run of packets with nothing in them, in one read.</summary>
    private static async Task SilenceAsync(IDeviceConnection connection, FakeMeter meter, int packets)
    {
        meter.PacketsPerRead = packets;
        meter.Empty = true;
        meter.Level = 0;

        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);
    }

    // ---------- the level, and what listening costs ----------

    [Fact]
    public async Task OnlyWhatRecords_GetsALevelRow()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        string[] ids = [.. connection.Capabilities.Select(c => c.Id)];

        // The QuadCast has a headphone socket as well as a capsule, and a level meter on
        // the socket would be measuring what the user is listening to.
        Assert.Contains(CapabilityIds.MicSystemPeak, ids);
        Assert.DoesNotContain($"{CapabilityIds.MicSystemPeak}.2", ids);

        Capability level = connection.Capabilities.Single(c => c.Id == CapabilityIds.MicSystemPeak);

        Assert.True(level.IsLive);
        Assert.Equal(CapabilityDomain.Microphone, level.Domain);
    }

    /// <summary>
    /// The one that matters most. Attaching a device happens on every discovery sweep, for
    /// every microphone on the machine; if that opened a stream, plugging a headset in
    /// would light the in-use indicator of every microphone in the house for a moment, and
    /// nobody would ever work out why.
    /// </summary>
    [Fact]
    public async Task AttachingToAMicrophone_DoesNotListenToIt()
    {
        Fixture fixture = Desk();

        await AttachAsync(fixture, QuadCastInfo);

        Assert.Empty(fixture.Audio.Listening);
    }

    [Fact]
    public async Task AskingForTheLevel_StartsListeningAndKeepsTheOneStream()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);

        // One stream, not one per reading: the level is asked for sixteen times a second,
        // and starting and stopping a capture stream at that rate would make the taskbar
        // indicator strobe.
        FakeMeter meter = Assert.Single(fixture.Audio.Listening);

        Assert.Equal("quad-in", meter.EndpointId);
        Assert.Equal(3, meter.Reads);
    }

    [Fact]
    public async Task TheLevelIsReportedInDecibels()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        // A tenth of full scale is −20 dBFS, which is roughly ordinary speech.
        FakeMeter meter = await FirstListenAsync(fixture, connection);
        meter.Level = 0.1;

        Assert.Equal(CapabilityValue.Of(-20), await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct));

        // Full scale is the top of the scale, and digital silence is the bottom of it
        // rather than an absent reading: a muted microphone is a fact, not a failure.
        meter.Level = 1.0;
        Assert.Equal(CapabilityValue.Of(0), await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct));

        meter.Level = 0;
        Assert.Equal(CapabilityValue.Of(-60), await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct));
    }

    [Fact]
    public async Task AStreamThatDies_IsLetGoOfRatherThanAskedAgain()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await FirstListenAsync(fixture, connection);
        meter.Level = null;

        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct));
        Assert.True(meter.Disposed);

        // And the next glance starts a fresh one, because whatever took the endpoint may
        // have let go by then.
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);
        Assert.Equal(2, fixture.Audio.Listening.Count);
    }

    [Fact]
    public async Task ClosingTheConnection_StopsListening()
    {
        Fixture fixture = Desk();
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);

        FakeMeter meter = await FirstListenAsync(fixture, connection);

        connection.Dispose();

        // A device unplugged while its page is open is the one case where the user cannot
        // put the light out themselves.
        Assert.True(meter.Disposed);
    }

    [Fact]
    public async Task AnEndpointHeldExclusively_RefusesOnceRatherThanEveryFrame()
    {
        Fixture fixture = Desk();
        fixture.Audio.Refuse.Add("quad-in");

        // The endpoint still opens for settings; it is the stream that is refused, so the
        // rest of the microphone's page is unaffected.
        fixture.Audio.Refuse.Remove("quad-in");
        IDeviceConnection connection = await AttachAsync(fixture, QuadCastInfo);
        fixture.Audio.Refuse.Add("quad-in");

        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct));
        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct));

        Assert.Empty(fixture.Audio.Listening);
        Assert.Equal(CapabilityValue.Of(100), await connection.ReadAsync(CapabilityIds.MicSystemLevel, Ct));
    }

    private static async Task<FakeMeter> FirstListenAsync(Fixture fixture, IDeviceConnection connection)
    {
        await connection.ReadAsync(CapabilityIds.MicSystemPeak, Ct);
        return Assert.Single(fixture.Audio.Listening);
    }

    private static async Task<IDeviceConnection> AttachAsync(Fixture fixture, DeviceInfo device)
    {
        IDeviceConnection? connection = await fixture.Contributor.AttachAsync(device, Ct);
        Assert.NotNull(connection);
        return connection;
    }

    private static DeviceInfo MaxwellInfo => new()
    {
        Key = "audeze.race:maxwell-xbox",
        DriverId = "audeze.race",
        Vendor = "Audeze",
        Model = "Maxwell",
        Class = DeviceClass.Headset,
        HardwareIds = new HashSet<string>(StringComparer.Ordinal) { "usb:3329:4b18" },
    };

    private static DeviceInfo QuadCastInfo => new()
    {
        Key = $"system:{Microphone:D}",
        DriverId = "system",
        Vendor = "HyperX",
        Model = "HyperX QuadCast S",
        Class = DeviceClass.Microphone,
        HardwareIds = new HashSet<string>(StringComparer.Ordinal) { $"container:{Microphone:D}" },
    };

    /// <summary>This desk, as the probes found it.</summary>
    private static Fixture Desk()
    {
        var audio = new FakeAudio();

        audio.Add("chat", "Speakers (Chat-Audeze Maxwell)", Headset, AudioFlow.Render, 44);
        audio.Add("game", "Speakers (Game-Audeze Maxwell)", Headset, AudioFlow.Render, 40);
        audio.Add("boom", "Microphone (Chat-Audeze Maxwell)", Headset, AudioFlow.Capture, 50);
        audio.Add("quad-in", "Microphone (HyperX QuadCast S)", Microphone, AudioFlow.Capture, 100);
        audio.Add("quad-out", "Speakers (HyperX QuadCast S)", Microphone, AudioFlow.Render, 40);
        audio.Add("ivcam", "Microphone (e2eSoft iVCam)", Nowhere, AudioFlow.Capture, 100);
        audio.Add("steam", "Speakers (Steam Streaming Speakers)", Nowhere, AudioFlow.Render, 100);

        var system = new FakeSystem();
        system.Add(Headset, 0x3329, 0x4B18);
        system.Add(Microphone, 0x0951, 0x171D);

        return new Fixture(audio, system);
    }

    private sealed record Fixture(FakeAudio Audio, FakeSystem System)
    {
        public AudioContributor Contributor { get; } = new(Audio, System);
    }

    private sealed class FakeAudio : IAudioEndpoints
    {
        private readonly List<AudioEndpoint> _endpoints = [];

        public Dictionary<string, FakeChannel> Channels { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Refuse { get; } = new(StringComparer.Ordinal);

        public void Add(string id, string name, Guid container, AudioFlow flow, int volume)
        {
            _endpoints.Add(new AudioEndpoint { Id = id, Name = name, ContainerId = container, Flow = flow });
            Channels[id] = new FakeChannel(volume);
        }

        public IReadOnlyList<AudioEndpoint> Enumerate() => _endpoints;

        public IAudioEndpointChannel Open(string endpointId) =>
            Refuse.Contains(endpointId)
                ? throw new IOException("held by another process")
                : Channels[endpointId];

        /// <summary>Every meter handed out, so a test can check that they were let go of.</summary>
        public List<FakeMeter> Listening { get; } = [];

        public IAudioLevelMeter Listen(string endpointId)
        {
            if (Refuse.Contains(endpointId)) throw new IOException("held by another process");

            var meter = new FakeMeter(endpointId);
            Listening.Add(meter);
            return meter;
        }
    }

    /// <summary>A microphone that is not listened to, which is the point of most of these tests.</summary>
    internal sealed class FakeMeter(string endpointId) : IAudioLevelMeter
    {
        public string EndpointId { get; } = endpointId;

        public bool Disposed { get; private set; }

        public int Reads { get; private set; }

        /// <summary>What the next read answers. Null stands for a stream that has died.</summary>
        public double? Level { get; set; } = 0.1;

        /// <summary>
        /// How many packets a read drains, and whether they carry anything.
        ///
        /// A muted capsule is not a quiet one: the stream keeps running and every packet
        /// in it is exact zeroes. Measured on a QuadCast — fifty packets a half-second,
        /// all of them empty — and it is the only thing that separates a microphone
        /// switched off at its own button from a silent room.
        /// </summary>
        public int PacketsPerRead { get; set; } = 1;

        public bool Empty { get; set; }

        private long _packets;
        private long _empty;

        public string Format => "48 kHz · 32-bit float · 1 ch";

        public (long Packets, long Silent) Tally => (_packets, _empty);

        public double? Read()
        {
            Reads++;
            _packets += PacketsPerRead;
            if (Empty) _empty += PacketsPerRead;

            return Level;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeChannel : IAudioEndpointChannel
    {
        public FakeChannel(int volume) => Volume = volume;

        public int? Volume { get; private set; }

        /// <summary>Not null: a real endpoint always has an answer to whether it is muted.</summary>
        public bool? Mute { get; private set; } = false;

        /// <summary>Silence unless a test says otherwise, which is what an idle endpoint reads.</summary>
        public int? Peak { get; set; }

        public bool Disposed { get; private set; }

        public bool SetVolume(int percent)
        {
            Volume = percent;
            return true;
        }

        /// <summary>Every mute written, in order. A device that is already where the flag
        /// says needs a transition rather than a repeat, and the order is the evidence.</summary>
        public List<bool> Mutes { get; } = [];

        public bool SetMute(bool mute)
        {
            Mutes.Add(mute);
            Mute = mute;
            return true;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSystem : ISystemDeviceEnumerator
    {
        private readonly List<SystemDeviceNode> _nodes = [];

        public void Add(Guid container, ushort vendor, ushort product) =>
            _nodes.Add(new SystemDeviceNode
            {
                InstanceId = $"USB\\VID_{vendor:X4}&PID_{product:X4}",
                ContainerId = container,
                Link = SystemDeviceLink.Usb,
                VendorId = vendor,
                ProductId = product,
            });

        public IReadOnlyList<SystemDeviceNode> Enumerate() => _nodes;
    }
}
