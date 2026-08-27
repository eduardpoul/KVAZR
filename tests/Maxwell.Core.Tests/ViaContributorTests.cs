using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Keyboard;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// A keyboard that speaks VIA, and one that only looks as though it might.
///
/// The distinction is the reason the contributor asks before it offers. A collection on
/// the right usage page is a promise, not a fact: firmware can carry the page and answer
/// nothing on it, and a page of rows that never fill is worse than no rows at all.
/// </summary>
public sealed class ViaContributorTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class Pipe : IHidReportPipe
    {
        public Pipe(bool answers) => Answers = answers;

        public bool Answers { get; }

        public bool Disposed { get; private set; }

        public List<byte> Asked { get; } = [];

        public int ReportLength => 33;

        public byte[]? Request(byte[] report, Func<byte[], bool> accepts, TimeSpan within)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(accepts);

            Asked.Add(report[1]);

            if (!Answers) return null;

            byte[] reply = new byte[ReportLength];
            reply[1] = report[1];

            // The arguments come back, which is what makes a reply identifiable.
            for (int i = 2; i < 5 && i < report.Length; i++) reply[i] = report[i];

            switch (report[1])
            {
                case Via.GetProtocolVersion:
                    reply[2] = 0x00;
                    reply[3] = 0x0C;
                    break;

                case Via.GetLayerCount:
                    reply[2] = 8;
                    break;

                // Custom get value: channel, value id, then the byte.
                case 0x08:
                    reply[4] = 114;
                    break;
            }

            return accepts(reply) ? reply : null;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Factory(Pipe? pipe) : IHidReportPipeFactory
    {
        public IHidReportPipe? Open(ushort vendorId, ushort productId, ushort usagePage, byte usage) =>
            usagePage == Via.UsagePage && usage == Via.Usage ? pipe : null;
    }

    private static DeviceInfo Keyboard => new()
    {
        Key = "system:keyboard",
        DriverId = "system",
        Vendor = "NuPhy",
        Model = "Air96 V2",
        Class = DeviceClass.Keyboard,
        HardwareIds = new HashSet<string>(StringComparer.Ordinal) { "container:abc", "usb:19f5:3265" },
    };

    [Fact]
    public async Task AKeyboardThatAnswers_GetsItsRows()
    {
        var pipe = new Pipe(answers: true);

        IDeviceConnection? connection = await new ViaContributor(new Factory(pipe)).AttachAsync(Keyboard, Ct);

        Assert.NotNull(connection);
        Assert.Contains(connection.Capabilities, c => c.Id == CapabilityIds.KeysLayers);
        Assert.Contains(connection.Capabilities, c => c.Id == CapabilityIds.LightBrightness);

        Assert.Equal(CapabilityValue.Of(8), await connection.ReadAsync(CapabilityIds.KeysLayers, Ct));
        Assert.Equal(CapabilityValue.Of(114), await connection.ReadAsync(CapabilityIds.LightBrightness, Ct));

        connection.Dispose();
    }

    /// <summary>
    /// The version is asked first and the rest only if it answers. A keyboard that
    /// carries the page and says nothing gets no rows, and its pipe is let go of rather
    /// than held open for a device that will never use it.
    /// </summary>
    [Fact]
    public async Task ACollectionThatAnswersNothing_EarnsNoRows()
    {
        var pipe = new Pipe(answers: false);

        Assert.Null(await new ViaContributor(new Factory(pipe)).AttachAsync(Keyboard, Ct));

        Assert.Equal([Via.GetProtocolVersion], pipe.Asked);
        Assert.True(pipe.Disposed);
    }

    /// <summary>
    /// The effect row exists only where the names do.
    ///
    /// The wire carries an index and nothing else, and the same index names a different
    /// effect on a board built with a different set of animations — so a dropdown built
    /// without the model's list is not a lesser version of this row, it is a control
    /// nobody can use.
    /// </summary>
    [Fact]
    public async Task WithNoEffectNames_ThereIsNoEffectRow()
    {
        IDeviceConnection? connection =
            await new ViaContributor(new Factory(new Pipe(answers: true))).AttachAsync(Keyboard, Ct);

        Assert.DoesNotContain(connection!.Capabilities, c => c.Id == CapabilityIds.LightEffect);

        connection.Dispose();
    }

    [Fact]
    public async Task WithTheModelsOwnList_TheEffectRowIsNamed()
    {
        var connection = new ViaConnection(
            new Pipe(answers: true),
            [],
            ["All Off", "Solid Color", "Rainbow Moving Chevron"]);

        Capability effect = connection.Capabilities.Single(c => c.Id == CapabilityIds.LightEffect);

        Assert.Equal(CapabilityPrimitive.Choice, effect.Primitive);
        Assert.Equal(["All Off", "Solid Color", "Rainbow Moving Chevron"], effect.Choices.Select(c => c.Label));

        // And the index the wire carries is the index of the name.
        Assert.Equal([0, 1, 2], effect.Choices.Select(c => c.Index));

        connection.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ADeviceWithNoViaCollection_IsNotOurs()
    {
        Assert.Null(await new ViaContributor(new Factory(null)).AttachAsync(Keyboard, Ct));
    }

    /// <summary>
    /// The brightness is offered as writable and marked untested, because nobody has
    /// watched the write land on hardware. The amber mark says so on the page rather
    /// than the page pretending otherwise.
    /// </summary>
    [Fact]
    public async Task TheBrightness_IsWritableAndSaysItIsUnproven()
    {
        IDeviceConnection? connection =
            await new ViaContributor(new Factory(new Pipe(answers: true))).AttachAsync(Keyboard, Ct);

        Capability brightness = connection!.Capabilities.Single(c => c.Id == CapabilityIds.LightBrightness);

        Assert.True(brightness.IsWritable);
        Assert.Equal(CapabilityStatus.Untested, brightness.DeclaredStatus);

        connection.Dispose();
    }
}
