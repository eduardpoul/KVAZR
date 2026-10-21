using Kvazr.Core.Devices;
using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;

namespace Kvazr.Core.Drivers.Keyboard;

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

    public ViaConnection(
        IHidReportPipe pipe,
        IReadOnlyList<TransportInfo> transports,
        IReadOnlyList<string>? effects = null,
        IReadOnlyList<int>? colourless = null)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(transports);

        _pipe = pipe;
        _reportLength = pipe.ReportLength;
        Transports = transports;

        // The effect row exists only where the names do. A dropdown of bare numbers is
        // not a lesser version of this row — it is a control nobody can use, since the
        // number means nothing without the list it indexes.
        // The effect leads the lighting rows: it is what the lights are doing, and the
        // three below it are how. Last of all it was also the row at the bottom of the
        // page, where its list of forty-one had nowhere to open.
        Capabilities = effects is { Count: > 0 }
            ? [Rows[0], Effect(effects), .. Rows.Skip(1).Select(c => Under(c, colourless))]
            : Rows;
    }

    /// <summary>
    /// Ties the colour to the effect above it, where the model says which effects make
    /// it beside the point. Everything else on the card is left alone: brightness and
    /// speed mean something under every effect there is.
    /// </summary>
    private static Capability Under(Capability row, IReadOnlyList<int>? colourless) =>
        row.Id == CapabilityIds.LightColor && colourless is { Count: > 0 }
            ? row with
            {
                HiddenBy = CapabilityIds.LightEffect,
                HiddenWhen = new HashSet<int>(colourless),
            }
            : row;

    private static Capability Effect(IReadOnlyList<string> effects) => new()
    {
        Id = CapabilityIds.LightEffect,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Choice,
        Title = "Effect",
        Subtitle = "What the lights do — the list this firmware was built with",
        Choices = [.. effects.Select((name, i) => new CapabilityChoice(i, name))],
        DeclaredStatus = CapabilityStatus.Untested,
        IsVolatile = true,
    };

    public IReadOnlyList<Capability> Capabilities { get; }

    private static readonly IReadOnlyList<Capability> Rows =
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

        new()
        {
            Id = CapabilityIds.LightSpeed,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Range,
            Title = "Speed",
            Subtitle = "How fast the effect moves",
            Range = new CapabilityRange { Min = 0, Max = 255 },
            DeclaredStatus = CapabilityStatus.Untested,
            IsVolatile = true,
        },

        new()
        {
            Id = CapabilityIds.LightColor,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Lighting,
            Title = "Colour",

            // The keyboard keeps hue and saturation; brightness is the row above. Folding
            // the two together would give the page two controls for one number and a
            // swatch that goes black when the light is turned down.
            Subtitle = "One colour for the whole board — VIA sets no key on its own",

            // One zone, and the control is built out of these: with no members the row
            // draws its header and nothing under it, which is what it did.
            //
            // One rather than many because that is what the protocol offers. VIA's RGB
            // matrix channel carries a single hue and saturation for the board; per-key
            // colour is a different mechanism and needs the model's own definition, the
            // same missing piece the keymap and the effect list need.
            Members = [new CapabilityMember(0, "Whole board")],

            DeclaredStatus = CapabilityStatus.Untested,
            IsVolatile = true,
        },

        // Not the effect. Its number is an index into whichever effects this firmware
        // compiled in, in the order QMK's own list happens to declare them — so the same
        // number names a different effect on the next keyboard, and a dropdown built
        // from a guess would be confidently wrong. It needs the model's own list, which
        // is the same missing piece the keymap needs.
    ];

    public IReadOnlyList<TransportInfo> Transports { get; }

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(capabilityId switch
        {
            CapabilityIds.KeysLayers => Ask(Via.GetLayerCount) is { } layers && Via.LayerCount(layers) is { } count
                ? CapabilityValue.Of(count)
                : CapabilityValue.Absent,

            CapabilityIds.LightBrightness => Lighting(RgbMatrixBrightness, 1) is { } level
                ? CapabilityValue.Of((int)level)
                : CapabilityValue.Absent,

            CapabilityIds.LightSpeed => Lighting(RgbMatrixSpeed, 1) is { } speed
                ? CapabilityValue.Of((int)speed)
                : CapabilityValue.Absent,

            CapabilityIds.LightColor => Lighting(RgbMatrixColour, 2) is { } packed
                ? CapabilityValue.Numbers(Via.Rgb((byte)(packed >> 8), (byte)packed))
                : CapabilityValue.Absent,

            CapabilityIds.LightEffect => Lighting(RgbMatrixEffect, 1) is { } effect
                ? CapabilityValue.Choice((int)effect)
                : CapabilityValue.Absent,

            _ => CapabilityValue.Absent,
        });

    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = (capabilityId, value) switch
        {
            (CapabilityIds.LightBrightness, NumberValue level) =>
                [RgbMatrixBrightness, (byte)Math.Clamp(level.Value, 0, 255)],

            (CapabilityIds.LightSpeed, NumberValue speed) =>
                [RgbMatrixSpeed, (byte)Math.Clamp(speed.Value, 0, 255)],

            (CapabilityIds.LightColor, VectorValue { Items: [NumberValue rgb, ..] }) =>
                Colour(rgb.Value),

            (CapabilityIds.LightEffect, ChoiceValue effect) =>
                [RgbMatrixEffect, (byte)Math.Clamp(effect.Index, 0, 255)],

            _ => [],
        };

        if (payload.Length == 0) return Task.FromResult(false);

        // Set, then save. Without the save the keyboard takes the value and forgets it at
        // the next power cycle, which would make a setting that survives closing the
        // application — the whole point of writing into the keyboard — quietly not.
        byte[]? set = Send(
            Via.Request(_reportLength, CustomSetValue, [RgbMatrixChannel, .. payload]),
            reply => Via.Answers(reply, CustomSetValue, RgbMatrixChannel, payload[0]));

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
    private const byte RgbMatrixEffect = 0x02;
    private const byte RgbMatrixSpeed = 0x03;
    private const byte RgbMatrixColour = 0x04;

    /// <summary>Hue and saturation, packed the way the keyboard sends them.</summary>
    private static byte[] Colour(int rgb)
    {
        (byte hue, byte saturation) = Via.Hsv(rgb);
        return [RgbMatrixColour, hue, saturation];
    }

    /// <summary>One lighting value, as many bytes wide as it is declared to be.</summary>
    private long? Lighting(byte value, int width)
    {
        byte[]? reply = Send(
            Via.Request(_reportLength, CustomGetValue, RgbMatrixChannel, value),
            r => Via.Answers(r, CustomGetValue, RgbMatrixChannel, value));

        return Via.Number(reply, 4, width);
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
