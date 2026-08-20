using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Protocol;

/// <summary>
/// An open RACE channel to the Maxwell. Serialises every exchange: the device does
/// not tolerate overlapping requests, and a read issued while another is in flight
/// returns the other one's answer.
/// </summary>
public sealed class RaceSession : IDisposable
{
    private const int PollIntervalMs = 10;
    private const int RetryDelayMs = 50;
    private const int WriteSettleMs = 250;
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
    /// stale one is only returned once every attempt is spent.
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
                            stale ??= packet;
                        }
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
    public async Task<int> ReadValueAsync(ushort command, byte sub, CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, (byte)command, (byte)(command >> 8), sub];

        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == command && p.Sub == sub && p.Payload.Length >= 4,
            DefaultAttempts, DefaultWindowMs, cancellationToken).ConfigureAwait(false);

        return packet?.Value ?? -1;
    }

    /// <summary>
    /// Read from the 0x2C83 namespace, which lays its payload out as
    /// 00 &lt;sub&gt; 00 &lt;value&gt; — sub-command second, unlike the 0x09xx family.
    /// </summary>
    public async Task<int> ReadNamespace2CAsync(byte sub, CancellationToken cancellationToken = default)
    {
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x83, 0x2C, sub];

        RacePacket? packet = await RequestAsync(
            RaceCodec.BuildShortReport(body, Descriptor.OutputReportLength),
            p => p.Command == RaceCommands.ChatMixRead && p.Payload.Length >= 4
                 && (p.Sub2C == sub || p.Sub == sub),
            DefaultAttempts, DefaultWindowMs, cancellationToken).ConfigureAwait(false);

        return packet?.Value ?? -1;
    }

    /// <summary>Current ChatMix position, or -1.</summary>
    public Task<int> ReadChatMixAsync(CancellationToken cancellationToken = default) =>
        ReadNamespace2CAsync(MaxwellFeatures.ChatMix, cancellationToken);

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

            await Task.Delay(WriteSettleMs, cancellationToken).ConfigureAwait(false);
            if (await readBack().ConfigureAwait(false) == expected) return true;
            await Task.Delay(RetryDelayMs * 3, cancellationToken).ConfigureAwait(false);
        }

        return false;
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
