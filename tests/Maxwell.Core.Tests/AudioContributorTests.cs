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
        Assert.Equal(CapabilityValue.Of(false), await connection.ReadAsync(CapabilityIds.MicSystemMute, Ct));
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
    }

    private sealed class FakeChannel : IAudioEndpointChannel
    {
        public FakeChannel(int volume) => Volume = volume;

        public int? Volume { get; private set; }

        /// <summary>Not null: a real endpoint always has an answer to whether it is muted.</summary>
        public bool? Mute { get; private set; } = false;

        public bool Disposed { get; private set; }

        public bool SetVolume(int percent)
        {
            Volume = percent;
            return true;
        }

        public bool SetMute(bool mute)
        {
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
