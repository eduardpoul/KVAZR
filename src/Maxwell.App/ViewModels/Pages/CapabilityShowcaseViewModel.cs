using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>One row of the matrix: a primitive in each of the eight states.</summary>
public sealed record ShowcaseRow(string Title, string Subtitle, IReadOnlyList<CapabilityViewModel> Cells);

/// <summary>
/// Artboard 2a, rendered from the real controls rather than drawn again.
///
/// The point is that this page cannot drift from the application: every cell is a live
/// view model going through the same factory and the same templates the device pages
/// use. If a state stops rendering correctly here, it is broken everywhere.
/// </summary>
public sealed class CapabilityShowcaseViewModel
{
    private const string SilentHint = "Гарнитура не отвечает — включите её";

    public static IReadOnlyList<string> StateNames { get; } =
    [
        "Обычное",
        "Недоступна сейчас",
        "Непроверенная",
        "Идёт запись",
        "Ошибка записи",
        "Изменена извне",
        "Необратимая",
        "Опасная",
    ];

    public CapabilityShowcaseViewModel()
    {
        var session = new PreviewDeviceSession();

        // Ordered as artboard 2a orders them: the scalar controls first, then the
        // composites, which are taller and would otherwise push the simple rows apart.
        Rows =
        [
            Build("Показание", "Readout · заряд, прошивка, мьют", Readout, CapabilityValue.Of(62), session),
            Build("Переключатель", "Toggle · сайдтон, подсказки", Toggle, CapabilityValue.Of(true), session),
            Build("Ползунок", "Range · ChatMix, громкость", Range, CapabilityValue.Of(10), session),
            Build("Выбор", "Choice · шумоподавление, пресет", Choice, CapabilityValue.Choice(2), session),
            Build("Действие", "Action · сброс, сопряжение", Action, CapabilityValue.Absent, session),

            Build("Связка ползунков", "RangeGroup · каналы, DPI по осям", Grouped, GroupedValue, session),
            Build("График", "Curve · полосы EQ, кривая отклика", Curve, CurveValue, session),
            Build("Схема кнопок", "ButtonMap · мышь", Buttons, ButtonsValue, session),
            Build("Раскладка", "KeyMap · слои клавиатуры", Keys, KeysValue, session),
            Build("Подсветка", "Lighting · зоны и цвет", Light, LightValue, session),
            Build("Профили", "ProfileSet · onboard-слоты", Profiles, CapabilityValue.Choice(0), session),
            Build("Хосты", "HostSlots · мультихост", Hosts, CapabilityValue.Choice(1), session),
        ];
    }

    public string Title => "Дизайн-система";

    public IReadOnlyList<ShowcaseRow> Rows { get; }

    private static ShowcaseRow Build(
        string title,
        string subtitle,
        Capability descriptor,
        CapabilityValue value,
        IDeviceSession session)
    {
        CapabilityState Base() => new()
        {
            Descriptor = descriptor,
            Value = value,
            Status = CapabilityStatus.Verified,
            Range = descriptor.Range,
        };

        List<CapabilityViewModel> cells =
        [
            Cell(Base(), session),
            Cell(Base() with
            {
                Descriptor = descriptor with
                {
                    Requirement = RequirementKind.BoomMicrophone,
                    RequirementHint = "Доступно при подключённом бум-микрофоне",
                },
                IsAvailable = false,
            }, session),
            Cell(Base() with { Status = CapabilityStatus.Untested }, session),
            Cell(Base() with { IsWriting = true }, session),
            Cell(Base() with { WriteError = "Устройство не приняло значение. Вернули прежнее" }, session),
            Cell(Base() with { ChangedExternally = true }, session),
            Cell(Base() with { Descriptor = descriptor with { IsReversible = false } }, session),
            Cell(Base() with { Descriptor = descriptor with { Risk = CapabilityRisk.Confirm } }, session),
        ];

        return new ShowcaseRow(title, subtitle, cells);
    }

    private static CapabilityViewModel Cell(CapabilityState state, IDeviceSession session) =>
        CapabilityViewModelFactory.Create(state, session, HintDetail.Full);

