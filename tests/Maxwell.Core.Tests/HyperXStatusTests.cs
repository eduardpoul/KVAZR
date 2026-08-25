using System.Collections.Concurrent;

using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.HyperX;
using Maxwell.Race.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The QuadCast's status channel, and the one row it earns.
///
/// Every constant here was measured on the device rather than read anywhere: the report
/// number, the two sub-commands, the mute polarity by watching the light, and the four
/// pattern values by asking for each icon by its drawing. The tests exist to keep those
/// measurements from being quietly re-guessed later.
/// </summary>
public sealed class HyperXStatusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ItAttachesToTheMicrophone_ByEitherOfItsUsbFunctions()
    {
        var listeners = new FakeListeners();
        var contributor = new HyperXContributor(listeners);

        // The microphone appears as two USB functions: the audio side Windows names the
        // device from, and the control side the status channel lives on. The page is
        // built from the first and the channel from the second, so either has to do.
        Assert.NotNull(await contributor.AttachAsync(Device("usb:0951:171d"), Ct));
        Assert.NotNull(await contributor.AttachAsync(Device("usb:0951:171f"), Ct));
    }

    [Fact]
    public async Task ItIsAskedAboutEveryDeviceAndSaysNoToMostOfThem()
    {
        var contributor = new HyperXContributor(new FakeListeners());

        Assert.Null(await contributor.AttachAsync(Device("usb:046d:c54d"), Ct));
        Assert.Null(await contributor.AttachAsync(Device("container:0f9b41c2-0000-0000-0000-000000000000"), Ct));

        // A different HyperX product. This contributor knows one status channel and the
        // sub-commands on it; a headset would answer differently or not at all.
        Assert.Null(await contributor.AttachAsync(Device("usb:0951:16ea"), Ct));
    }

    [Fact]
    public async Task RecognisedButWithNoChannel_IsNotAnError()
    {
        var listeners = new FakeListeners { Refuse = true };
        var contributor = new HyperXContributor(listeners);

        // An older model, or a control function Windows has not enumerated. The rest of
        // the device is a working microphone with a full baseline page either way.
        Assert.Null(await contributor.AttachAsync(Device("usb:0951:171d"), Ct));
    }

    [Fact]
    public async Task OneRow_ReadOnly_WithFourPatterns()
    {
        using IDeviceConnection connection = await AttachAsync(new FakeListeners());

        Capability row = Assert.Single(connection.Capabilities);

        Assert.Equal(CapabilityIds.MicPolarPattern, row.Id);
        Assert.Equal(CapabilityDomain.Microphone, row.Domain);

        // The dial is mechanical. A control that offered to move it would be a lie about
        // the hardware rather than a feature nobody got round to.
        Assert.Equal(CapabilityAccess.Read, row.Access);
        Assert.Equal(4, row.Choices.Count);
        Assert.Equal("Stereo", row.Choices[0].Label);
        Assert.Equal("Bidirectional", row.Choices[3].Label);
    }

    [Fact]
    public async Task UntilTheDialIsTurned_ThereIsNoValueToShow()
    {
        using IDeviceConnection connection = await AttachAsync(new FakeListeners());

        // Measured: the channel sends only on change, and asking it for the current
        // report is refused even with the right report id. So the honest answer on the
        // way up is "nothing yet" rather than a value that was never read.
        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TheSelectorIsRead_FromSubCommand11(byte pattern)
    {
        var listeners = new FakeListeners();
        using IDeviceConnection connection = await AttachAsync(listeners);

        listeners.Send([0x05, 0x11, pattern, 0, 0, 0, 0, 0]);

        await WaitFor(connection, CapabilityValue.Choice(pattern));
    }

    [Fact]
    public async Task TheMuteOnTheSameChannel_IsNotMistakenForThePattern()
    {
        var listeners = new FakeListeners();
        using IDeviceConnection connection = await AttachAsync(listeners);

        // Sub-command 0x10 on the same report number, and it means the capsule. Decoding
        // it here would put the mute's 0 or 1 into a row about which way the microphone
        // listens — and both are values this row accepts, so nothing would look wrong.
        listeners.Send([0x05, 0x10, 0x01, 0, 0, 0, 0, 0]);
        await Task.Delay(120, Ct);

        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));
    }

    [Fact]
    public async Task ClosingTheConnection_LetsGoOfTheDevice()
    {
        var listeners = new FakeListeners();
        IDeviceConnection connection = await AttachAsync(listeners);

        connection.Dispose();

        // The ear holds an open HID handle. Left running it would keep the microphone
        // after the session that owns it has gone.
        Assert.True(listeners.Opened.Single().Disposed);
    }

    /// <summary>
    /// The payoff of the whole dig. The row used to be empty until somebody turned the
    /// dial, on a page opened to look at that very row.
    ///
    /// The question is a replay of NGENUITY's own opening move, watched at four
    /// milliseconds a sample, and the answer carries the selector at byte 16.
    /// </summary>
    [Fact]
    public async Task OnTheWayUp_ItAsksTheMicrophoneWhereTheDialIs()
    {
        var listeners = new FakeListeners { Answer = Answer(2) };
        using IDeviceConnection connection = await AttachAsync(listeners);

        Assert.Equal(CapabilityValue.Choice(2), await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));

        byte[] asked = Assert.Single(listeners.Pipes.Single().Asked);
        Assert.Equal<byte[]>([0x04, 0x58, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01], asked);
    }

    /// <summary>
    /// The kind of value has to match the kind of row, and this is the case that says so
    /// out loud because nothing else does.
    ///
    /// A control with named options finds its value by looking for that index among them.
    /// Handed a plain number it finds nothing, however right the number is, and draws an
    /// empty row. Which is what happened: the page showed a dash while the harness beside
    /// it printed the correct pattern, because a harness prints whatever it is given and a
    /// control does not.
    /// </summary>
    [Fact]
    public async Task TheValueIsAChoice_BecauseTheRowIsOne()
    {
        var listeners = new FakeListeners { Answer = Answer(2) };
        using IDeviceConnection connection = await AttachAsync(listeners);

        Assert.Equal(CapabilityPrimitive.Choice, connection.Capabilities.Single().Primitive);
        Assert.IsType<ChoiceValue>(await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));
    }

    [Fact]
    public async Task WithNoRegisterToAsk_ItStillFillsOnTheFirstTurn()
    {
        var listeners = new FakeListeners();
        using IDeviceConnection connection = await AttachAsync(listeners);

        // An older model, or one that will not open for writing. Worse than asking, and
        // still a working row.
        Assert.Empty(listeners.Pipes);
        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));

        listeners.Send([0x05, 0x11, 0x03, 0, 0, 0, 0, 0]);
        await WaitFor(connection, CapabilityValue.Choice(3));
    }

    [Fact]
    public async Task AnAnswerOutOfRange_IsNotBelieved()
    {
        // Four positions and no more. A register answering with something else is
        // answering a different question, and drawing it as a pattern would invent one.
        var listeners = new FakeListeners { Answer = Answer(9) };
        using IDeviceConnection connection = await AttachAsync(listeners);

        Assert.Equal(CapabilityValue.Absent, await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));
    }

    [Fact]
    public async Task ADialTurnedWhileAsking_OutranksTheAnswer()
    {
        var listeners = new FakeListeners { Answer = Answer(0) };
        using IDeviceConnection connection = await AttachAsync(listeners);

        // The reply describes the state of a fifth of a second ago. A hand that moved the
        // dial in the meantime is the newer fact, and the ear is already running.
        listeners.Send([0x05, 0x11, 0x03, 0, 0, 0, 0, 0]);
        await WaitFor(connection, CapabilityValue.Choice(3));
    }

    [Fact]
    public async Task ClosingIt_LetsGoOfTheRegisterToo()
    {
        var listeners = new FakeListeners { Answer = Answer(0) };
        IDeviceConnection connection = await AttachAsync(listeners);

        connection.Dispose();

        Assert.True(listeners.Pipes.Single().Disposed);
    }

    /// <summary>
    /// A reply with the selector where the microphone puts it.
    ///
    /// Counted from the front of the buffer, report id included, which is where the first
    /// version of this got it wrong: the fake was built to the same off-by-one as the code
    /// it was checking, so both agreed and the row read stereo whatever the dial said. The
    /// bytes below are the ones a QuadCast actually sends.
    /// </summary>
    private static byte[] Answer(byte pattern)
    {
        byte[] reply = new byte[65];
        reply[13] = 0x01;
        reply[17] = pattern;
        return reply;
    }

    // ---------- fixture ----------

    private static async Task<IDeviceConnection> AttachAsync(FakeListeners listeners)
    {
        IDeviceConnection? connection =
            await new HyperXContributor(listeners).AttachAsync(Device("usb:0951:171d"), Ct);

        return connection!;
    }

    /// <summary>The ear runs on its own thread, so the value lands a moment after the report.</summary>
    private static async Task WaitFor(IDeviceConnection connection, CapabilityValue wanted)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct) == wanted) return;

            await Task.Delay(20, Ct);
        }

        Assert.Equal(wanted, await connection.ReadAsync(CapabilityIds.MicPolarPattern, Ct));
    }

    private static DeviceInfo Device(params string[] hardwareIds) => new()
    {
        Key = "system:quadcast",
        DriverId = "system",
        Vendor = "HyperX",
        Model = "QuadCast S",
        Class = DeviceClass.Microphone,
        HardwareIds = new HashSet<string>(hardwareIds, StringComparer.Ordinal),
    };

    private sealed class FakeListeners : IHidListenerFactory
    {
        public bool Refuse { get; init; }

        /// <summary>What the register answers, or null for a device that will not be asked.</summary>
        public byte[]? Answer { get; init; }

        public List<FakeListener> Opened { get; } = [];

        public List<FakePipe> Pipes { get; } = [];

        public IHidVendorPipe? OpenPipe(ushort vendorId, ushort usagePage)
        {
            Assert.Equal(0x0951, vendorId);

            // The register that answers, which is a different collection from the one
            // that announces.
            Assert.Equal(0xFF90, usagePage);

            if (Refuse || Answer is null) return null;

            var pipe = new FakePipe(Answer);
            Pipes.Add(pipe);
            return pipe;
        }

        public IHidListener? Open(ushort vendorId, ushort usagePage)
        {
            Assert.Equal(0x0951, vendorId);

            // The status channel, not the telephony page that stayed silent through
            // ninety seconds of tapping, and not the lighting pipe.
            Assert.Equal(0xFFFF, usagePage);

            if (Refuse) return null;

            var listener = new FakeListener();
            Opened.Add(listener);
            return listener;
        }

        public void Send(byte[] report)
        {
            foreach (FakeListener listener in Opened) listener.Queue.Enqueue(report);
        }
    }

    private sealed class FakePipe(byte[] answer) : IHidVendorPipe
    {
        public List<byte[]> Asked { get; } = [];

        public bool Disposed { get; private set; }

        public byte[]? Ask(byte[] command, TimeSpan within)
        {
            Asked.Add(command);
            return answer;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeListener : IHidListener
    {
        public ConcurrentQueue<byte[]> Queue { get; } = new();

        public bool Disposed { get; private set; }

        public byte[]? Listen(TimeSpan wait) => Queue.TryDequeue(out byte[]? report) ? report : null;

        public void Dispose() => Disposed = true;
    }
}
