using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Maxwell.Race.Native;

namespace Maxwell.Race;

/// <summary>
/// One RACE packet inside a HID input report; a report can carry several.
/// 0x5B is the reply to a command and usually carries the value itself. 0x5D is an
/// event the command triggers — a battery read answers with a short 0x5B followed by
/// a 0x5D holding the level, while a 0x0901 read answers with a single 0x5B.
/// So neither kind can be assumed to be the payload: match on the command instead.
/// </summary>
public sealed record RacePacket(byte Kind, ushort Command, byte[] Payload, int Offset)
{
    public const byte Reply = 0x5B;
    public const byte Event = 0x5D;

    public bool IsReply => Kind == Reply;
    public bool IsEvent => Kind == Event;

    /// <summary>Sub-command for the 0x09xx family, which leads its payload with it.</summary>
    public byte Sub => Payload.Length > 0 ? Payload[0] : (byte)0;

    /// <summary>Reads put the value last: battery percent, gain, or source state.</summary>
    public byte Value => Payload.Length > 0 ? Payload[^1] : (byte)0;

    public string Hex => Convert.ToHexString(Payload);
}

/// <summary>
/// An open RACE channel to the Maxwell over USB HID.
/// Output report 0x06, input report 0x07 polled with HidD_GetInputReport
/// (the device does not push interrupt reports for RACE replies).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RaceSession : IDisposable
{
    private const byte OutputReportId = 0x06;
    private const byte InputReportId = 0x07;

    private IntPtr _handle;
    private readonly object _gate = new();

    public MaxwellInterface Interface { get; }

    /// <summary>Raised for every raw input report read off the wire. Diagnostics only.</summary>
    public event Action<byte[]>? ReportReceived;

    private RaceSession(IntPtr handle, MaxwellInterface iface)
    {
        _handle = handle;
        Interface = iface;
    }

    public static RaceSession Open(MaxwellInterface iface)
    {
        IntPtr handle = HidNative.CreateFile(iface.Path,
            HidNative.GenericRead | HidNative.GenericWrite, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle)
            throw new IOException(
                $"Cannot open {iface.Path} for read/write (Win32 {Marshal.GetLastWin32Error()}). " +
                "Close Audeze HQ and try again.");

        return new RaceSession(handle, iface);
    }

    // ---------- framing ----------

    /// <summary>1-byte length framing, used by the 0x09xx family: 06 LEN 80 05 5A ...</summary>
    private byte[] BuildShort(byte[] body)
    {
        byte[] report = new byte[Interface.OutputReportLength];
        report[0] = OutputReportId;
        report[1] = (byte)body.Length;
        Array.Copy(body, 0, report, 2, Math.Min(body.Length, report.Length - 2));
        return report;
    }

    /// <summary>2-byte length framing, used by 0x1680 whose body already starts with 05 5A.</summary>
    private byte[] BuildLong(byte[] body)
    {
        byte[] report = new byte[Interface.OutputReportLength];
        report[0] = OutputReportId;
        report[1] = (byte)(body.Length & 0xFF);
        report[2] = (byte)(body.Length >> 8);
        Array.Copy(body, 0, report, 3, Math.Min(body.Length, report.Length - 3));
        return report;
    }

    private void Write(byte[] report) =>
        HidNative.WriteFile(_handle, report, report.Length, out _, IntPtr.Zero);

    // ---------- polled reads ----------

    /// <summary>Read one pending input report, or null when nothing is queued.</summary>
    private byte[]? PollOnce()
    {
        byte[] buffer = new byte[Interface.InputReportLength];
        buffer[0] = InputReportId;
        if (!HidNative.HidD_GetInputReport(_handle, buffer, buffer.Length)) return null;

        int length = buffer[1] | (buffer[2] << 8);
        if (length <= 0) return null;

        ReportReceived?.Invoke(buffer);
        return buffer;
    }

    /// <summary>
    /// Discard queued replies. A read issued right after a write otherwise picks up
    /// the preceding command's response and returns a stale value.
    /// </summary>
    public void Drain(int maxReports = 16)
    {
        for (int i = 0; i < maxReports; i++)
            if (PollOnce() is null) return;
    }

    /// <summary>Send a report, then poll until a packet the predicate accepts arrives.</summary>
    private RacePacket? Request(byte[] report, Func<RacePacket, bool> accept, int attempts, int windowMs)
    {
        lock (_gate)
        {
            // A matching packet found in a stale buffer is the previous answer to the
            // same question, which is wrong straight after a write. Prefer a packet
            // from a report the device marked fresh, and only fall back to a stale
            // one once every attempt is spent.
            RacePacket? stale = null;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Drain();
                Write(report);

                long deadline = Environment.TickCount64 + windowMs;
                while (Environment.TickCount64 < deadline)
                {
                    if (PollOnce() is { } buffer)
                    {
                        bool fresh = IsFresh(buffer);
                        foreach (RacePacket packet in ParsePackets(buffer))
                        {
                            if (!accept(packet)) continue;
                            if (fresh) return packet;
                            stale ??= packet;
                        }
                    }

                    Thread.Sleep(10);
                }

                Thread.Sleep(50);
            }

            return stale;
        }
    }

    /// <summary>Byte 2 flag: the buffer holds no new data, only the previous reply.</summary>
    private const byte StaleFlag = 0x80;

    /// <summary>True when the report carries data the device has not returned before.</summary>
    public static bool IsFresh(byte[] report) => report[1] > 0 && (report[2] & StaleFlag) == 0;

    /// <summary>
    /// Split a raw input report into RACE packets. Layout per packet:
    /// 05 &lt;kind&gt; &lt;len:u16&gt; &lt;cmd:u16&gt; &lt;payload&gt;, where len counts the command too.
    /// Byte 1 of the report is the fresh-data length and byte 2 is flags — not a
    /// 16-bit length — so the whole buffer is scanned whenever byte 1 is unusable.
    /// </summary>
    public static List<RacePacket> ParsePackets(byte[] report)
    {
        var packets = new List<RacePacket>();
        int declared = report[1];
        int end = declared > 0 && 3 + declared <= report.Length ? 3 + declared : report.Length;

        for (int i = 3; i + 6 <= end; i++)
        {
            if (report[i] != 0x05) continue;

            byte kind = report[i + 1];
            if (kind != RacePacket.Reply && kind != RacePacket.Event) continue;

            int length = report[i + 2] | (report[i + 3] << 8);
            if (length < 2 || i + 4 + length > end) continue;

            ushort command = (ushort)(report[i + 4] | (report[i + 5] << 8));
            byte[] payload = report[(i + 6)..(i + 4 + length)];

            packets.Add(new RacePacket(kind, command, payload, i));
            i += 3 + length;
        }

        return packets;
    }

    // ---------- commands (read-only) ----------

    public const ushort CmdBattery = 0x0CD6;
    public const ushort CmdRead = 0x0901;
    public const ushort CmdWrite = 0x0900;

    public const byte SubGainMaster = 0x28;
    public const byte SubGainLeft = 0x29;
    public const byte SubGainRight = 0x2A;
    public const byte SubAudioSource = 0x2F;

    /// <summary>
    /// Battery percentage, or -1. Request 06 07 80 05 5A 03 00 D6 0C; the answer
    /// arrives as a 0x5D packet whose last payload byte is the level.
    /// </summary>
    public int ReadBattery(int attempts = 4, int windowMs = 500)
    {
        byte[] body = { 0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C };
        // The 0x5B reply is a bare status byte; the level rides the 0x5D event,
        // so require a payload long enough to be more than an acknowledgement.
        RacePacket? packet = Request(BuildShort(body),
            p => p.Command == CmdBattery && p.Payload.Length >= 3, attempts, windowMs);
        return packet?.Value ?? -1;
    }

    /// <summary>ChatMix and sidetone live in their own namespace: 0x2C82 writes, 0x2C83 reads.</summary>
    public const ushort CmdChatMixWrite = 0x2C82;
    public const ushort CmdChatMixRead = 0x2C83;

    public const byte SubChatMix = 0x0B;

    /// <summary>0 = all game, 10 = balanced, 20 = all chat.</summary>
    public const byte ChatMixBalanced = 10;
    public const byte ChatMixMax = 20;

    /// <summary>Read one 0x0901 value: gain channel, audio source state, and so on.</summary>
    public int ReadValue(byte sub, int attempts = 4, int windowMs = 400) =>
        ReadValue(CmdRead, sub, attempts, windowMs);

    /// <summary>Read a value from any read-namespace command.</summary>
    public int ReadValue(ushort command, byte sub, int attempts = 4, int windowMs = 400)
    {
        byte[] body = { 0x80, 0x05, 0x5A, 0x04, 0x00, (byte)command, (byte)(command >> 8), sub };
        RacePacket? packet = Request(BuildShort(body),
            p => p.Command == command && p.Sub == sub && p.Payload.Length >= 4, attempts, windowMs);
        return packet?.Value ?? -1;
    }

    /// <summary>
    /// Current ChatMix position, or -1. The 0x2C namespace lays its payload out as
    /// 00 &lt;sub&gt; 00 &lt;value&gt;, with the sub-command second — unlike the 0x09xx family,
    /// which puts it first.
    /// </summary>
    public int ReadChatMix(int attempts = 4, int windowMs = 400) =>
        ReadNamespace2C(SubChatMix, attempts, windowMs);

    /// <summary>Read a value from the 0x2C83 namespace, accounting for its payload layout.</summary>
    public int ReadNamespace2C(byte sub, int attempts = 4, int windowMs = 400)
    {
        byte[] body = { 0x80, 0x05, 0x5A, 0x04, 0x00, 0x83, 0x2C, sub };
        RacePacket? packet = Request(BuildShort(body),
            p => p.Command == CmdChatMixRead && p.Payload.Length >= 4
                 && (p.Payload[1] == sub || p.Payload[0] == sub),
            attempts, windowMs);
        return packet?.Value ?? -1;
    }

    /// <summary>Set ChatMix. Verified by reading it back.</summary>
    public bool WriteChatMix(byte value, int attempts = 4)
    {
        if (value > ChatMixMax)
            throw new ArgumentOutOfRangeException(nameof(value), $"ChatMix ranges 0..{ChatMixMax}.");

        byte[] body = { 0x80, 0x05, 0x5A, 0x05, 0x00, 0x82, 0x2C, SubChatMix, 0x00, value };

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            lock (_gate)
            {
                Drain();
                Write(BuildShort(body));
            }

            Thread.Sleep(250);
            if (ReadChatMix() == value) return true;
            Thread.Sleep(150);
        }

        return false;
    }

    /// <summary>Highest gain the driver tolerates. Above this the coil can be damaged.</summary>
    public const int MaxGain = 150;

    /// <summary>
    /// Firmware-reserved gain codes: 0x88 is the boot default and 0x8E is silently
    /// rejected, so neither can be used as a test value or restored reliably.
    /// </summary>
    public static bool IsReservedGain(int value) => value is 0x88 or 0x8E;

    /// <summary>
    /// Write one 0x0900 value. Applies live and persists to NVDM, so callers are
    /// responsible for restoring the previous value.
    /// </summary>
    public bool WriteValue(byte sub, byte value, int attempts = 4)
    {
        if (sub is SubGainMaster or SubGainLeft or SubGainRight)
        {
            if (value > MaxGain)
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Gain {value} exceeds the safe maximum of {MaxGain}.");
            if (IsReservedGain(value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"0x{value:X2} is a reserved firmware code and will not stick.");
        }

        byte[] body = { 0x80, 0x05, 0x5A, 0x05, 0x00, 0x00, 0x09, sub, 0x00, value };

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            lock (_gate)
            {
                Drain();
                Write(BuildShort(body));
            }

            Thread.Sleep(250);
            if (ReadValue(sub, attempts: 3) == value) return true;
            Thread.Sleep(150);
        }

        return false;
    }

    /// <summary>Send a raw body and return every packet that comes back.</summary>
    public List<RacePacket> SendRaw(byte[] body, int windowMs = 500) => Probe(body, windowMs);

    /// <summary>Every packet the device returns for a command. Diagnostics and protocol discovery.</summary>
    public List<RacePacket> Probe(byte[] body, int windowMs = 500)
    {
        var packets = new List<RacePacket>();
        lock (_gate)
        {
            Drain();
            Write(BuildShort(body));

            long deadline = Environment.TickCount64 + windowMs;
            while (Environment.TickCount64 < deadline)
            {
                if (PollOnce() is { } buffer) packets.AddRange(ParsePackets(buffer));
                Thread.Sleep(10);
            }
        }
        return packets;
    }

    /// <summary>Read four bytes of flash or SRAM via cmd 0x1680. Over a dongle link this hits the dongle.</summary>
    public byte[]? ReadWord(uint address, int attempts = 5, int windowMs = 350)
    {
        byte[] body =
        {
            0x05, 0x5A, 0x08, 0x00, 0x80, 0x16, 0x00, 0x00,
            (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24),
        };

        byte[] wanted = { (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24) };

        lock (_gate)
        {
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Write(BuildLong(body));

                var collected = new List<byte>();
                long deadline = Environment.TickCount64 + windowMs;
                while (Environment.TickCount64 < deadline)
                {
                    if (PollOnce() is { } buffer)
                    {
                        int length = buffer[1] | (buffer[2] << 8);
                        int end = Math.Min(3 + length, buffer.Length);
                        for (int i = 3; i < end; i++) collected.Add(buffer[i]);
                    }
                    Thread.Sleep(8);
                }

                if (TryExtractWord(collected, wanted, out byte[]? word)) return word;
                Thread.Sleep(60);
            }
            return null;
        }
    }

    /// <summary>Scan a 0x1680 response stream for the 05 5B marker followed by the echoed address and data.</summary>
    private static bool TryExtractWord(List<byte> stream, byte[] address, out byte[]? word)
    {
        word = null;
        for (int i = 0; i + 8 <= stream.Count; i++)
        {
            if (stream[i] != address[0] || stream[i + 1] != address[1]
                || stream[i + 2] != address[2] || stream[i + 3] != address[3]) continue;

            bool marked = false;
            for (int j = Math.Max(0, i - 16); j < i; j++)
                if (stream[j] == 0x05 && j + 1 < i && stream[j + 1] == 0x5B) { marked = true; break; }
            if (!marked) continue;

            word = new[] { stream[i + 4], stream[i + 5], stream[i + 6], stream[i + 7] };
            return true;
        }
        return false;
    }

    /// <summary>Read a NUL-terminated version string out of flash at <paramref name="baseAddress"/>.</summary>
    public string? ReadVersionString(uint baseAddress, int words = 4)
    {
        var bytes = new List<byte>();
        for (uint offset = 0; offset < words * 4; offset += 4)
        {
            byte[]? word = ReadWord(baseAddress + offset);
            if (word is null) return null;
            bytes.AddRange(word);
        }

        var text = new StringBuilder();
        foreach (byte c in bytes)
        {
            if (c == 0) break;
            if (c < 0x20 || c > 0x7E) return null;
            text.Append((char)c);
        }

        Match match = Regex.Match(text.ToString(), @"\d+\.\d+\.\d+\.\d+");
        return match.Success ? match.Value : null;
    }

    public static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    public void Dispose()
    {
        if (_handle == HidNative.InvalidHandle || _handle == IntPtr.Zero) return;
        HidNative.CloseHandle(_handle);
        _handle = HidNative.InvalidHandle;
    }
}