    // ---------- the five scalar descriptors, drawn from real Maxwell capabilities ----------

    private static Capability Readout => new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Заряд",
        Access = CapabilityAccess.Read,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private static Capability Toggle => new()
    {
        Id = CapabilityIds.MicSidetone,
        Domain = CapabilityDomain.Microphone,
        Primitive = CapabilityPrimitive.Toggle,
        Title = "Сайдтон",
        RequirementHint = SilentHint,
    };

    private static Capability Range => new()
    {
        Id = CapabilityIds.MixChat,
        Domain = CapabilityDomain.Mix,
        Primitive = CapabilityPrimitive.Range,
        Title = "ChatMix",
        Range = new CapabilityRange { Min = 0, Max = 20, Detents = [0, 10, 20] },
        RequirementHint = SilentHint,
    };

    private static Capability Choice => new()
    {
        Id = CapabilityIds.MicNoiseReduction,
        Domain = CapabilityDomain.Microphone,
        Primitive = CapabilityPrimitive.Choice,
        Title = "Шумоподавление",
        Choices = [new(0, "Выключено"), new(1, "Низкое"), new(2, "Высокое")],
        RequirementHint = SilentHint,
    };

    private static Capability Action => new()
    {
        Id = CapabilityIds.MaintFactoryReset,
        Domain = CapabilityDomain.Maintenance,
        Primitive = CapabilityPrimitive.Action,
        Title = "Сброс",
        Access = CapabilityAccess.Write,
        RequirementHint = SilentHint,
    };

    // ---------- the seven composites ----------
    //
    // These are taken from the stand-in mouse and keyboard rather than from Maxwell,
    // because Maxwell has no buttons to remap and no layers. The readings alongside them
    // are the same ones the mock devices report, so a cell here and the same capability
    // on a device page are showing identical data through identical code.

    private static Capability Grouped => new()
    {
        Id = CapabilityIds.PointerDpiXy,
        Domain = CapabilityDomain.Pointing,
        Primitive = CapabilityPrimitive.RangeGroup,
        Title = "Разрешение по осям",
        Range = new CapabilityRange { Min = 200, Max = 8000, Step = 50, Unit = "DPI" },
        Members = [new(0, "По X"), new(1, "По Y")],
        RequirementHint = SilentHint,
    };

    private static CapabilityValue GroupedValue => CapabilityValue.Numbers(1600, 1200);

    private static Capability Curve => new()
    {
        Id = CapabilityIds.AudioEqGraphic,
        Domain = CapabilityDomain.Audio,
        Primitive = CapabilityPrimitive.Curve,
        Title = "Полосы эквалайзера",
        Range = new CapabilityRange { Min = -12, Max = 12, Unit = "дБ" },
        Members =
        [
            new(0, "32 Гц"), new(1, "64 Гц"), new(2, "125 Гц"), new(3, "250 Гц"), new(4, "500 Гц"),
            new(5, "1 кГц"), new(6, "2 кГц"), new(7, "4 кГц"), new(8, "8 кГц"), new(9, "16 кГц"),
        ],
        RequirementHint = SilentHint,
    };

    private static CapabilityValue CurveValue => CapabilityValue.Numbers(5, 3, 0, -2, -3, -1, 2, 4, 3, 1);

    private static Capability Buttons => new()
    {
        Id = CapabilityIds.ButtonsMap,
        Domain = CapabilityDomain.Buttons,
        Primitive = CapabilityPrimitive.ButtonMap,
        Title = "Назначение кнопок",
        Members =
        [
            new(0, "Кнопка 4"),
            new(1, "Кнопка 5"),
            new(2, "Кнопка DPI") { Hint = "меняет значение извне" },
        ],
        Choices =
        [
            new(0, "Ничего"),
            new(1, "Назад"),
            new(2, "Вперёд"),
            new(3, "Ступень DPI"),
            new(4, "Смена профиля"),
        ],
        RequirementHint = SilentHint,
    };

    private static CapabilityValue ButtonsValue => CapabilityValue.Vector(
        [CapabilityValue.Choice(1), CapabilityValue.Choice(2), CapabilityValue.Choice(3)]);

