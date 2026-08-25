using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.HyperX;

/// <summary>
/// The QuadCast's own status channel, read and nothing else.
///
/// One eight-byte input report, numbered 0x05, with a sub-command in byte 1 and its value
/// in byte 2. Two sub-commands are decoded and both were measured with hands on the
/// device rather than read anywhere:
///
/// <list type="table">
///   <item><term>0x10</term><description>the capsule — 01 live, 00 muted. Confirmed by the light: it came on with 01 and went out with 00.</description></item>
///   <item><term>0x11</term><description>the polar selector — 0 stereo, 1 cardioid, 2 omnidirectional, 3 bidirectional. Confirmed by asking for each icon by its drawing rather than by its place on the dial, because the order on this one is not the order the pictures show.</description></item>
/// </list>
///
/// Only the pattern is published. The capsule already has a row, driven by Core Audio and
/// by the capture stream, and a second switch beside it would be two controls for one
/// thing that disagree the moment either is used. What this channel could add there is
/// worth noting rather than doing twice: it knows the mute without the microphone being
/// opened, where the stream witness has to hold it open to see anything.
///
/// The channel says nothing until something moves. Asking for the current report is
/// refused — measured, with the right report id, which is not how that conclusion was
/// first reached — so the row is absent until the selector is turned once. That is a fact
/// about the microphone and the page says so rather than drawing a value it does not have.
/// </summary>
internal sealed class HyperXConnection : IDeviceConnection
{
    /// <summary>Where a QuadCast announces itself. Not the telephony page, which stays silent.</summary>
    internal const ushort StatusPage = 0xFFFF;

    internal const ushort Vendor = 0x0951;

    private const byte StatusReport = 0x05;
    private const byte PolarPattern = 0x11;

    /// <summary>Where the register that answers lives. Not the status channel, which only announces.</summary>
    internal const ushort PipePage = 0xFF90;

    /// <summary>"What is your state" — NGENUITY's own opening question, byte for byte.</summary>
    private static readonly byte[] AskState = [0x04, 0x58, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01];

    /// <summary>
    /// Where the selector sits in the reply, counted from the front of the buffer.
    ///
    /// Seventeen rather than sixteen, and the difference is the report id. Windows puts it
    /// in the first byte and the device's own numbering starts after it, so a byte read
    /// off a printed dump lands one place early. This was written as sixteen first: the
    /// row then read stereo whatever the dial said, because the byte before the selector
    /// is one of the zeroes around it and zero is a valid pattern.
    /// </summary>
    private const int PatternAt = 17;

    /// <summary>Long enough for an answer measured at four milliseconds, short enough not to hold a sweep up.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(200);

    /// <summary>How long a read waits before looking at the cancellation token again.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(400);

    private readonly IHidListener _listener;
    private readonly IHidVendorPipe? _pipe;
    private readonly HyperXLighting? _lighting;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _ear;

    private int _pattern = -1;
    private bool _disposed;

    /// <summary>The slot this driver keeps its lighting under. One device, one string.</summary>
    private const string LightingSlot = "hyperx.lighting";

    private readonly IDeviceMemory? _memory;
    private readonly string _deviceKey;

    public HyperXConnection(
        IHidListener listener,
        IHidVendorPipe? pipe = null,
        IDeviceMemory? memory = null,
        string deviceKey = "")
    {
        ArgumentNullException.ThrowIfNull(listener);

        _listener = listener;
        _pipe = pipe;
        _memory = memory;
        _deviceKey = deviceKey;

        // Only where there is a register to write to. Without one the microphone keeps its
        // own lighting and the rows below are simply not published, which is the truth
        // rather than four controls that do nothing.
        _lighting = pipe is null ? null : new HyperXLighting(pipe);

        // Whatever was chosen last time, applied the moment the microphone is found
        // rather than when somebody opens its page. The lights are on the desk; waiting
        // for a window to be opened before honouring a setting would be visible.
        if (_lighting is { } lights && memory?.Recall(deviceKey, LightingSlot) is { } kept)
        {
            lights.Apply(LightingState.Parse(kept));
        }

        Capabilities = _lighting is null ? [Selector] : [Selector, .. Lights];

        _ear = Task.Run(Listen);

        // Before anything is drawn. Without it the row is empty until somebody turns the
        // dial, which on a page opened to look at that very row reads as broken.
        AskOnce();
    }

