using Kvazr.Race.Abstractions;

namespace Kvazr.Race.Protocol;

/// <summary>
/// An open RACE channel to the Maxwell. Serialises every exchange: the device does
/// not tolerate overlapping requests, and a read issued while another is in flight
/// returns the other one's answer.
/// </summary>
public sealed class RaceSession : IDisposable
{
    private const int PollIntervalMs = 10;
    private const int RetryDelayMs = 50;

    /// <summary>
    /// How long a stale-flagged but accepted answer waits for a fresh one to overtake it.
    /// Long enough that a reply genuinely in flight still wins, short enough that it is
    /// not worth measuring.
    /// </summary>
    private const int StaleGraceMs = 60;

    /// <summary>
    /// How long a coefficient transfer is given to be acknowledged. Longer than an
    /// ordinary reply window because the headset reloads its filter before answering, and
    /// a window that expires early reports a refusal that never happened.
    /// </summary>
    private const int EqAckWindowMs = 1500;

    /// <summary>
    /// Shortest gap between two coefficient transfers.
    ///
    /// Not politeness. The headset reloads its filter after a transfer and does not
    /// answer while it does; firing the next one into that window leaves it holding part
    /// of one coefficient set and part of another, which it will happily play. That is
    /// what a dragged slider produces if nothing throttles it, and it is what turned a
    /// harmless experiment into a headset that ground until it was power-cycled.
    /// </summary>
    private const int EqApplySpacingMs = 250;

    /// <summary>
    /// How long a fragmented reply is followed before giving up. Generous: the parts
    /// arrive as fast as they can be polled, but a slot read crosses four reports and
    /// abandoning it half-collected leaves nothing to show for any of them.
    /// </summary>
    private const int LongReplyWindowMs = 900;

    private long _lastEqApply;
    private const int CableSettleMs = 250;
    private const int DongleSettleMs = 600;
    private const int DefaultAttempts = 4;
    private const int DefaultWindowMs = 400;

    private readonly IHidTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public MaxwellInterface Descriptor => _transport.Descriptor;

    /// <summary>Raised for every raw input report read off the wire. Diagnostics only.</summary>
    public event Action<byte[]>? ReportReceived;

    public RaceSession(IHidTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    // ---------- exchange ----------

    private byte[]? PollOnce()
    {
        byte[]? report = _transport.ReadInputReport(RaceCodec.InputReportId);
        if (report is not null) ReportReceived?.Invoke(report);
        return report;
    }

    /// <summary>
    /// Discard replies the device still has queued. Without this a read straight after
    /// a write returns the write's acknowledgement instead of the new value.
    /// </summary>
    private void DrainCore(int maxReports = 16)
    {
        for (int i = 0; i < maxReports; i++)
        {
            byte[]? report = _transport.ReadInputReport(RaceCodec.InputReportId);
            if (report is null || !RaceCodec.IsFresh(report)) return;
        }
    }

    /// <summary>
    /// Send a report, then poll until a packet the predicate accepts arrives.
    /// A match found in a stale buffer is the previous answer to the same question,
    /// which is wrong straight after a write — so a fresh match wins immediately and a
    /// stale one only once the grace below runs out.
    /// </summary>
    private async Task<RacePacket?> RequestAsync(
        byte[] report,
        Func<RacePacket, bool> accept,
        int attempts,
        int windowMs,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RacePacket? stale = null;
            long staleAt = 0;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                DrainCore();
                _transport.Write(report);

                long deadline = Environment.TickCount64 + windowMs;
                while (Environment.TickCount64 < deadline)
                {
                    if (PollOnce() is { } buffer)
                    {
                        bool fresh = RaceCodec.IsFresh(buffer);
                        foreach (RacePacket packet in RaceCodec.ParsePackets(buffer))
                        {
                            if (!accept(packet)) continue;
                            if (fresh) return packet;

                            if (stale is null)
                            {
                                stale = packet;
                                staleAt = Environment.TickCount64;
                            }
                        }
                    }

                    // An accepted answer is already in hand, but out of a replay: the head
                    // of a buffer whose freshness was spent before we polled it. Waiting
                    // the patience out spent four attempts — about 1.8 seconds — per
                    // capability and then returned this very packet. Measured: thirty
                    // seconds to read one headset, back when the dongle's stale flag made
                    // every reply over it look like a replay.
                    //
                    // The grace keeps the original intent — a fresh answer still wins if
                    // one is on its way — without paying full patience for an answer
                    // already received.
                    if (stale is not null && Environment.TickCount64 - staleAt >= StaleGraceMs)
                    {
                        return stale;
                    }

                    await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false);
            }

            return stale;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- reads ----------

    /// <summary>Battery percentage, or -1 when the device does not answer.</summary>
    public async Task<int> ReadBatteryAsync(CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x03, 0x00, 0xD6, 0x0C];

        // The 0x5B reply is a bare status byte; the level rides the 0x5D event, so
        // require a payload long enough to be more than an acknowledgement.
        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == RaceCommands.Battery && p.Payload.Length >= 3,
            DefaultAttempts, 500, cancellationToken).ConfigureAwait(false);