    /// <summary>
    /// A quarter of the stand-in board: enough to show rows, layers and the difference
    /// between an unassigned key and an unread one, in a cell a twelfth of the width of
    /// the page.
    /// </summary>
    private static readonly string[] ShowcaseBase = ["Esc", "Q", "W", "E", "R", "T", "A", "S", "D", "F", "G", "H"];

    private static readonly string[] ShowcaseFn = ["~", "F1", "F2", "F3", "F4", "F5", "", "", "", "", "", ""];

    private static Capability Keys => new()
    {
        Id = CapabilityIds.KeysMap,
        Domain = CapabilityDomain.Keys,
        Primitive = CapabilityPrimitive.KeyMap,
        Title = "Раскладка",
        LayerCount = 3,
        GridColumns = 6,
        Members = [.. ShowcaseBase.Select((legend, i) => new CapabilityMember(i, legend))],
        Choices = [new(0, "—"), new(1, "Esc"), new(2, "Fn"), new(3, "Ctrl"), new(4, "⇧")],
        RequirementHint = SilentHint,
    };

    private static CapabilityValue KeysValue => CapabilityValue.Vector(
    [
        CapabilityValue.Vector([.. ShowcaseBase.Select(CapabilityValue.Of)]),
        CapabilityValue.Vector([.. ShowcaseFn.Select(CapabilityValue.Of)]),

        // Never asked for. The third tab draws faded caps rather than empty ones.
        CapabilityValue.Absent,
    ]);

    private static Capability Light => new()
    {
        Id = CapabilityIds.LightColor,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Lighting,
        Title = "Цвет зон",
        Members = [new(0, "Логотип"), new(1, "Колесо")],
        RequirementHint = SilentHint,
    };

    private static CapabilityValue LightValue => CapabilityValue.Numbers(0x2C6FD1, 0x7B4EC4);

    private static Capability Profiles => new()
    {
        Id = CapabilityIds.ProfilesOnboard,
        Domain = CapabilityDomain.Profiles,
        Primitive = CapabilityPrimitive.ProfileSet,
        Title = "Профили устройства",
        Members =
        [
            new(0, "Onboard 1 · Игровой") { Hint = "3200 DPI · 1000 Гц" },
            new(1, "Onboard 2 · Работа") { Hint = "1600 DPI · 500 Гц" },
            new(2, "Onboard 3") { Hint = "Слот пуст", IsEmpty = true },
        ],
        RequirementHint = SilentHint,
    };

    private static Capability Hosts => new()
    {
        Id = CapabilityIds.LinkHosts,
        Domain = CapabilityDomain.Link,
        Primitive = CapabilityPrimitive.HostSlots,
        Title = "Хосты",
        Members =
        [
            new(0, "Хост 1 · донгл") { Hint = "Ноутбук" },
            new(1, "Хост 2 · Bluetooth") { Hint = "Этот компьютер" },
            new(2, "Хост 3 · свободен") { Hint = "Удерживайте кнопку 3 секунды", IsEmpty = true },
        ],
        RequirementHint = SilentHint,
    };
}

/// <summary>
/// A session that answers but never touches hardware, so the showcase can present live
/// controls without a device. Writes go nowhere on purpose: this page demonstrates
/// states, and a control here must not be able to change a real headset.
/// </summary>
internal sealed class PreviewDeviceSession : IDeviceSession
{
    public string DeviceKey => "preview";

    public DeviceSnapshot Snapshot { get; } = new()
    {
        Info = new DeviceInfo { Key = "preview", DriverId = "preview", Vendor = "—", Model = "—" },
        Availability = DeviceAvailability.Active,
    };

    public event EventHandler<DeviceSnapshot>? SnapshotChanged
    {
        add { }
        remove { }
    }

    public Task RefreshAsync(IReadOnlyList<string> capabilityIds, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RefreshAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> WriteAsync(
        string capabilityId,
        CapabilityValue value,
        CancellationToken cancellationToken = default) => Task.FromResult(true);
}
