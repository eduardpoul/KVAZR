using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.HyperX;

/// <summary>
/// The QuadCast's two light bars, painted from here because they cannot be painted from
/// anywhere else.
///
/// The microphone has an effect of its own and runs it whenever nobody is talking to it.
/// A frame sent from the host overrides that for about a second and then the firmware
/// takes the lights back — measured, by sending one frame and watching. There is no
/// command that stores a colour: the maker's own software does not persist one either,
/// its effects reset the moment it closes.
///
/// So lighting here is not a setting but a mode of a running program, and the honest shape
/// for that is a loop. It costs two or three frames a second to hold a colour and about
/// twenty to animate one, at sixty-four bytes each — a hundredth of a percent of a full
/// speed bus, and a couple of system calls. The cost that matters is that the colour goes
/// out when this does.
///
/// Every effect is computed here rather than asked for, because the device has no notion
/// of them: what it accepts is one frame, two colours wide.
/// </summary>
internal sealed class HyperXLighting : IDisposable
{
    /// <summary>What the device is shown before every frame. Watched going past, byte for byte.</summary>
    private static readonly byte[] Header = [0x04, 0xF2, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01];

    /// <summary>Marks a colour inside a frame. One per zone, four bytes each.</summary>
    private const byte ColourCode = 0x81;

    /// <summary>
    /// How often a frame is composed. Fast enough for an animation to look continuous and
    /// slow enough to be nothing: twenty frames a second is 2.5 kB, against twelve
    /// megabits of bus.
    /// </summary>
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How long a colour that is not moving may go unrepainted.
    ///
    /// The firmware takes the lights back about a second after the last frame, so a steady
    /// colour still has to be repeated — but eight times a second would be seven wasted.
    /// This is the interval that keeps it lit and nothing more.
    /// </summary>
    private static readonly TimeSpan Refresh = TimeSpan.FromMilliseconds(400);

    private readonly IHidVendorPipe _pipe;
    private readonly object _gate = new();
    private readonly Timer _timer;

