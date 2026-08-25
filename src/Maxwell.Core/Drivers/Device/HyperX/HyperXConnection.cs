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
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _ear;

    private int _pattern = -1;
    private bool _disposed;

    public HyperXConnection(IHidListener listener, IHidVendorPipe? pipe = null)
    {
        ArgumentNullException.ThrowIfNull(listener);

        _listener = listener;
        _pipe = pipe;
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

    public IReadOnlyList<Capability> Capabilities { get; } =
    [
        new Capability
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
            Choices =
            [
                new CapabilityChoice(0, "Stereo"),
                new CapabilityChoice(1, "Cardioid"),
                new CapabilityChoice(2, "Omnidirectional"),
                new CapabilityChoice(3, "Bidirectional"),
            ],

            // It changes under somebody's hand and never under ours, which is the whole
            // definition of volatile.
            IsVolatile = true,
        },
    ];

    /// <summary>None. The device is owned by whoever found it; this only adds a row.</summary>
    public IReadOnlyList<TransportInfo> Transports => [];

    public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(capabilityId, CapabilityIds.MicPolarPattern, StringComparison.Ordinal))
        {
            return Task.FromResult(CapabilityValue.Absent);
        }

        int pattern = Volatile.Read(ref _pattern);

        return Task.FromResult(pattern < 0 ? CapabilityValue.Absent : CapabilityValue.Of(pattern));
    }

    /// <summary>
    /// Nothing here is writable. The selector is a mechanical dial: there is no command
    /// that could move it, and a control that pretended otherwise would be a lie about the
    /// hardware rather than a missing feature.
    /// </summary>
    public Task<bool> WriteAsync(
        string capabilityId, CapabilityValue value, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

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

        _listener.Dispose();
        _pipe?.Dispose();
        _stop.Dispose();
    }
}
