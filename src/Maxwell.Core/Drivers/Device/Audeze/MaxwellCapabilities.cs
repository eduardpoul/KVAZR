using Maxwell.Core.Devices;
using Maxwell.Core.Model;
using Maxwell.Race;
using Maxwell.Race.Protocol;

namespace Maxwell.Core.Drivers.Audeze;

/// <summary>
/// What an Audeze Maxwell declares to the shell.
///
/// Statuses come from docs/FEATURES.md and are honest: eight of these have been read
/// and written on live hardware, the rest carry byte sequences lifted out of Audeze HQ
/// that have never been confirmed. Marking the difference is the whole point — a
/// control that silently does nothing is worse than one that says it is untested.
/// </summary>
public static class MaxwellCapabilities
{
    /// <summary>
    /// Why a control is out of reach, and it is not the lead.
    ///
    /// This said "Available over the cable" for a long time and it was simply untrue:
    /// the requirement these rows carry is <see cref="RequirementKind.ControlChannel"/>,
    /// which asks that the headset be answering — and it answers over the dongle exactly
    /// as it does over USB-C. Measured on both: the same twenty-five sub-commands reply,
    /// and the only difference between the two sweeps is the byte that says which lead is
    /// carrying the sound. The equaliser, the gains and the balance were all driven over
    /// the dongle for a day while this sentence claimed they could not be.
    /// </summary>
    private const string SilentHint = "The headset is not answering — switch it on";

    public static IReadOnlyList<Capability> All { get; } = Build();

