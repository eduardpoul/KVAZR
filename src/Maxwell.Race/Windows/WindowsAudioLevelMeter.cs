using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// A microphone's input level, measured by listening to it.
///
/// The obvious way to do this is <c>IAudioMeterInformation</c>, which every endpoint
/// offers for free — and it was tried first. It reads a flat zero: the meter belongs to
/// the audio engine, the engine only runs on an endpoint while something is streaming
/// through it, and with nothing recording there is nothing to measure. Forty-six samples
/// of a working QuadCast, maximum zero, is what settled it.
///
/// So this opens a shared-mode capture stream of its own and reads the samples. That is
/// what the sound settings, OBS and every conferencing application do, and it has the same
/// visible consequence: while this object is alive Windows shows the microphone as in use
/// and lists the application among those that have listened. Which is why the lifetime is
/// the whole design — see <see cref="IAudioLevelMeter"/> — and why nothing in the
/// application creates one except the row that draws the level, while its page is open.
///
/// The stream is shared, never exclusive: a level meter must not be able to take a
/// microphone away from the call the person is in.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAudioLevelMeter : IAudioLevelMeter
{
    /// <summary>Buffer length, in 100-ns units. A tenth of a second at a read every sixtieth.</summary>
    private const long BufferDuration = 1_000_000;

    private const int Shared = 0;
    private const uint Silent = 0x2;
    private const int InprocServer = 1;

    private const ushort FormatFloat = 3;
    private const ushort FormatPcm = 1;
    private const ushort FormatExtensible = 0xFFFE;

    private static readonly Guid SubtypeFloat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly object _lock = new();

    private object? _device;
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;

    private Sample _sample;
    private double _last;
    private bool _broken;
    private long _packets;
    private long _silent;

    public WindowsAudioLevelMeter(object device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;

        Guid iid = typeof(IAudioClient).GUID;
        if (((IActivate)device).Activate(ref iid, InprocServer, IntPtr.Zero, out object activated) != 0)
        {
            throw new IOException("The capture client could not be activated.");
        }

        _client = (IAudioClient)activated;

        IntPtr format = IntPtr.Zero;

        try
        {
            if (_client.GetMixFormat(out format) != 0 || format == IntPtr.Zero)
            {
                throw new IOException("The endpoint did not report a format.");
            }

            _sample = Describe(format);

            if (_sample.Kind == SampleKind.Unknown)
            {
                throw new IOException($"The endpoint records in a format this cannot read ({_sample.Bits}-bit).");
            }

            // Shared, and with no stream flags: this is a listener, not a recorder. The
            // engine resamples for us and the call the person is in keeps the device.
            if (_client.Initialize(Shared, 0, BufferDuration, 0, format, IntPtr.Zero) != 0)
            {
                throw new IOException("The capture stream could not be initialised.");
            }
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
        }

        Guid capture = typeof(IAudioCaptureClient).GUID;
        if (_client.GetService(ref capture, out object service) != 0)
        {
            throw new IOException("The capture service could not be obtained.");
        }

        _capture = (IAudioCaptureClient)service;

        if (_client.Start() != 0) throw new IOException("The capture stream could not be started.");
    }

    /// <summary>
    /// Drains whatever has arrived since the last call and returns the loudest sample in
    /// it.
    ///
    /// With no new audio the previous answer stands rather than dropping to zero. Reads
    /// come faster than packets arrive by design — the meter is asked sixteen times a
    /// second and the device delivers every ten milliseconds or so, but not on our
    /// schedule — and a meter that alternated between the real level and zero would look
    /// like a fault in the microphone rather than in the timing.
    /// </summary>
    public double? Read()
    {
        lock (_lock)
        {
            if (_broken || _capture is not { } capture) return null;

            float loudest = 0;
            bool any = false;

            while (true)
            {
                if (capture.GetNextPacketSize(out uint waiting) != 0) return Fail();
                if (waiting == 0) break;

                if (capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) != 0)
                {
                    return Fail();
                }

                try
                {
                    if (frames > 0)
                    {
                        any = true;
                        _packets++;

                        // A silent packet carries whatever was in the buffer before it,
                        // and reading that as audio is how a meter twitches in a quiet
                        // room. Windows says so in a flag; believe it.
                        if ((flags & Silent) != 0)
                        {
                            _silent++;
                        }
                        else
                        {
                            float loudestHere = Peak(data, frames);

                            // A packet of pure zeroes without the flag is silence too, and
                            // it is the shape a microphone muted by its own button takes:
                            // the stream keeps running and carries nothing.
                            if (loudestHere == 0) _silent++;

                            loudest = Math.Max(loudest, loudestHere);
                        }
                    }
                }
                finally
                {
                    _ = capture.ReleaseBuffer(frames);
                }
            }

            if (!any) return _last;

            _last = Math.Clamp(loudest, 0f, 1f);
            return _last;
        }
    }

    public (long Packets, long Silent) Tally
    {
        get
        {
            lock (_lock) return (_packets, _silent);
        }
    }

    private double? Fail()
    {
        // The endpoint went away, or somebody took it exclusively. Say nothing rather
        // than zero: a level of zero is a claim about the room.
        _broken = true;
        return null;
    }

    private unsafe float Peak(IntPtr data, uint frames)
    {
        long samples = (long)frames * _sample.Channels;
        float loudest = 0;

        switch (_sample.Kind)
        {
            case SampleKind.Float32:
            {
                var reading = (float*)data;
                for (long i = 0; i < samples; i++) loudest = Math.Max(loudest, Math.Abs(reading[i]));
                break;
            }

            case SampleKind.Pcm16:
            {
                var reading = (short*)data;
                for (long i = 0; i < samples; i++) loudest = Math.Max(loudest, Math.Abs(reading[i]) / 32768f);
                break;
            }

            case SampleKind.Pcm32:
            {
                var reading = (int*)data;
                for (long i = 0; i < samples; i++) loudest = Math.Max(loudest, Math.Abs(reading[i] / 2147483648f));
                break;
            }

            default:
                return 0;
        }

        return loudest;
    }

    /// <summary>
    /// What the engine hands us. In shared mode this is almost always 32-bit float,
    /// because that is what the mixer works in — but "almost always" is not a thing to
    /// read a memory buffer on, so the format is asked and the unreadable case is a
    /// refusal rather than a guess at somebody's samples.
    /// </summary>
    private static Sample Describe(IntPtr format)
    {
        ushort tag = (ushort)Marshal.ReadInt16(format, 0);
        ushort channels = (ushort)Marshal.ReadInt16(format, 2);
        ushort bits = (ushort)Marshal.ReadInt16(format, 14);

        if (tag == FormatExtensible)
        {
            var subtype = Marshal.PtrToStructure<Guid>(format + 24);
            tag = subtype == SubtypeFloat ? FormatFloat : subtype == SubtypePcm ? FormatPcm : (ushort)0;
        }

        SampleKind kind = (tag, bits) switch
        {
            (FormatFloat, 32) => SampleKind.Float32,
            (FormatPcm, 16) => SampleKind.Pcm16,
            (FormatPcm, 32) => SampleKind.Pcm32,
            _ => SampleKind.Unknown,
        };

        uint rate = (uint)Marshal.ReadInt32(format, 4);

        return new Sample(kind, Math.Max((ushort)1, channels), bits, rate);
    }

    public string Format => _sample.Kind switch
    {
        SampleKind.Unknown => "unknown",
        _ => $"{_sample.Rate / 1000.0:0.#} kHz · {_sample.Bits}-bit " +
             $"{(_sample.Kind == SampleKind.Float32 ? "float" : "PCM")} · {_sample.Channels} ch",
    };

    public void Dispose()
    {
        lock (_lock)
        {
            if (_client is { } client)
            {
                _ = client.Stop();
            }

            Release(_capture);
            Release(_client);
            Release(_device);

            _capture = null;
            _client = null;
            _device = null;
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
    }

    private enum SampleKind
    {
        Unknown,
        Float32,
        Pcm16,
        Pcm32,
    }

    private readonly record struct Sample(SampleKind Kind, ushort Channels, ushort Bits, uint Rate);

    // ---------- interop ----------

    /// <summary>
    /// The one method of <c>IMMDevice</c> this needs. Declared here rather than taking
    /// the whole interface from its neighbour so that the meter can be handed a device
    /// without the two files sharing a private type.
    /// </summary>
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivate
    {
        int Activate(ref Guid iid, uint context, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    /// <summary>
    /// The whole vtable, as everywhere else in this folder: <c>GetService</c> is the
    /// twelfth entry and leaving out one of the eleven before it would call something
    /// else entirely.
    /// </summary>
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity,
            IntPtr format, IntPtr sessionGuid);

        int GetBufferSize(out uint frames);

        int GetStreamLatency(out long latency);

        int GetCurrentPadding(out uint frames);

        int IsFormatSupported(int shareMode, IntPtr format, IntPtr closestMatch);

        int GetMixFormat(out IntPtr format);

        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        int Start();

        int Stop();

        int Reset();

        int SetEventHandle(IntPtr handle);

        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        int GetBuffer(out IntPtr data, out uint frames, out uint flags,
            out ulong devicePosition, out ulong counterPosition);

        int ReleaseBuffer(uint frames);

        int GetNextPacketSize(out uint frames);
    }
}