    /// <summary>
    /// Asks the register for the selector's position, once, and takes the answer only if
    /// nothing has been heard yet.
    ///
    /// Only if nothing has been heard: the ear is already running by the time this
    /// returns, and a reply about the state a fifth of a second ago must not overwrite a
    /// hand that moved the dial in between.
    /// </summary>
    private void AskOnce()
    {
        if (_pipe is null) return;

        try
        {
            if (_pipe.Ask(AskState, Patience) is not { } answer) return;
            if (answer.Length <= PatternAt) return;

            byte pattern = answer[PatternAt];
            if (pattern > 3) return;

            _ = Interlocked.CompareExchange(ref _pattern, pattern, -1);
        }
        catch (IOException)
        {
            // Taken, unplugged, or refusing to be written to. The row fills on the first
            // turn of the dial instead, which is where it started.
        }
    }

    public IReadOnlyList<Capability> Capabilities { get; }

    private static readonly Capability Selector =
        new()
        {
            Id = CapabilityIds.MicPolarPattern,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Choice,
            Access = CapabilityAccess.Read,
            Title = "Polar pattern",

            // Its own heading rather than filed under Microphone with the recording
            // level. Not because of which tier it came from — that is this application's
            // business and not the user's — but because it is a control on the body of
            // this particular microphone, and reading it next to a Windows slider
            // suggests Windows has an opinion about it.
            Section = "HyperX",

            // Where it comes from, in the words a person would use. Windows has no notion
            // of a polar pattern at all, so saying the microphone is what was asked is the
            // difference between a row that looks invented and one that looks read.
            Subtitle = "The selector on the back, read from the microphone itself",
            // Each one named for its drawing as well as for itself. The selector is
            // labelled with rings and nothing else, so somebody checking the screen
            // against the microphone is matching shapes.
            Choices =
            [
                new CapabilityChoice(0, "Stereo") { Glyph = "polar.stereo" },
                new CapabilityChoice(1, "Cardioid") { Glyph = "polar.cardioid" },
                new CapabilityChoice(2, "Omnidirectional") { Glyph = "polar.omni" },
                new CapabilityChoice(3, "Bidirectional") { Glyph = "polar.figure8" },
            ],

            // It changes under somebody's hand and never under ours, which is the whole
            // definition of volatile.
            IsVolatile = true,
        };

    /// <summary>
    /// The lighting, as five separate rows under a heading of the domain's own.
    ///
    /// Not "HyperX" like the selector above: that row is filed by its maker because it is
    /// a fact about this particular microphone's body, while lighting is lighting on any
    /// device that has it, and a section called after the maker twice on one page is a
    /// heading that has stopped saying anything.
    ///
    /// Separate because they are separate questions and a device that offered only some of
    /// them would otherwise be undrawable: the colour is a pair of zones, the effect is a
    /// list, the brightness is a scale, and whether any of it is ours at all is a switch.
    /// The interface has drawn all four shapes since before this driver existed.
    /// </summary>
    private static readonly Capability[] Lights =
    [
        new Capability
        {
            Id = CapabilityIds.LightOn,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Toggle,
            Title = "Lighting",

            // The sentence has to say what off means, because it is not darkness. The
            // microphone has an effect of its own and runs it whenever nothing is
            // overriding it, so switching this off gives the lights back rather than
            // putting them out.
            Subtitle = "Painted by this application while it runs. Off gives the microphone its own effect back",
        },
        new Capability
        {
            Id = CapabilityIds.LightEffect,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Effect",
            EnabledBy = CapabilityIds.LightOn,
            Choices =
            [
                new CapabilityChoice(0, "Static"),
                new CapabilityChoice(1, "Breathing"),
                new CapabilityChoice(2, "Blink"),
                new CapabilityChoice(3, "Wave"),
                new CapabilityChoice(4, "Rainbow"),
            ],
        },
        new Capability
        {
            Id = CapabilityIds.LightColor,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Lighting,
            Title = "Colour",
            EnabledBy = CapabilityIds.LightOn,

            // Two bars, and they are named for where they are on the microphone rather
            // than for anything in the protocol: whoever is choosing a colour is looking
            // at the device, not at the frame.
            Members =
            [
                new CapabilityMember(0, "Upper bar"),
                new CapabilityMember(1, "Lower bar"),
            ],
        },
        new Capability
        {
            Id = CapabilityIds.LightSpeed,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Range,
            Title = "Speed",
            EnabledBy = CapabilityIds.LightOn,
            Subtitle = "How fast the effect runs. Nothing to do with a steady colour",
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },
        new Capability
        {
            Id = CapabilityIds.LightBrightness,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Range,
            Title = "Brightness",
            EnabledBy = CapabilityIds.LightOn,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },
    ];