    private LightingState _state = LightingState.Default;
    private (int Upper, int Lower) _painted = (-1, -1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;
    private double _phase;
    private bool _disposed;

    public HyperXLighting(IHidVendorPipe pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        _pipe = pipe;
        _timer = new Timer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public LightingState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    /// <summary>
    /// Replaces the state and starts or stops the loop to match.
    ///
    /// Switching off stops the timer rather than painting black: the microphone is not a
    /// lamp somebody turned off, it is a device with lighting of its own that this
    /// application has stopped overriding. Painting it dark would be a different claim,
    /// and a worse one — the light would go out and stay out for as long as we ran.
    /// </summary>
    public void Apply(LightingState state)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _state = state;
            _painted = (-1, -1);

            if (state.On) _timer.Change(TimeSpan.Zero, Frame);
            else _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void Tick()
    {
        byte[]? colour = null;

        lock (_gate)
        {
            if (_disposed || !_state.On) return;

            _phase += Frame.TotalSeconds;

            (int upper, int lower) = Compose(_state, _phase);
            DateTimeOffset now = DateTimeOffset.Now;

            // A frame that says the same as the last one is worth sending only often
            // enough to keep the firmware from taking over.
            if ((upper, lower) != _painted || now - _last >= Refresh)
            {
                colour = Frame64(upper, lower);
                _painted = (upper, lower);
                _last = now;
            }
        }

        if (colour is null) return;

        // Outside the lock: a write is a system call and the interface thread may be
        // asking for the state at the same moment.
        _ = _pipe.Send(Header);
        _ = _pipe.Send(colour);
    }

    /// <summary>
    /// What the two bars should be at this instant. Pure, so every effect can be checked
    /// without a device, a timer or a clock.
    /// </summary>
    internal static (int Upper, int Lower) Compose(LightingState state, double seconds)
    {
        double speed = 0.4 + (state.Speed / 100.0 * 1.6);
        double turn = seconds * speed;

        return state.Effect switch
        {
            LightingEffect.Breathing => Both(Dim(state, Wave(turn))),

            // Square rather than smooth, and off means off: a blink that dipped to a dark
            // red would read as a fault in the microphone.
            LightingEffect.Blink => Both(Wave(turn) > 0.5 ? Dim(state, 1) : (0, 0, 0)),

            // Two bars is not much of a wave, so the second one is half a turn behind the
            // first. That is the whole of it, and it is what the hardware can show.
            LightingEffect.Wave => (
                Pack(Dim(state, Wave(turn), state.Upper)),
                Pack(Dim(state, Wave(turn + 0.5), state.Lower))),

            // The chosen colour is ignored here on purpose: a rainbow that started from
            // one hue would be a different effect with the same name.
            LightingEffect.Cycle => Both(Dim(state, 1, Hue(turn / 4))),

            _ => (Pack(Dim(state, 1, state.Upper)), Pack(Dim(state, 1, state.Lower))),
        };
    }

    /// <summary>A triangle rather than a sine: cheaper, and indistinguishable on two bars.</summary>
    private static double Wave(double turn)
    {
        double t = turn - Math.Floor(turn);
        return t < 0.5 ? t * 2 : 2 - (t * 2);
    }

    private static (int Upper, int Lower) Both((int R, int G, int B) colour)
    {
        int packed = Pack(colour);
        return (packed, packed);
    }

    private static (int R, int G, int B) Dim(LightingState state, double factor) =>
        Dim(state, factor, state.Upper);

    private static (int R, int G, int B) Dim(LightingState state, double factor, int packed)
    {
        double scale = Math.Clamp(factor, 0, 1) * Math.Clamp(state.Brightness / 100.0, 0, 1);

        return (
            (int)Math.Round(((packed >> 16) & 0xFF) * scale),
            (int)Math.Round(((packed >> 8) & 0xFF) * scale),
            (int)Math.Round((packed & 0xFF) * scale));
    }

    private static int Pack((int R, int G, int B) colour) =>
        (Math.Clamp(colour.R, 0, 255) << 16) | (Math.Clamp(colour.G, 0, 255) << 8) | Math.Clamp(colour.B, 0, 255);

    /// <summary>A hue at full saturation, as a packed colour. Zero is red and it wraps.</summary>
    private static int Hue(double turn)
    {
        double h = (turn - Math.Floor(turn)) * 6;
        int step = (int)h;
        double f = h - step;

        int rise = (int)Math.Round(f * 255);
        int fall = 255 - rise;

        return step switch
        {
            0 => Pack((255, rise, 0)),
            1 => Pack((fall, 255, 0)),
            2 => Pack((0, 255, rise)),
            3 => Pack((0, fall, 255)),
            4 => Pack((rise, 0, 255)),
            _ => Pack((255, 0, fall)),
        };
    }

    /// <summary>One frame: a colour per bar, marked and padded to what the pipe takes.</summary>
    internal static byte[] Frame64(int upper, int lower) =>
    [
        ColourCode, (byte)(upper >> 16), (byte)(upper >> 8), (byte)upper,
        ColourCode, (byte)(lower >> 16), (byte)(lower >> 8), (byte)lower,
    ];

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _timer.Dispose();
    }
}

/// <summary>What the lighting is doing. A record so a change is one assignment under a lock.</summary>
internal readonly record struct LightingState(
    bool On,
    LightingEffect Effect,
    int Upper,
    int Lower,
    int Brightness,
    int Speed)
{
    /// <summary>
    /// Off, and white underneath.
    ///
    /// Off because the microphone arrives with lighting of its own and an application that
    /// took it over on sight would be answering a question nobody asked. White because it
    /// is the one colour in the palette that says nothing about taste.
    /// </summary>
    public static readonly LightingState Default =
        new(false, LightingEffect.Static, 0xFFFFFF, 0xFFFFFF, 100, 50);
}

internal enum LightingEffect
{
    Static,
    Breathing,
    Blink,
    Wave,
    Cycle,
}
