using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;

namespace Maxwell.Core.Drivers.Keyboard;

/// <summary>
/// A keyboard that speaks VIA, over whatever lead carries the raw HID collection.
///
/// A class protocol rather than a vendor one: VIA is the same on every keyboard that has
/// it, the command ids are in QMK's own header, and the brand on the case does not enter
/// into it. That is why this lives beside the audio and pointer tiers instead of under a
/// maker's name.
///
/// What it does not do is draw the keyboard. The keymap is readable — the protocol has a
/// command for it and the reads work — but a picture of a keyboard needs the matrix and
/// the legends, and VIA keeps neither: its own client fetches them from a separate index
/// of keyboard definitions, one entry per model. Until that index exists here, the rows
/// below are the ones the protocol answers on its own.
/// </summary>
internal sealed class ViaConnection : IDeviceConnection
{
    /// <summary>Long enough for a keyboard on a cable, short enough not to hold a sweep.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(600);

    private readonly IHidReportPipe _pipe;
    private readonly int _reportLength;

    public ViaConnection(IHidReportPipe pipe, IReadOnlyList<TransportInfo> transports)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(transports);

        _pipe = pipe;
        _reportLength = pipe.ReportLength;
        Transports = transports;
    }

    public IReadOnlyList<Capability> Capabilities { get; } =
    [
        new()
        {
            Id = CapabilityIds.KeysLayers,
            Domain = CapabilityDomain.Keys,
            Primitive = CapabilityPrimitive.Readout,
            Access = CapabilityAccess.Read,
            Title = "Layers",
            Subtitle = "How many the keyboard stores, whatever the application knows about them",
        },

        new()
        {
            Id = CapabilityIds.LightBrightness,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Range,
            Title = "Brightness",

            // The keyboard's own scale, not a percentage. VIA carries one byte and the
            // firmware reads it as one byte; rescaling here would mean the number on the
            // page and the number in the keyboard disagreeing about what was set.
            Range = new CapabilityRange { Min = 0, Max = 255 },

            // Written into the keyboard, so it survives the application closing — which
            // is the difference between this and every setting the operating system owns.
            Subtitle = "Kept in the keyboard, and stays after the application closes",

            // Nobody has watched this write land on real hardware yet. The amber mark
            // says so on the page rather than the page pretending otherwise.
            DeclaredStatus = CapabilityStatus.Untested,

            // The lighting keys on the board move it without telling anybody.
            IsVolatile = true,
        },
    ];

    public IReadOnlyList<TransportInfo> Transports { get; }

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(capabilityId switch
        {
            CapabilityIds.KeysLayers => Ask(Via.GetLayerCount) is { } layers && Via.LayerCount(layers) is { } count
                ? CapabilityValue.Of(count)
                : CapabilityValue.Absent,

            CapabilityIds.LightBrightness => Lighting(RgbMatrixBrightness) is { } level
                ? CapabilityValue.Of(level)
                : CapabilityValue.Absent,

            _ => CapabilityValue.Absent,
        });

    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        if (capabilityId != CapabilityIds.LightBrightness || value is not NumberValue number)
        {
            return Task.FromResult(false);
        }

        byte level = (byte)Math.Clamp(number.Value, 0, 255);

        // Set, then save. Without the save the keyboard takes the value and forgets it at
        // the next power cycle, which would make a setting that survives closing the
        // application — the whole point of writing into the keyboard — quietly not.
        byte[]? set = Send(Via.Request(_reportLength, CustomSetValue, RgbMatrixChannel, RgbMatrixBrightness, level),
            reply => Via.Answers(reply, CustomSetValue, RgbMatrixChannel, RgbMatrixBrightness));

        if (set is null || Via.Declined(set)) return Task.FromResult(false);

        _ = Send(Via.Request(_reportLength, CustomSave, RgbMatrixChannel),
            reply => Via.Answers(reply, CustomSave, RgbMatrixChannel));

        return Task.FromResult(true);
    }

    /// <summary>The custom-value channel, which is where QMK keeps its lighting.</summary>
    private const byte CustomSetValue = 0x07;
    private const byte CustomGetValue = 0x08;
    private const byte CustomSave = 0x09;
    private const byte RgbMatrixChannel = 0x03;
    private const byte RgbMatrixBrightness = 0x01;

    private int? Lighting(byte value)
    {
        byte[]? reply = Send(
            Via.Request(_reportLength, CustomGetValue, RgbMatrixChannel, value),
            r => Via.Answers(r, CustomGetValue, RgbMatrixChannel, value));

        return (int?)Via.Number(reply, 4, 1);
    }

    private byte[]? Ask(byte command, params byte[] arguments) =>
        Send(Via.Request(_reportLength, command, arguments), reply => Via.Answers(reply, command, arguments));

    private byte[]? Send(byte[] report, Func<byte[], bool> accepts)
    {
        try
        {
            return _pipe.Request(report, accepts, Patience);
        }
        catch (IOException)
        {
            // The cable went, or the keyboard was switched to wireless mid-sweep. Not an
            // error worth throwing: the session reads it as the device going quiet.
            return null;
        }
    }

    public void Dispose() => _pipe.Dispose();
}
