using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>One cell of the matrix: a primitive in one of the eight states.</summary>
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
    private const string CableHint = "Настройка доступна по кабелю";

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

        Rows =
        [
            Build("Индикатор", "Readout · заряд, прошивка, мьют", Readout, CapabilityValue.Of(62), session),
            Build("Переключатель", "Toggle · сайдтон, подсказки", Toggle, CapabilityValue.Of(true), session),
            Build("Ползунок", "Range · ChatMix, громкость", Range, CapabilityValue.Of(10), session),
            Build("Выбор", "Choice · шумоподавление, пресет", Choice, CapabilityValue.Choice(2), session),
            Build("Действие", "Action · сброс, сопряжение", Action, CapabilityValue.Absent, session),
        ];
    }

    public string Title => "Дизайн-система";

    public IReadOnlyList<ShowcaseRow> Rows { get; }

    /// <summary>
    /// Primitives without a control yet — KeyMap, ButtonMap, Curve, Lighting, ProfileSet,
    /// HostSlots, RangeGroup. Named rather than quietly omitted, so the page reports what
    /// the design system is still missing instead of implying it is complete.
    /// </summary>
    public static IReadOnlyList<string> Pending { get; } =
    [
        "RangeGroup", "Curve", "KeyMap", "ButtonMap", "Lighting", "ProfileSet", "HostSlots",
    ];

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

    // ---------- the five descriptors, drawn from real Maxwell capabilities ----------

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
        RequirementHint = CableHint,
    };

    private static Capability Range => new()
    {
        Id = CapabilityIds.MixChat,
        Domain = CapabilityDomain.Mix,
        Primitive = CapabilityPrimitive.Range,
        Title = "ChatMix",
        Range = new CapabilityRange { Min = 0, Max = 20, Detents = [0, 10, 20] },
        RequirementHint = CableHint,
    };

    private static Capability Choice => new()
    {
        Id = CapabilityIds.MicNoiseReduction,
        Domain = CapabilityDomain.Microphone,
        Primitive = CapabilityPrimitive.Choice,
        Title = "Шумоподавление",
        Choices = [new(0, "Выключено"), new(1, "Низкое"), new(2, "Высокое")],
        RequirementHint = CableHint,
    };

    private static Capability Action => new()
    {
        Id = CapabilityIds.MaintFactoryReset,
        Domain = CapabilityDomain.Maintenance,
        Primitive = CapabilityPrimitive.Action,
        Title = "Сброс",
        Access = CapabilityAccess.Write,
        RequirementHint = CableHint,
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