        return packet?.Value ?? -1;
    }

    /// <summary>Read one 0x0901 value: gain channel, noise reduction, sidetone level, and so on.</summary>
    public Task<int> ReadValueAsync(byte sub, CancellationToken cancellationToken = default) =>
        ReadValueAsync(RaceCommands.Read, sub, cancellationToken);

    /// <summary>Read a value from any read-namespace command that leads its payload with the sub-command.</summary>
    public Task<int> ReadValueAsync(ushort command, byte sub, CancellationToken cancellationToken = default) =>
        ReadValueAsync(command, sub, DefaultAttempts, DefaultWindowMs, cancellationToken);

    /// <summary>
    /// The same read with the patience spelled out, for sweeping a namespace.
    ///
    /// Four attempts at four hundred milliseconds is right for a value somebody is waiting
    /// on: the headset over its dongle drops a reply often enough that one try is not
    /// enough. It is the wrong trade for a sweep, where most sub-commands do not exist and
    /// each of them costs the full one and a half seconds of waiting for a reply that was
    /// never coming — two hundred and fifty of those is six minutes of nothing.
    /// </summary>
    public async Task<int> ReadValueAsync(
        ushort command,
        byte sub,
        int attempts,
        int windowMs,
        CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, (byte)command, (byte)(command >> 8), sub];

        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == command && p.Sub == sub && p.Payload.Length >= 4,
            attempts, windowMs, cancellationToken).ConfigureAwait(false);

        return packet?.Value ?? -1;
    }

    /// <summary>
    /// Read from the 0x2C83 namespace, which lays its payload out as
    /// 00 &lt;sub&gt; 00 &lt;value&gt; — sub-command second, unlike the 0x09xx family.
    /// </summary>
    public Task<int> ReadNamespace2CAsync(byte sub, CancellationToken cancellationToken = default) =>
        ReadNamespace2CAsync(sub, DefaultAttempts, DefaultWindowMs, cancellationToken);

    /// <inheritdoc cref="ReadNamespace2CAsync(byte, CancellationToken)"/>
    /// <remarks>With the patience spelled out, for sweeping the namespace.</remarks>
    public async Task<int> ReadNamespace2CAsync(
        byte sub,
        int attempts,
        int windowMs,
        CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x83, 0x2C, sub];

        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == RaceCommands.ChatMixRead && p.Payload.Length >= 4
                 && (p.Sub2C == sub || p.Sub == sub),
            attempts, windowMs, cancellationToken).ConfigureAwait(false);

        return packet?.Value ?? -1;
    }

    /// <summary>Current ChatMix position, or -1.</summary>
    public Task<int> ReadChatMixAsync(CancellationToken cancellationToken = default) =>
        ReadNamespace2CAsync(MaxwellFeatures.ChatMix, cancellationToken);

    /// <summary>
    /// The auto power-off timeout in seconds, or -1. Zero means the timer is off.
    ///
    /// Not a one-byte value like its neighbours in this namespace, which is why reading it
    /// as one produced a meaningless small number: the headset answers
    /// <c>00 &lt;sub&gt; 00 | &lt;on&gt; 00 &lt;lo&gt; &lt;hi&gt; | &lt;on&gt; 00 &lt;lo&gt; &lt;hi&gt;</c> —
    /// a flag and a sixteen-bit count of seconds, stated twice. Audeze HQ declares the
    /// same shape: <c>writeAutoPowerOff(unsigned short)</c>, reading the two halves back
    /// from consecutive report bytes.
    /// </summary>
    public async Task<int> ReadAutoPowerOffAsync(CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x83, 0x2C, MaxwellFeatures.AutoPowerOff];

        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == RaceCommands.ChatMixRead
                 && p.Payload.Length >= 7
                 && p.Sub2C == MaxwellFeatures.AutoPowerOff,
            DefaultAttempts, DefaultWindowMs, cancellationToken).ConfigureAwait(false);

        if (packet is null) return -1;

        byte[] payload = packet.Payload;

        // Off is off, whatever seconds are parked alongside it.
        if (payload[3] == 0) return 0;

        return payload[5] | (payload[6] << 8);
    }

    /// <summary>
    /// Sets the auto power-off timeout. Seconds, or zero to switch the timer off.
    ///
    /// The long form the feature table hints at without explaining: length <c>0x0C</c>,
    /// and the flag-and-value group repeated. The single-byte write every other
    /// sub-command in this namespace uses is silently refused here — the headset answers,
    /// the read-back comes up unchanged, and that is exactly what an application built on
    /// the short form sees.
    /// </summary>
    public async Task<bool> WriteAutoPowerOffAsync(int seconds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, ushort.MaxValue);

        byte on = (byte)(seconds > 0 ? 1 : 0);
        byte lo = (byte)(seconds & 0xFF);
        byte hi = (byte)((seconds >> 8) & 0xFF);

        byte[] body =
        [
            0x80, 0x05, 0x5A, 0x0C, 0x00, 0x82, 0x2C, MaxwellFeatures.AutoPowerOff, 0x00,
            on, 0x00, lo, hi,
            on, 0x00, lo, hi,
        ];

        for (int attempt = 0; attempt < DefaultAttempts; attempt++)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DrainCore();
                _transport.Write(RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength));
            }
            finally
            {
                _gate.Release();
            }

            await Task.Delay(SettleMs, cancellationToken).ConfigureAwait(false);

            if (await ReadAutoPowerOffAsync(cancellationToken).ConfigureAwait(false) == seconds) return true;

            await Task.Delay(RetryDelayMs * 3, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Reads one user equaliser slot back as its parametric bands, or null when the slot
    /// is empty or the headset does not answer.
    ///
    /// Nothing here is shaped like the register namespaces. The payload names a
    /// non-volatile key instead of a sub-command, and the answer is far longer than one
    /// report's worth of value: a five-byte header followed by a whole number of
    /// eighteen-byte band records. Audeze HQ caps the reply at a thousand bytes and so do
    /// we, because the cap is part of the request and the headset honours it literally.
    /// </summary>
    public async Task<MaxwellEqReply?> ReadEqBandsAsync(
        byte slot, CancellationToken cancellationToken = default)
    {
        if (!RaceCommands.IsUserEqSlot(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot),
                $"The equaliser numbers its four user slots 0..3; {slot} is not one of them.");
        }

        const int cap = 1000;

        byte[] body =
        [
            0x80, 0x05, 0x5A, 0x06, 0x00, 0x0C, 0x0A,
            slot, RaceCommands.EqNvKey, unchecked((byte)cap), (byte)(cap >> 8),
        ];

        byte[]? frame = await RequestLongAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            RaceCommands.EqBandsRead, cancellationToken).ConfigureAwait(false);

        // 05 5B <len:u16> 0C 0A <status> <slot> EF <size:u16> <blob>
        if (frame is null || frame.Length < 11) return null;

        byte status = frame[6];
        if (frame[7] != slot) return null;

        if (status != MaxwellEqReply.Ok) return new MaxwellEqReply(slot, status, null);

        int size = frame[9] | (frame[10] << 8);
        if (11 + size > frame.Length) return new MaxwellEqReply(slot, status, null);

        return new MaxwellEqReply(slot, status, MaxwellEqSlot.Parse(slot, frame.AsSpan(11, size)));
    }

    /// <summary>
    /// Issue a request whose answer is longer than one input report, and hand back the
    /// whole reassembled frame.
    ///
    /// The device fragments a long reply exactly as it accepts a long command: each report
    /// is <c>07 &lt;count&gt; &lt;flags&gt;</c> and then <c>count</c> bytes, and the parts
    /// are simply concatenated. Only the first carries the 05 5B magic and the length, so
    /// there is nothing in a continuation to recognise it by — the reply has to be
    /// followed from the moment it starts, and anything arriving in between belongs to it.
    ///
    /// This is why an equaliser slot read looked like an unanswered one for so long: a
    /// populated slot answers with 196 bytes, the single-report parser saw a packet whose
    /// declared length ran past the end of the report, discarded it, and reported silence.
    /// An empty slot answers in five bytes and parsed perfectly, so the failure looked
    /// exactly like a headset with nothing saved on it.
    /// </summary>
    private async Task<byte[]?> RequestLongAsync(
        byte[] report, ushort command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < DefaultAttempts; attempt++)
            {
                DrainCore();
                _transport.Write(report);

                byte[]? frame = await GatherAsync(command, cancellationToken).ConfigureAwait(false);
                if (frame is not null) return frame;

                await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]?> GatherAsync(ushort command, CancellationToken cancellationToken)
    {
        var frame = new List<byte>();
        int want = -1;
        long deadline = Environment.TickCount64 + LongReplyWindowMs;

        while (Environment.TickCount64 < deadline)
        {
            byte[]? buffer = PollOnce();

            // Byte 1 counts the bytes this report carries. Over the dongle byte 2 is 0x80
            // on every report including fresh ones, so it says nothing about staleness
            // here and the count is the only signal worth reading.
            if (buffer is not null && buffer.Length >= 3 && buffer[1] > 0)
            {
                int count = Math.Min(buffer[1], buffer.Length - 3);

                if (frame.Count == 0)
                {
                    if (count >= 6 && buffer[3] == RaceCodec.Marker && buffer[4] == RacePacket.Reply)
                    {
                        ushort answered = (ushort)(buffer[7] | (buffer[8] << 8));
                        if (answered == command)
                        {
                            want = 4 + (buffer[5] | (buffer[6] << 8));
                            frame.AddRange(buffer.AsSpan(3, count));
                        }
                    }
                }
                else
                {
                    frame.AddRange(buffer.AsSpan(3, count));
                }

                if (want > 0 && frame.Count >= want) return [.. frame.Take(want)];
            }

            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Loads biquad coefficients into the running filter.
    ///
    /// One block per sample rate, all of them together — see <see cref="RaceCommands"/>.
    /// This does not touch non-volatile memory: the coefficients live until the headset is
    /// power-cycled or its preset changes.
    /// </summary>
    public async Task<EqApplyResult> ApplyEqCoefficientsAsync(
        byte[] blob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blob);

        // 05 5A <len:u16> 03 0E 00 <blob>, where len counts the command and the byte
        // after it as well as the payload.
        int length = blob.Length + 3;

        byte[] body = new byte[7 + blob.Length];
        body[0] = 0x05;
        body[1] = 0x5A;
        body[2] = (byte)length;
        body[3] = (byte)(length >> 8);
        body[4] = 0x03;
        body[5] = 0x0E;
        body[6] = 0x00;
        blob.CopyTo(body.AsSpan(7));

        List<byte[]> reports = RaceCodec.BuildChunkedReports(
            0x80, body, Descriptor.OutputReportLength);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long since = Environment.TickCount64 - _lastEqApply;
            if (_lastEqApply != 0 && since < EqApplySpacingMs)
            {
                await Task.Delay((int)(EqApplySpacingMs - since), cancellationToken).ConfigureAwait(false);
            }

            _lastEqApply = Environment.TickCount64;

            DrainCore();
            for (int i = 0; i < reports.Count; i++)
            {
                // A refused chunk is the failure that matters most: the ones before it have
                // already landed, so the device is holding a truncated coefficient set.
                if (!_transport.Write(reports[i])) return EqApplyResult.ChunkRefused(i, reports.Count);
            }

            long deadline = Environment.TickCount64 + EqAckWindowMs;
            while (Environment.TickCount64 < deadline)
            {
                if (PollOnce() is { } buffer
                    && RaceCodec.ParsePackets(buffer).Any(p => p.Command == RaceCommands.EqCoefficientsApply))
                {
                    return EqApplyResult.Applied;
                }

                await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }

            return EqApplyResult.Silent;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The undocumented 0x0910 status struct, or null.</summary>
    public async Task<byte[]?> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x03, 0x00, 0x10, 0x09];

        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == RaceCommands.Status && p.Payload.Length >= 4,
            DefaultAttempts, 500, cancellationToken).ConfigureAwait(false);

        return packet?.Payload;
    }

    // ---------- writes ----------

    /// <summary>
    /// Write one 0x0900 value and verify it by reading back. Applies live and persists
    /// to NVDM, so the caller owns restoring the previous value.
    /// </summary>
    public async Task<bool> WriteValueAsync(byte sub, byte value, CancellationToken cancellationToken = default)
    {
        if (MaxwellFeatures.IsGain(sub))
        {
            if (value > RaceCommands.MaxGain)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Gain {value} exceeds the safe maximum of {RaceCommands.MaxGain}.");
            }

            if (RaceCommands.IsReservedGain(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"0x{value:X2} is a reserved firmware code and will not stick.");
            }
        }

        byte[] body = [0x80, 0x05, 0x5A, 0x05, 0x00, 0x00, 0x09, sub, 0x00, value];
        return await WriteVerifiedAsync(body, () => ReadValueAsync(sub, cancellationToken),
            value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Set ChatMix (0..20) and verify it by reading back.</summary>
    public Task<bool> WriteChatMixAsync(byte value, CancellationToken cancellationToken = default)
    {
        if (value > RaceCommands.ChatMixMax)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                $"ChatMix ranges 0..{RaceCommands.ChatMixMax}.");
        }

        return WriteNamespace2CAsync(MaxwellFeatures.ChatMix, value, cancellationToken);
    }

    /// <summary>Write into the 0x2C82 namespace — ChatMix, sidetone state — and verify it.</summary>
    public async Task<bool> WriteNamespace2CAsync(byte sub, byte value, CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x05, 0x00, 0x82, 0x2C, sub, 0x00, value];
        return await WriteVerifiedAsync(body, () => ReadNamespace2CAsync(sub, cancellationToken),
            value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// How long to let a write land before reading it back. Over the dongle the value
    /// has to cross the radio twice, and the cable's quarter second regularly expires
    /// first — the write then reports failure while the setting has in fact changed,
    /// which is worse than a plain failure because the UI and the device disagree.
    /// </summary>
    private int SettleMs => Descriptor.IsDongle ? DongleSettleMs : CableSettleMs;

    /// <summary>Write, settle, read back, repeat. The device drops writes silently, so verification is mandatory.</summary>
    private async Task<bool> WriteVerifiedAsync(
        byte[] body,
        Func<Task<int>> readBack,
        byte expected,
        CancellationToken cancellationToken)
    {
        byte[] report = RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength);

        for (int attempt = 0; attempt < DefaultAttempts; attempt++)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DrainCore();
                _transport.Write(report);
            }
            finally
            {
                _gate.Release();
            }

            await Task.Delay(SettleMs, cancellationToken).ConfigureAwait(false);
            if (await readBack().ConfigureAwait(false) == expected) return true;
            await Task.Delay(RetryDelayMs * 3, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    // ---------- destructive ----------

    /// <summary>
    /// Wipes the headset back to factory settings — presets, earcup balance and gain
    /// levels all go. There is no undo, and a custom firmware balance is lost with it.
    ///
    /// The <paramref name="confirmed"/> flag exists so this cannot be reached by a
    /// mistyped command id: it has to be passed deliberately at the call site.
    /// </summary>
    public async Task<bool> FactoryResetAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("A factory reset must be explicitly confirmed.");

        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x11, 0x95];
        return await SendFireAndForgetAsync(body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the current dongle pairing and re-enters pairing mode. The headset stops
    /// working over the air until it is paired again.
    /// </summary>
    public async Task<bool> StartPairingAsync(bool confirmed, CancellationToken cancellationToken = default)
    {
        if (!confirmed) throw new InvalidOperationException("Re-pairing must be explicitly confirmed.");

        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x01, 0x11, 0xAA];
        return await SendFireAndForgetAsync(body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a command that reboots or reconfigures the device, so no reply is expected
    /// and the absence of one is not a failure.
    /// </summary>
    private async Task<bool> SendFireAndForgetAsync(byte[] body, CancellationToken cancellationToken)
    {
        byte[] report = RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DrainCore();
            return _transport.Write(report);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- memory ----------

    /// <summary>Read four bytes of flash or SRAM via 0x1680. Over a dongle link this hits the dongle.</summary>
    public async Task<byte[]?> ReadWordAsync(uint address, CancellationToken cancellationToken = default)
    {
        byte[] report = RaceCodec.BuildLongReport(
            RaceCodec.BuildMemoryReadBody(address), Descriptor.OutputReportLength);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                _transport.Write(report);

                var collected = new List<byte>();
                long deadline = Environment.TickCount64 + 350;
                while (Environment.TickCount64 < deadline)
                {
                    if (PollOnce() is { } buffer)
                    {
                        // The 0x1680 reply is not framed like other packets, so the
                        // raw tail is accumulated and scanned for the echoed address.
                        int length = buffer[1] | (buffer[2] << 8);
                        int end = Math.Min(3 + length, buffer.Length);
                        for (int i = 3; i < end; i++) collected.Add(buffer[i]);
                    }

                    await Task.Delay(8, cancellationToken).ConfigureAwait(false);
                }

                if (RaceCodec.TryExtractWord(collected, address, out byte[]? word)) return word;
                await Task.Delay(60, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Read a NUL-terminated version string out of flash.</summary>
    public async Task<string?> ReadVersionStringAsync(
        uint baseAddress, int words = 4, CancellationToken cancellationToken = default)
    {
        var bytes = new List<byte>();

        for (uint offset = 0; offset < words * 4; offset += 4)
        {
            byte[]? word = await ReadWordAsync(baseAddress + offset, cancellationToken).ConfigureAwait(false);
            if (word is null) return null;
            bytes.AddRange(word);
        }

        return RaceCodec.ParseVersionString(bytes);
    }

    // ---------- diagnostics ----------

    /// <summary>
    /// Send a body under either framing and return only the input buffers that carry
    /// fresh data. Discovery use: the question being asked is "does anything at all come
    /// back", and a stale buffer is the device replaying its last answer, which is the
    /// same as silence.
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> ProbeBodyAsync(
        byte[] body, bool longFraming, int windowMs = 400,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        byte[] report = longFraming
            ? RaceCodec.BuildLongReport(body, Descriptor.OutputReportLength)
            : RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength);

        var fresh = new List<byte[]>();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DrainCore();
            _transport.Write(report);

            long deadline = Environment.TickCount64 + windowMs;
            while (Environment.TickCount64 < deadline)
            {
                if (PollOnce() is { } buffer && RaceCodec.IsFresh(buffer)) fresh.Add(buffer);
                await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        return fresh;
    }

    /// <summary>Send a raw body and collect every packet that comes back. Protocol discovery only.</summary>
    public async Task<IReadOnlyList<RacePacket>> ProbeAsync(
        byte[] body, int windowMs = 500, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var packets = new List<RacePacket>();
        byte[] report = RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DrainCore();
            _transport.Write(report);

            long deadline = Environment.TickCount64 + windowMs;
            while (Environment.TickCount64 < deadline)
            {
                if (PollOnce() is { } buffer) packets.AddRange(RaceCodec.ParsePackets(buffer));
                await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        return packets;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _transport.Dispose();
        _gate.Dispose();
    }
}