    private static Capability[] Build() =>
    [
        // ---------- Power ----------

        new()
        {
            Id = CapabilityIds.PowerBattery,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Charge",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },

        new()
        {
            Id = CapabilityIds.PowerCharging,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Charging",
            Subtitle = "Inferred from the charge rising — the protocol has no flag for it",
            Access = CapabilityAccess.Read,
            DeclaredStatus = CapabilityStatus.Untested,
        },

        new()
        {
            Id = CapabilityIds.PowerAutoOff,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Auto power-off",
            Subtitle = "How much silence before the headset switches itself off",

            // Real seconds, taken from the device rather than guessed. The headset stores
            // a sixteen-bit count — 1800 when this was recovered, which is the half hour
            // it had been sitting on — so these options are the values it is actually
            // given. An earlier version of this row offered a ladder of invented indices
            // and could not write at all; both faults had the same cause.
            Choices =
            [
                new(0, "Off"),
                new(1, "1 minute"),
                new(2, "5 minutes"),
                new(3, "10 minutes"),
                new(4, "15 minutes"),
                new(5, "20 minutes"),
                new(6, "30 minutes"),
                new(7, "45 minutes"),
                new(8, "60 minutes"),
            ],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.DeviceEdition,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Edition",
            Subtitle = "Read from the USB identifier",
            Access = CapabilityAccess.Read,
        },

        new()
        {
            Id = CapabilityIds.DeviceFirmware,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Firmware",
            Access = CapabilityAccess.Read,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        // ---------- Audio ----------

        new()
        {
            Id = CapabilityIds.AudioVolume,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Master volume",

            // The scale stops at the safe ceiling rather than running to the hardware's
            // full range with a warning line drawn across it: a limit that cannot be
            // crossed beats one the reader is merely advised about, and the gain write
            // latches irreversibly.
            Subtitle = "The scale stops at 150 — above that the driver can be damaged",

            // The mirror register latches on the first gain write and only a factory
            // reset puts it back, so no gain write is truly undoable.
            IsReversible = false,
            Range = new CapabilityRange { Min = 0, Max = RaceCommands.MaxGain },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.AudioGainLeft,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Left earcup",
            IsReversible = false,
            Range = new CapabilityRange { Min = 0, Max = RaceCommands.MaxGain },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.AudioGainRight,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Right earcup",
            IsReversible = false,
            Range = new CapabilityRange { Min = 0, Max = RaceCommands.MaxGain },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.AudioBalance,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Earcup balance",
            Subtitle = "128 is dead centre; the travel is about ±6",
            Range = new CapabilityRange { Min = 122, Max = 134, Detents = [128] },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        // Back, and reading real bands off the headset. Three things had to be right at
        // once before it would, and each looked like the protocol being wrong: the slots
        // are numbered 0..3 rather than by preset index, a populated slot answers across
        // four input reports where an empty one fits in a single, and the coefficient
        // generator's width argument is a bandwidth rather than a Q. See
        // docs/FEATURES.md for the whole of it.
        new()
        {
            Id = CapabilityIds.AudioEqGraphic,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Curve,
            Title = "Equaliser",
            Subtitle = "Up to ±12 dB a band, applied at once",
            DeclaredStatus = CapabilityStatus.Untested,

            // Writable only where the coefficients can be produced. The maths lives in a
            // library that arrives with the vendor application rather than with us, so on
            // a machine without it this stays a curve you can look at — which is still
            // worth having, and better than a slider that moves and changes nothing.
            Access = AirohaPeqBridge.Available ? CapabilityAccess.ReadWrite : CapabilityAccess.Read,

            // Centred on zero, and the range the wearer's own presets are drawn on.
            Range = new CapabilityRange { Min = -12, Max = 12, Unit = "dB" },
            Members =
            [
                new(0, "32"),
                new(1, "64"),
                new(2, "125"),
                new(3, "250"),
                new(4, "500"),
                new(5, "1 k"),
                new(6, "2 k"),
                new(7, "4 k"),
                new(8, "8 k"),
                new(9, "16 k"),
            ],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        // Last in the audio group on purpose: the sliders above it are one shape read
        // down a column, and a dropdown parked among them broke that column in half.
        new()
        {
            Id = CapabilityIds.AudioEqPreset,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Equaliser preset",
            Subtitle = "Ten slots: six built in, four the wearer's",
            DeclaredStatus = CapabilityStatus.Untested,
            // The four user slots are left out.
            //
            // They exist on the headset and can be selected, but nothing can be put in
            // them yet: writing a curve applies it to the running filter and stores it
            // nowhere. So they are empty, they stay empty, and choosing one silently
            // turns the equaliser off while the picker goes on naming a preset. Offering
            // a slot that can only disappoint is worse than not offering it, and they come
            // back the moment saving does.
            Choices = [.. EqPresets.All.Where(p => !p.IsUserSlot)
                .Select(p => new CapabilityChoice(p.Index, p.Name))],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        // ---------- Microphone ----------

        // No microphone-mute row, and the reason is measured rather than assumed.
        //
        // The headset does not tell the host about it, by any route. Sub-command 0x22,
        // which Audeze HQ reads for this, answers 0xFF and never anything else — over the
        // dongle and over USB-C, with the boom out and with the boom seated, with the USB
        // audio device dead and with it live and passing sound. Sub-command 0x35, the mic
        // level, does not answer at all; a read-only sweep of 0x00..0xFF in both the
        // 0x0901 and the 0x2C83 namespaces finds neither of them moving for anything the
        // microphone does. Nor is it announced: listening on all three HID interfaces —
        // including the telephony page, where a headset mute belongs — caught fourteen
        // reports from the volume wheel and not one from the boom.
        //
        // The owner checked Audeze HQ itself against the same movement: nothing there
        // changes either. So the mute happens inside the headset, the USB stream simply
        // carries silence, and no software on this side can know. A row that says
        // "plug the boom microphone in" to somebody whose boom is plugged in is worse than no
        // row at all.
        //
        // Kept as a note rather than deleted from history: if the firmware or the headset
        // is ever taken apart further, this is the ground the next attempt starts from.

        new()
        {
            Id = CapabilityIds.MicNoiseReduction,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Noise reduction",

            // The headset changes this on its own — there is a control on it for exactly
            // that, and the owner used it while the page was open and watched the page go
            // on claiming the old setting for a minute. An open page reads the battery
            // and whatever is marked here, and nothing else; unmarked, this was read once
            // when the device appeared and never again.
            IsVolatile = true,

            // Processing applied to the outgoing voice, not noise cancelling for the
            // wearer. The Maxwell has no ANC at all, and wording that blurs the two
            // sends people looking for a feature the hardware does not have.
            Subtitle = "Voice processing inside the microphone",
            DeclaredStatus = CapabilityStatus.Untested,
            Choices =
            [
                new((int)NoiseReductionMode.Off, "Off"),
                new((int)NoiseReductionMode.Low, "Low"),
                new((int)NoiseReductionMode.High, "High"),
            ],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.MicSidetone,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Toggle,
            Title = "Sidetone",
            Subtitle = "Hearing yourself in the headphones",
            DeclaredStatus = CapabilityStatus.Untested,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.MicSidetoneLevel,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Range,
            Title = "Sidetone level",
            DeclaredStatus = CapabilityStatus.Untested,

            // The ceiling lives in its own register. Hard-coding 31 would misbehave the
            // moment a firmware ships a different limit.
            Range = new CapabilityRange
            {
                Min = 0,
                Max = 31,
                Source = RangeSource.Device,
                MaxFromCapability = CapabilityIds.MicSidetoneMax,
            },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.MicSidetoneMax,
            IsSupporting = true,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Sidetone ceiling",
            Access = CapabilityAccess.Read,
        },

        // ChatMix sits in Audio rather than in a section of its own. It is a balance
        // between two volumes and belongs beside them; alone in "Game and chat" it was a
        // heading, a card and a single row for one slider.
        new()
        {
            Id = CapabilityIds.MixChat,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "ChatMix",
            Subtitle = "A 0–20 scale; the value can move on the device itself",

            // There is a physical wheel on the earcup driving the same value, so the app
            // is not the only writer and must not treat its own copy as authoritative.
            IsVolatile = true,
            Range = new CapabilityRange
            {
                Min = 0,
                Max = RaceCommands.ChatMixMax,
                Detents = [0, RaceCommands.ChatMixBalanced, RaceCommands.ChatMixMax],
            },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },

        new()
        {
            Id = CapabilityIds.MaintFactoryReset,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Action,
            Title = "Factory reset",
            Subtitle = "Erases every setting, the corrected balance among them",
            Access = CapabilityAccess.Write,
            DeclaredStatus = CapabilityStatus.Untested,
            Risk = CapabilityRisk.Hidden,
            IsReversible = false,

            // Shown, and not pressable. The command is sub 0x95 of 0x1101 and has never
            // been run against this headset — deliberately, because the notes label
            // 0x1100-0x1101 "Battery/sync" and getting that wrong once would wipe the
            // owner's settings, the corrected channel balance among them. Until it has
            // been confirmed some other way, the row exists to describe the device rather
            // than to be used.
            IsWithheld = true,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = SilentHint,
        },
    ];
}
