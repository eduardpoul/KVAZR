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
    private const string BoomHint = "Работают настройки бума. Подключите бум-микрофон";
    private const string CableHint = "Настройка доступна по кабелю";

    public static IReadOnlyList<Capability> All { get; } = Build();

    private static Capability[] Build() =>
    [
        // ---------- Power ----------

        new()
        {
            Id = CapabilityIds.PowerBattery,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Заряд",
            Access = CapabilityAccess.Read,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },

        new()
        {
            Id = CapabilityIds.PowerCharging,
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Зарядка",
            Subtitle = "Выводится по росту заряда — прямого флага в протоколе не найдено",
            Access = CapabilityAccess.Read,
            DeclaredStatus = CapabilityStatus.Untested,
        },

        new()
        {
            Id = CapabilityIds.PowerAutoOff,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Таймер автовыключения",
            Subtitle = "Через сколько тишины наушники выключатся сами",

            // Real seconds, taken from the device rather than guessed. The headset stores
            // a sixteen-bit count — 1800 when this was recovered, which is the half hour
            // it had been sitting on — so these options are the values it is actually
            // given. An earlier version of this row offered a ladder of invented indices
            // and could not write at all; both faults had the same cause.
            Choices =
            [
                new(0, "Выключен"),
                new(1, "1 минута"),
                new(2, "5 минут"),
                new(3, "10 минут"),
                new(4, "15 минут"),
                new(5, "20 минут"),
                new(6, "30 минут"),
                new(7, "45 минут"),
                new(8, "60 минут"),
            ],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.DeviceEdition,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Версия",
            Subtitle = "Определяется по идентификатору USB",
            Access = CapabilityAccess.Read,
        },

        new()
        {
            Id = CapabilityIds.DeviceFirmware,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Прошивка",
            Access = CapabilityAccess.Read,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        // ---------- Audio ----------

        new()
        {
            Id = CapabilityIds.AudioVolume,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Общая громкость",

            // The scale stops at the safe ceiling rather than running to the hardware's
            // full range with a warning line drawn across it: a limit that cannot be
            // crossed beats one the reader is merely advised about, and the gain write
            // latches irreversibly.
            Subtitle = "Шкала кончается на 150 — выше можно повредить излучатель",

            // The mirror register latches on the first gain write and only a factory
            // reset puts it back, so no gain write is truly undoable.
            IsReversible = false,
            Range = new CapabilityRange { Min = 0, Max = RaceCommands.MaxGain },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.AudioGainLeft,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Левый канал",
            IsReversible = false,
            Range = new CapabilityRange { Min = 0, Max = RaceCommands.MaxGain },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.AudioGainRight,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Правый канал",
            IsReversible = false,
            Range = new CapabilityRange { Min = 0, Max = RaceCommands.MaxGain },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.AudioBalance,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Баланс чашек",
            Subtitle = "128 — ровно центр, ход около ±6",
            Range = new CapabilityRange { Min = 122, Max = 134, Detents = [128] },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
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
            Title = "Эквалайзер",
            Subtitle = "Полоса до ±12 дБ, применяется сразу",
            DeclaredStatus = CapabilityStatus.Untested,

            // Writable only where the coefficients can be produced. The maths lives in a
            // library that arrives with the vendor application rather than with us, so on
            // a machine without it this stays a curve you can look at — which is still
            // worth having, and better than a slider that moves and changes nothing.
            Access = AirohaPeqBridge.Available ? CapabilityAccess.ReadWrite : CapabilityAccess.Read,

            // Centred on zero, and the range the wearer's own presets are drawn on.
            Range = new CapabilityRange { Min = -12, Max = 12, Unit = "дБ" },
            Members =
            [
                new(0, "32"),
                new(1, "64"),
                new(2, "125"),
                new(3, "250"),
                new(4, "500"),
                new(5, "1 к"),
                new(6, "2 к"),
                new(7, "4 к"),
                new(8, "8 к"),
                new(9, "16 к"),
            ],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        // Last in the audio group on purpose: the sliders above it are one shape read
        // down a column, and a dropdown parked among them broke that column in half.
        new()
        {
            Id = CapabilityIds.AudioEqPreset,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Пресет эквалайзера",
            Subtitle = "10 слотов: 6 встроенных, 4 пользовательских",
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
            RequirementHint = CableHint,
        },

        // ---------- Microphone ----------

        new()
        {
            Id = CapabilityIds.MicMute,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Микрофон замьючен",

            // Read only, and not as a shortcut: Audeze HQ has readMicMute and no writer
            // at all. Mute moves only when the boom arm is raised. Drawing a switch here
            // would promise control that does not exist.
            Access = CapabilityAccess.Read,
            Subtitle = "Управляется подъёмом бум-микрофона",
            DeclaredStatus = CapabilityStatus.Untested,
            Requirement = RequirementKind.BoomMicrophone,
            RequirementHint = BoomHint,
        },

        new()
        {
            Id = CapabilityIds.MicLevel,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Range,
            Title = "Уровень микрофона",
            DeclaredStatus = CapabilityStatus.Untested,
            Range = new CapabilityRange { Min = 0, Max = 255 },
            Requirement = RequirementKind.BoomMicrophone,
            RequirementHint = BoomHint,
        },

        new()
        {
            Id = CapabilityIds.MicNoiseReduction,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Шумоподавление",

            // Processing applied to the outgoing voice, not noise cancelling for the
            // wearer. The Maxwell has no ANC at all, and wording that blurs the two
            // sends people looking for a feature the hardware does not have.
            Subtitle = "Обработка голоса в микрофоне",
            DeclaredStatus = CapabilityStatus.Untested,
            Choices =
            [
                new((int)NoiseReductionMode.Off, "Выключено"),
                new((int)NoiseReductionMode.Low, "Низкое"),
                new((int)NoiseReductionMode.High, "Высокое"),
            ],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.MicSidetone,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Toggle,
            Title = "Сайдтон",
            Subtitle = "Самослышимость в наушниках",
            DeclaredStatus = CapabilityStatus.Untested,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.MicSidetoneLevel,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Range,
            Title = "Уровень сайдтона",
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
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.MicSidetoneMax,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Потолок сайдтона",
            Access = CapabilityAccess.Read,
        },

        // ChatMix sits in Audio rather than in a section of its own. It is a balance
        // between two volumes and belongs beside them; alone in "Игра и чат" it was a
        // heading, a card and a single row for one slider.
        new()
        {
            Id = CapabilityIds.MixChat,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "ChatMix",
            Subtitle = "Шкала 0–20, значение может измениться на устройстве",

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
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.MaintFactoryReset,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Action,
            Title = "Сброс к заводским настройкам",
            Subtitle = "Стирает все настройки, включая исправленный баланс",
            Access = CapabilityAccess.Write,
            DeclaredStatus = CapabilityStatus.Untested,
            Risk = CapabilityRisk.Hidden,
            IsReversible = false,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },
    ];
}
