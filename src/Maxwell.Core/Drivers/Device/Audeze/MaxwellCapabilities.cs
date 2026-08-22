using Maxwell.Core.Devices;
using Maxwell.Core.Model;
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
            Domain = CapabilityDomain.Power,
            Primitive = CapabilityPrimitive.Readout,

            // Declared as a Readout, not a Choice, on purpose. The device answers with a
            // small integer that is almost certainly an index into a preset list, and
            // that list has not been recovered yet. A dropdown built on a guessed mapping
            // would write the wrong timeout while looking authoritative.
            Title = "Таймер автовыключения",
            Subtitle = "Индекс пресета; набор значений ещё не расшифрован",
            Access = CapabilityAccess.Read,
            DeclaredStatus = CapabilityStatus.Untested,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.DeviceFirmware,
            Domain = CapabilityDomain.Power,
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
            Subtitle = "Выше 150 можно повредить излучатель",

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

        new()
        {
            Id = CapabilityIds.AudioEqPreset,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Пресет эквалайзера",
            Subtitle = "10 слотов: 6 встроенных, 4 пользовательских",
            DeclaredStatus = CapabilityStatus.Untested,
            Choices = [.. EqPresets.All.Select(p => new CapabilityChoice(p.Index, p.Name)
            {
                Hint = p.IsUserSlot ? "пользовательский" : null,
            })],
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.AudioEqGraphic,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Curve,
            Title = "Полосы эквалайзера",
            Subtitle = "Редактор появится позже",

            // The function exists in the firmware and in Audeze HQ; the command that
            // reaches it has not been found. Declared so the section can show it as
            // known-but-unreachable instead of pretending the hardware lacks it.
            DeclaredStatus = CapabilityStatus.Unavailable,
            Access = CapabilityAccess.Read,
        },

        new()
        {
            Id = CapabilityIds.AudioVoicePrompts,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Громкость голосовых подсказок",
            Subtitle = "Озвучка включения, выключения и смены пресета",
            DeclaredStatus = CapabilityStatus.Untested,
            Range = new CapabilityRange { Min = 0, Max = 255 },
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

        // ---------- Game and chat ----------

        new()
        {
            Id = CapabilityIds.MixChat,
            Domain = CapabilityDomain.Mix,
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

        // ---------- Maintenance ----------

        new()
        {
            Id = CapabilityIds.SensorsHeadTracking,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Toggle,
            Title = "Датчики head-tracking",
            Subtitle = "Под вопросом: для этой модели функция не описана Audeze",
            DeclaredStatus = CapabilityStatus.Untested,
            Access = CapabilityAccess.Write,
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = CableHint,
        },

        new()
        {
            Id = CapabilityIds.LinkPair,
            Domain = CapabilityDomain.Maintenance,
            Primitive = CapabilityPrimitive.Action,
            Title = "Сопряжение с донглом",
            Subtitle = "Разрывает текущую пару",
            Access = CapabilityAccess.Write,
            DeclaredStatus = CapabilityStatus.Untested,
            Risk = CapabilityRisk.Confirm,
            IsReversible = false,
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