    /// <summary>None. The device is owned by whoever found it; this only adds a row.</summary>
    public IReadOnlyList<TransportInfo> Transports => [];

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (_lighting is { } lighting && Light(capabilityId, lighting.State) is { } lit)
        {
            return Task.FromResult(lit);
        }

        if (!string.Equals(capabilityId, CapabilityIds.MicPolarPattern, StringComparison.Ordinal))
        {
            return Task.FromResult(CapabilityValue.Absent);
        }

        int pattern = Volatile.Read(ref _pattern);

        // A choice rather than a number, and the distinction is not cosmetic: a control
        // with named options looks its value up by index among them, and a plain number
        // matches none of them however right it is. The row read as empty on the page
        // while the same reading printed correctly in the harness, which prints whatever
        // it is given.
        return Task.FromResult(pattern < 0 ? CapabilityValue.Absent : CapabilityValue.Choice(pattern));
    }

    /// <summary>
    /// The selector is never writable: it is a mechanical dial, there is no command that
    /// could move it, and a control that pretended otherwise would be a lie about the
    /// hardware rather than a missing feature.
    ///
    /// The lighting is, and it is written to this application rather than to the
    /// microphone — which cannot keep it. What reaches the device is a stream of frames
    /// for as long as the switch is on.
    /// </summary>
    public Task<bool> WriteAsync(
        string capabilityId, CapabilityValue value, CancellationToken cancellationToken = default)
    {
        if (_lighting is not { } lighting) return Task.FromResult(false);

        LightingState was = lighting.State;

        LightingState now = (capabilityId, value) switch
        {
            (CapabilityIds.LightOn, FlagValue flag) => was with { On = flag.Value },

            (CapabilityIds.LightEffect, ChoiceValue choice) when choice.Index is >= 0 and <= 4 =>
                was with { Effect = (LightingEffect)choice.Index },

            (CapabilityIds.LightBrightness, NumberValue number) =>
                was with { Brightness = Math.Clamp(number.Value, 0, 100) },

            (CapabilityIds.LightSpeed, NumberValue number) =>
                was with { Speed = Math.Clamp(number.Value, 0, 100) },

            (CapabilityIds.LightColor, VectorValue zones) when zones.Items.Count >= 2 =>
                was with
                {
                    Upper = zones.Items[0].AsNumber(was.Upper),
                    Lower = zones.Items[1].AsNumber(was.Lower),
                },

            _ => was,
        };

        if (now == was) return Task.FromResult(false);

        lighting.Apply(now);

        // Written down on every change, because there is nowhere else it could be read
        // back from: the microphone forgets a colour a second after it is shown.
        _memory?.Remember(_deviceKey, LightingSlot, now.ToText());

        return Task.FromResult(true);
    }

    /// <summary>The lighting rows, read back out of the state the loop is painting from.</summary>
    private static CapabilityValue? Light(string capabilityId, LightingState state) => capabilityId switch
    {
        CapabilityIds.LightOn => CapabilityValue.Of(state.On),
        CapabilityIds.LightEffect => CapabilityValue.Choice((int)state.Effect),
        CapabilityIds.LightBrightness => CapabilityValue.Of(state.Brightness),
        CapabilityIds.LightSpeed => CapabilityValue.Of(state.Speed),
        CapabilityIds.LightColor => CapabilityValue.Numbers(state.Upper, state.Lower),
        _ => null,
    };

    private void Listen()
    {
        while (!_stop.IsCancellationRequested)
        {
            byte[]? report;

            try
            {
                report = _listener.Listen(Tick);
            }
            catch (IOException)
            {
                // Unplugged, or taken by something else. The contributor attaches again
                // on the next sweep; nothing here is worth a crash on a background thread.
                return;
            }

            if (report is not { Length: >= 3 }) continue;
            if (report[0] != StatusReport || report[1] != PolarPattern) continue;

            Volatile.Write(ref _pattern, report[2]);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stop.Cancel();

        // Long enough for a read already in flight to time out and see the token. The ear
        // holds an open HID handle, and leaving it running would hold the device after the
        // session that owns it has gone.
        _ = _ear.Wait(TimeSpan.FromSeconds(2));

        // Before the pipe it writes through, and before the handle underneath that.
        _lighting?.Dispose();

        _listener.Dispose();
        _pipe?.Dispose();
        _stop.Dispose();
    }
}
