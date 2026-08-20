using Maxwell.Core.Devices;

namespace Maxwell.Core.Drivers.Mock;

/// <summary>
/// Capability lists for a stand-in mouse and keyboard.
///
/// Neither device exists on this desk, and ten of the design's artboards are about
/// them. Without something to render, the shell would be built against a headset and
/// discovered to be headset-shaped the day real hardware arrives.
///
/// The shapes are taken from what the real protocols expose — Logitech HID++ for the
/// mouse, VIA for the keyboard, both catalogued in docs/CAPABILITIES.md — but every
/// value here is invented. Everything is declared
/// <see cref="CapabilityStatus.Untested"/> so the interface says so out loud.
/// </summary>
public static class MockCatalogs
{
    public static IReadOnlyList<Capability> Mouse { get; } =
    [
        Battery,
        Charging,

        new()
        {
            Id = CapabilityIds.LinkSignal,
            Domain = CapabilityDomain.Link,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Уровень сигнала",
            Access = CapabilityAccess.Read,
            DeclaredStatus = CapabilityStatus.Untested,
            Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
        },

        Hosts,

        new()
        {
            Id = CapabilityIds.PointerDpi,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.Range,
            Title = "Разрешение сенсора",
            Subtitle = "Ступени читаются из устройства",
            DeclaredStatus = CapabilityStatus.Untested,
            Range = new CapabilityRange { Min = 200, Max = 8000, Step = 50, Unit = "DPI" },
        },

        new()
        {
            Id = CapabilityIds.PointerReportRate,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Частота опроса",
            Subtitle = "Выше — отзывчивее, но батарея садится быстрее",
            DeclaredStatus = CapabilityStatus.Untested,
            Choices =
            [
                new(0, "125 Гц"),
                new(1, "250 Гц"),
                new(2, "500 Гц"),
                new(3, "1000 Гц"),
            ],
        },

        new()
        {
            Id = CapabilityIds.ButtonsMap,
            Domain = CapabilityDomain.Buttons,
            Primitive = CapabilityPrimitive.ButtonMap,
            Title = "Назначение кнопок",
            Subtitle = "Раскладка пишется в память мыши",
            DeclaredStatus = CapabilityStatus.Untested,
        },

        Brightness,
        LightEffect,
        OnboardProfiles,
    ];

    public static IReadOnlyList<Capability> Keyboard { get; } =
    [
        Battery,
        Charging,
        Hosts,

        new()
        {
            Id = CapabilityIds.KeysMap,
            Domain = CapabilityDomain.Keys,
            Primitive = CapabilityPrimitive.KeyMap,
            Title = "Раскладка",

            // The distinction people care about: VIA writes the layout into the keyboard,
            // so it keeps working with the application closed and on another computer.
            Subtitle = "Пишется в клавиатуру и работает без запущенного приложения",
            DeclaredStatus = CapabilityStatus.Untested,
        },

        new()
        {
            Id = CapabilityIds.KeysLayers,
            Domain = CapabilityDomain.Keys,
            Primitive = CapabilityPrimitive.Readout,
            Title = "Слоёв",
            Access = CapabilityAccess.Read,
            DeclaredStatus = CapabilityStatus.Untested,
        },

        new()
        {
            Id = CapabilityIds.LightOn,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Toggle,
            Title = "Подсветка",
            DeclaredStatus = CapabilityStatus.Untested,
        },

        Brightness,
        LightEffect,
        OnboardProfiles,
    ];

    // ---------- pieces shared by both ----------

    private static Capability Battery => new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Заряд",
        Access = CapabilityAccess.Read,
        DeclaredStatus = CapabilityStatus.Untested,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private static Capability Charging => new()
    {
        Id = CapabilityIds.PowerCharging,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Зарядка",
        Access = CapabilityAccess.Read,
        DeclaredStatus = CapabilityStatus.Untested,
    };

    private static Capability Hosts => new()
    {
        Id = CapabilityIds.LinkHosts,
        Domain = CapabilityDomain.Link,
        Primitive = CapabilityPrimitive.HostSlots,
        Title = "Хосты",
        Subtitle = "Три слота, переключение на устройстве и отсюда",
        DeclaredStatus = CapabilityStatus.Untested,

        // The user can press the switch key on the device itself, so the app is not the
        // only writer here.
        IsVolatile = true,
    };

    private static Capability Brightness => new()
    {
        Id = CapabilityIds.LightBrightness,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Range,
        Title = "Яркость подсветки",
        DeclaredStatus = CapabilityStatus.Untested,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private static Capability LightEffect => new()
    {
        Id = CapabilityIds.LightEffect,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Choice,
        Title = "Эффект",
        DeclaredStatus = CapabilityStatus.Untested,
        Choices =
        [
            new(0, "Выключен"),
            new(1, "Постоянный"),
            new(2, "Дыхание"),
            new(3, "Цикл"),
        ],
    };

    private static Capability OnboardProfiles => new()
    {
        Id = CapabilityIds.ProfilesOnboard,
        Domain = CapabilityDomain.Profiles,
        Primitive = CapabilityPrimitive.ProfileSet,
        Title = "Профили устройства",

        // Not the same thing as the application's profiles, and the interface has to
        // keep the two apart: these live in the device and travel with it.
        Subtitle = "Хранятся в памяти устройства",
        DeclaredStatus = CapabilityStatus.Untested,
    };
}
