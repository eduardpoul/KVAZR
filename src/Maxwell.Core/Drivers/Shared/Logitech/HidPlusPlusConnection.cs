using System.Runtime.Versioning;

using Maxwell.Core.Devices;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Core.Drivers.Logitech;

/// <summary>
/// One paired device, read through its receiver on demand.
///
/// The channel is opened per read rather than held: a receiver is shared hardware —
/// Logitech's own software may be running beside us — and the polls are half a minute
/// apart. Paying an open per read buys never contending for the handle in between.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HidPlusPlusConnection : IDeviceConnection
{
    private static readonly Capability BatteryCapability = new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charge",
        Subtitle = "Over HID++ through the receiver",
        Access = CapabilityAccess.Read,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private readonly DeviceInfo _device;
    private readonly string _path;
    private readonly int _input;
    private readonly int _output;
    private readonly byte _slot;

    /// <summary>The last percentage this connection believed, for the glitch re-read.</summary>
    private int _lastPercent = -1;

    private PointerFacts _facts;

    public HidPlusPlusConnection(
        DeviceInfo device, string path, int input, int output, byte slot, PointerFacts? facts = null)
    {
        _device = device;
        _path = path;
        _input = input;
        _output = output;
        _slot = slot;
        _facts = facts ?? new PointerFacts();

        Capabilities = Declare(_facts);
    }

    /// <summary>
    /// The rows this particular device offers, built from what it said about itself when
    /// it was connected.
    ///
    /// Nothing here is decided in advance and nothing is a literal. A resolution row
    /// exists only if the device described a resolution, and its ends and its notches are
    /// the ends and the steps that device gave — a Superlight reaches 44000 and the mouse
    /// beside it reaches something else. A number written here would be right once.
    /// </summary>
    private static List<Capability> Declare(PointerFacts facts)
    {
        var rows = new List<Capability> { BatteryCapability, ChargingCapability };

        if (facts is { Dpi: { } running, Presets.Count: > 1 })
        {
            // The mouse's own steps, and nothing between them.
            //
            // This was a slider across the whole range the sensor will accept — a hundred
            // to forty-four thousand on the one measured — and that was wrong twice over.
            // A drag writes at every position it passes through, so a hand crossing the
            // track put a dozen resolutions into the mouse on its way; and the values in
            // between are not steps anybody wants, they are wherever a pixel landed. What
            // came out of it was a mouse at seven thousand four hundred.
            //
            // Five named steps, chosen one at a time, written once each. The steps are the
            // device's own, so a mouse with three offers three.
            List<int> steps = [.. facts.Presets];
            if (!steps.Contains(running)) steps.Insert(0, running);

            rows.Add(new Capability
            {
                Id = CapabilityIds.PointerDpi,
                Domain = CapabilityDomain.Pointing,
                Primitive = CapabilityPrimitive.Choice,
                Title = "Resolution",

                Subtitle = "The steps this mouse stores; its own button cycles them too",
                Access = CapabilityAccess.ReadWrite,

                // The options are the ladder, and the ladder is a reading rather than a
                // fact settled at connection: edit a rung and this list has to move with
                // it. Declared here as a seed for the moment before the ladder has been
                // read, and replaced by the session from that reading afterwards.
                Choices = [.. steps.Order().Select(dpi => new CapabilityChoice(dpi, $"{dpi} DPI"))],
                ChoicesFromCapability = CapabilityIds.PointerDpiSteps,
                ChoiceUnit = "DPI",
            });
        }

        if (facts.Presets.Count > 1)
        {
            // The rungs, editable one by one.
            //
            // The scale is not the device's. Asked, this mouse says 100 to 44000, and a
            // track across that is a track where one pixel is a hundred and seventy DPI —
            // the reason the first attempt at a resolution slider had to be taken out. So
            // the window is a working one, chosen by the person who uses the thing: 200 to
            // 5000 in steps of 200. Both ends happen to be rungs of the device's own
            // ladder, and every common resolution — 400, 800, 1200, 1600, 2400, 3200 — is
            // a multiple of the step, so every position of the track is a value the mouse
            // will accept.
            //
            // Clamped to what the device actually allows, and widened to hold whatever it
            // already has stored: a rung outside the window is still a rung, and a track
            // that cannot show it would write a different number the first time it was
            // touched.
            const int Comfortable = 200;
            const int Enough = 5000;

            int floor = Math.Max(facts.MinDpi ?? Comfortable, Comfortable);
            int ceiling = Math.Min(facts.MaxDpi ?? Enough, Enough);

            floor = Math.Min(floor, facts.Presets.Min());
            ceiling = Math.Max(ceiling, facts.Presets.Max());

            rows.Add(new Capability
            {
                Id = CapabilityIds.PointerDpiSteps,
                Domain = CapabilityDomain.Pointing,
                Primitive = CapabilityPrimitive.RangeGroup,
                Title = "Resolution steps",
                Subtitle = "The ladder the mouse cycles with its own button, kept in the mouse",
                Access = CapabilityAccess.ReadWrite,

                // Read and kept, and drawn inside the resolution row rather than as five
                // tracks of its own. Five sliders sitting open on the page is five ways to
                // write to a mouse's flash by brushing past them, for a thing that is set
                // once and then left alone.
                IsSupporting = true,
                Range = new CapabilityRange
                {
                    Min = floor,
                    Max = ceiling,
                    Step = Comfortable,
                    Unit = "DPI",
                },
                Members =
                [
                    .. facts.Presets.Select((_, i) => new CapabilityMember(i, $"Step {i + 1}")),
                ],
            });
        }

        if (facts is { ReportRate: not null, ReportRates.Count: > 1 })
        {
            rows.Add(new Capability
            {
                Id = CapabilityIds.PointerReportRate,
                Domain = CapabilityDomain.Pointing,
                Primitive = CapabilityPrimitive.Choice,
                Title = "Report rate",
                Subtitle = "How often the mouse tells the computer where it is",
                Access = CapabilityAccess.ReadWrite,
                Choices = [.. facts.ReportRates.Select(hz => new CapabilityChoice(hz, $"{hz} Hz"))],
            });
        }

        if (facts.Buttons is not null)
        {
            rows.Add(new Capability
            {
                Id = CapabilityIds.ButtonsCount,
                Domain = CapabilityDomain.Pointing,
                Primitive = CapabilityPrimitive.Readout,
                Title = "Buttons",
                Subtitle = "Counted by the mouse; its HID descriptor says otherwise",
                Access = CapabilityAccess.Read,
            });
        }

        if (facts.Profiles is not null)
        {
            rows.Add(new Capability
            {
                Id = CapabilityIds.DeviceProfiles,
                Domain = CapabilityDomain.Maintenance,
                Primitive = CapabilityPrimitive.Readout,
                Title = "Profiles on board",
                Access = CapabilityAccess.Read,
            });
        }

        if (facts.Serial is not null)
        {
            rows.Add(new Capability
            {
                Id = CapabilityIds.DeviceSerial,
                Domain = CapabilityDomain.Maintenance,
                Primitive = CapabilityPrimitive.Readout,
                Title = "Serial",
                Subtitle = "The only thing that tells two of the same mouse apart",
                Access = CapabilityAccess.Read,
            });
        }

        return rows;
    }

    private static readonly Capability ChargingCapability = new()
    {
        Id = CapabilityIds.PowerCharging,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charging",
        Subtitle = "Over HID++ — a real flag, not a guess",
        Access = CapabilityAccess.Read,
    };

    public IReadOnlyList<Capability> Capabilities { get; }

    public IReadOnlyList<TransportInfo> Transports => _device.Transports;

    /// <summary>
    /// One conversation at a time per receiver.
    ///
    /// A session serialises its own device's reads, which is enough while a device owns
    /// its cable. A Lightspeed receiver does not work that way: a mouse and a keyboard
    /// paired to the same dongle are two devices sharing one HID path, and a
    /// request-and-reply protocol down a shared path cannot carry two questions at once —
    /// the replies interleave and are handed to whichever device asked last.
    ///
    /// Not theory. Once devices began polling side by side, a fully charged mouse
    /// reported 15% and raised a low-battery notification, having read an answer meant
    /// for something else. Devices on different receivers still run in parallel; only
    /// those sharing a path take turns.
    /// </summary>
    private static readonly Dictionary<string, SemaphoreSlim> Channels =
        new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim GateFor(string path)
    {
        lock (Channels)
        {
            if (Channels.TryGetValue(path, out SemaphoreSlim? gate)) return gate;

            gate = new SemaphoreSlim(1, 1);
            Channels[path] = gate;
            return gate;
        }
    }

    public async Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        // Everything the device said about itself at connection, answered from what it
        // said. These do not change while it is plugged in, and asking again on every
        // sweep would be a conversation with a receiver for an answer nobody's hand can
        // alter.
        switch (capabilityId)
        {
            case CapabilityIds.PointerDpi:
                return _facts.Dpi is { } dpi ? CapabilityValue.Choice(dpi) : CapabilityValue.Absent;

            case CapabilityIds.PointerDpiSteps:
                return _facts.Presets.Count > 0
                    ? CapabilityValue.Vector([.. _facts.Presets.Select(CapabilityValue.Of)])
                    : CapabilityValue.Absent;

            case CapabilityIds.PointerReportRate:
                return _facts.ReportRate is { } rate ? CapabilityValue.Choice(rate) : CapabilityValue.Absent;

            case CapabilityIds.ButtonsCount:
                return _facts.Buttons is { } buttons ? CapabilityValue.Of(buttons) : CapabilityValue.Absent;

            case CapabilityIds.DeviceProfiles:
                return _facts.Profiles is { } profiles ? CapabilityValue.Of(profiles) : CapabilityValue.Absent;

            case CapabilityIds.DeviceSerial:
                return _facts.Serial is { } serial ? CapabilityValue.Of(serial) : CapabilityValue.Absent;
        }

        if (capabilityId is not (CapabilityIds.PowerBattery or CapabilityIds.PowerCharging))
        {
            return CapabilityValue.Absent;
        }

        SemaphoreSlim gate = GateFor(_path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var channel = new WindowsHidChannel(_path, _input, _output);

            if (HidPlusPlusReader.Battery(channel, _slot) is not { } charge)
            {
                return CapabilityValue.Absent;
            }

            // A reading thirty points below the last one is asked again before it is
            // believed. The receiver path is shared and busy, and a single garbled
            // exchange produced a phantom 15% from a charged mouse; a genuine drop
            // simply answers the same twice.
            if (_lastPercent >= 0
                && _lastPercent - charge.Percent > 30
                && HidPlusPlusReader.Battery(channel, _slot) is { } second)
            {
                charge = second;
            }

            _lastPercent = charge.Percent;

            return capabilityId == CapabilityIds.PowerBattery
                ? CapabilityValue.Of(charge.Percent)
                : CapabilityValue.Of(charge.IsCharging);
        }
        catch (IOException)
        {
            // The receiver is momentarily someone else's, or gone. An absent reading is
            // the honest answer either way; presence is the discovery sweep's business.
            return CapabilityValue.Absent;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Nothing here writes, and nothing pretends to.</summary>
    /// <summary>
    /// Changes the resolution, in the one place a change survives being closed: the
    /// profile the mouse plays from.
    ///
    /// Everything else here is read-only, and this one is not offered lightly. A mouse
    /// running its own profile refuses a resolution sent to it directly — the refusal
    /// reads as the function not existing — so the setting has to be edited where the
    /// mouse keeps it, which means reading a sector, changing five bytes of it and
    /// putting the checksum back. That the checksum can be put back was established
    /// against one the mouse had written itself.
    /// </summary>
    public async Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (capabilityId is not (CapabilityIds.PointerDpi
            or CapabilityIds.PointerReportRate
            or CapabilityIds.PointerDpiSteps))
        {
            return false;
        }

        if (capabilityId == CapabilityIds.PointerDpiSteps) return await StepsAsync(value, cancellationToken).ConfigureAwait(false);

        if (value is not ChoiceValue wanted) return false;

        SemaphoreSlim gate = GateFor(_path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var channel = new WindowsHidChannel(_path, _input, _output);

            if (capabilityId == CapabilityIds.PointerReportRate)
            {
                if (!PointerWriter.SetReportRate(channel, _slot, wanted.Index)) return false;

                _facts = _facts with { ReportRate = wanted.Index };
                return true;
            }

            if (_facts.Dpi is not { } current) return false;

            // A mouse asleep answers nothing, and the exchange that wakes it is the one
            // that goes unanswered. The read below is what wakes it here.
            if (!PointerWriter.SetResolution(channel, _slot, current, wanted.Index)) return false;

            // Believed only after the device says so. The resolution feature answers from
            // what the mouse is actually running at rather than from the sector, so this
            // is the change taking effect rather than the write being accepted.
            if (Live(channel) is not { } now) return false;

            // And the ladder as the mouse now has it, read back rather than worked out
            // here. Two rungs traded places, and a copy of that arithmetic in this file
            // would be a second thing to keep true.
            IReadOnlyList<int> ladder = PointerReader.Read(channel, _slot).Presets;

            _facts = _facts with
            {
                Dpi = now,
                Presets = ladder.Count > 0 ? ladder : _facts.Presets,
            };

            return now == wanted.Index;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The whole ladder at once, written into the profile and then believed only as far
    /// as the mouse confirms it.
    ///
    /// One sector write for all five, because this is flash in somebody's mouse. And the
    /// live resolution is read back afterwards rather than assumed unchanged: if the rung
    /// that was edited is the one the mouse is standing on, the mouse moves with it.
    /// </summary>
    private async Task<bool> StepsAsync(CapabilityValue value, CancellationToken cancellationToken)
    {
        if (value is not VectorValue vector) return false;

        int[] wanted = [.. vector.Items.Select(v => v.AsNumber())];
        if (wanted.Length == 0) return false;

        SemaphoreSlim gate = GateFor(_path);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var channel = new WindowsHidChannel(_path, _input, _output);

            if (!PointerWriter.SetSteps(channel, _slot, wanted)) return false;

            // Read back rather than trusted. The steps feature answers from the profile,
            // so this is the mouse agreeing rather than the write being accepted.
            PointerFacts fresh = PointerReader.Read(channel, _slot);

            _facts = _facts with
            {
                Presets = fresh.Presets.Count > 0 ? fresh.Presets : _facts.Presets,
                Dpi = fresh.Dpi ?? _facts.Dpi,
            };

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>What the mouse is running at now, from its own resolution feature.</summary>
    private int? Live(WindowsHidChannel channel)
    {
        if (HidPlusPlusReader.FeatureIndex(channel, _slot, 0x2202) is not { } feature) return null;

        byte[]? reply = HidPlusPlusReader.Ask(channel, _slot, feature, 0x05, 0x00);

        return reply is { Length: > 6 } && !HidPlusPlus.IsError(reply) ? (reply[5] << 8) | reply[6] : null;
    }

    public void Dispose()
    {
    }
}
