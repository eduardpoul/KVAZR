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
    // ---------- the stand-in keyboard's layout ----------
    //
    // Declared before the catalogs that use them. Static field initialisers run in
    // declaration order, so a layout defined below the keyboard would be null at the
    // moment the keyboard was built — silently, and only for the one device.

    /// <summary>The factory legends, row by row, twelve to a row.</summary>
    internal static readonly string[] BaseLayer =
    [
        "Esc", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "⌫",
        "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "⏎",
        "Caps", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'",
        "⇧", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "Fn",
    ];

    /// <summary>
    /// What holding Fn produces. Deliberately sparse: most of a function layer is
    /// unassigned on a real board, and the blank caps are what prove the control draws
    /// "nothing here" differently from "not read yet".
    /// </summary>
    internal static readonly string[] FunctionLayer =
    [
        "~", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "Del",
        "", "", "", "", "", "", "", "", "", "", "PrSc", "",
        "", "", "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", "PgUp", "PgDn", "Fn",
    ];

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

        MouseHosts,

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
            Id = CapabilityIds.PointerDpiXy,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.RangeGroup,
            Title = "Разрешение по осям",
            Subtitle = "Раздельные X и Y, для тех, кто ведёт прицел по горизонтали",
            DeclaredStatus = CapabilityStatus.Untested,

            // One range for both axes rather than one each. The sensor has a single
            // scale and saying so twice would let the two copies drift apart the first
            // time a firmware moved the ceiling.
            Range = new CapabilityRange { Min = 200, Max = 8000, Step = 50, Unit = "DPI" },
            Members = [new(0, "По X"), new(1, "По Y")],
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
            Id = CapabilityIds.PointerResponse,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.Curve,
            Title = "Кривая отклика",
            Subtitle = "Пишется в память мыши и работает без приложения",
            DeclaredStatus = CapabilityStatus.Untested,

            // Centred on zero: a band below the line slows the pointer at that speed and
            // one above it quickens. A scale running from zero would make "no change"
            // look like the bottom of the range instead of the middle of it.
            Range = new CapabilityRange { Min = -50, Max = 50, Unit = "%" },
            Members =
            [
                new(0, "0%"),
                new(1, "15%"),
                new(2, "30%"),
                new(3, "50%"),
                new(4, "70%"),
                new(5, "85%"),
                new(6, "100%"),
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

            // The DPI button cycles steps on the device itself, so the application is
            // not the only writer and the reading has to be polled.
            IsVolatile = true,

            Members =
            [
                new(0, "Кнопка 4"),
                new(1, "Кнопка 5"),
                new(2, "Колесо, нажатие"),
                new(3, "Кнопка DPI") { Hint = "меняет значение извне" },
            ],

            // Straight from libratbag's normalised action types, which already did the
            // work of reconciling what each vendor calls these.
            Choices =
            [
                new(0, "Ничего"),
                new(1, "Назад"),
                new(2, "Вперёд"),
                new(3, "Средняя кнопка"),
                new(4, "Ступень DPI"),
                new(5, "Смена профиля"),
                new(6, "Показать заряд"),
                new(7, "Макрос"),
            ],
        },

        LightOn,
        Brightness,
        LightEffect,

        new()
        {
            Id = CapabilityIds.LightColor,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Lighting,
            Title = "Цвет зон",
            Subtitle = "Две зоны, каждая своим цветом",
            DeclaredStatus = CapabilityStatus.Untested,
            Members = [new(0, "Логотип"), new(1, "Колесо")],
        },

        new()
        {
            Id = CapabilityIds.ProfilesOnboard,
            Domain = CapabilityDomain.Profiles,
            Primitive = CapabilityPrimitive.ProfileSet,
            Title = "Профили устройства",

            // Not the same thing as the application's profiles, and the interface has to
            // keep the two apart: these live in the device and travel with it.
            Subtitle = "Три слота в памяти мыши",
            DeclaredStatus = CapabilityStatus.Untested,
            Members =
            [
                new(0, "Onboard 1 · Игровой") { Hint = "3200 DPI · 1000 Гц · SmartShift выключен" },
                new(1, "Onboard 2 · Работа") { Hint = "1600 DPI · 500 Гц · SmartShift включён" },
                new(2, "Onboard 3")
                {
                    Hint = "Слот читается, запись в него не подтверждена",
                    IsEmpty = true,
                },
            ],
        },
    ];

    public static IReadOnlyList<Capability> Keyboard { get; } =
    [
        Battery,
        Charging,
        KeyboardHosts,

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

            // The layer moves when someone holds Fn, which the application never sees.
            IsVolatile = true,

            LayerCount = 4,
            GridColumns = 12,
            Members = KeyPositions,
            Choices = Keycodes,
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

        LightOn,
        Brightness,
        LightEffect,

        new()
        {
            Id = CapabilityIds.LightColor,
            Domain = CapabilityDomain.Light,
            Primitive = CapabilityPrimitive.Lighting,
            Title = "Цвет",

            // Artboard 10c states the limitation as a fact about the board rather than
            // as a missing feature, and so does this line.
            Subtitle = "Один цвет на всю плату — по клавишам не задаётся",
            DeclaredStatus = CapabilityStatus.Untested,
            Members = [new(0, "Вся плата")],
        },

        new()
        {
            Id = CapabilityIds.ProfilesOnboard,
            Domain = CapabilityDomain.Profiles,
            Primitive = CapabilityPrimitive.ProfileSet,
            Title = "Профили устройства",
            Subtitle = "Профиль — это слои, макросы и подсветка целиком",
            DeclaredStatus = CapabilityStatus.Untested,
            Members =
            [
                new(0, "Windows") { Hint = "4 слоя · 2 макроса · статичный свет" },
                new(1, "macOS") { Hint = "Cmd и Option переставлены · 4 слоя" },
                new(2, "Игровой") { Hint = "Win заблокирована · подсветка WASD" },
            ],
        },
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

    /// <summary>
    /// A dongle and two Bluetooth slots, as artboard 9b draws them. The keyboard's are
    /// separate because the wording is not interchangeable: one pairs by a button under
    /// the mouse, the other by a chord on the board, and a shared sentence would be
    /// wrong for one of them.
    /// </summary>
    private static Capability MouseHosts => new()
    {
        Id = CapabilityIds.LinkHosts,
        Domain = CapabilityDomain.Link,
        Primitive = CapabilityPrimitive.HostSlots,
        Title = "Хосты",
        Subtitle = "Переключается и кнопкой на днище мыши",
        DeclaredStatus = CapabilityStatus.Untested,

        // The user can press the switch button on the device itself, so the app is not
        // the only writer here.
        IsVolatile = true,

        Members =
        [
            new(0, "Хост 1 · донгл Lightspeed") { Hint = "Этот компьютер" },
            new(1, "Хост 2 · Bluetooth") { Hint = "Ноутбук · последний раз вчера" },
            new(2, "Хост 3 · свободен")
            {
                Hint = "Удерживайте кнопку на днище 3 секунды",
                IsEmpty = true,
            },
        ],
    };

    private static Capability KeyboardHosts => new()
    {
        Id = CapabilityIds.LinkHosts,
        Domain = CapabilityDomain.Link,
        Primitive = CapabilityPrimitive.HostSlots,
        Title = "Хосты",
        Subtitle = "Переключается и с клавиатуры — Fn + 1…3",
        DeclaredStatus = CapabilityStatus.Untested,
        IsVolatile = true,

        Members =
        [
            new(0, "Хост 1 · Bluetooth") { Hint = "Рабочий ноутбук" },
            new(1, "Хост 2 · Bluetooth") { Hint = "Этот компьютер · подключено 3 часа" },
            new(2, "Хост 3 · свободен")
            {
                Hint = "Fn + 3 удерживать 3 секунды для сопряжения",
                IsEmpty = true,
            },
        ],
    };

    private static Capability LightOn => new()
    {
        Id = CapabilityIds.LightOn,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Toggle,
        Title = "Подсветка",
        DeclaredStatus = CapabilityStatus.Untested,
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

    /// <summary>
    /// A four-row block rather than the seventy-five keys an Air75 really has.
    ///
    /// The layout is the one part of this file that is a simplification rather than an
    /// invention, and it is worth naming: the control has to prove it can draw rows,
    /// layers and an unread key, and forty-eight positions prove all three. A real
    /// layout comes from the-via/keyboards, which is a second external catalog and a
    /// decision of its own — see docs/CAPABILITIES.md §9.
    /// </summary>
    private static IReadOnlyList<CapabilityMember> KeyPositions =>
        [.. BaseLayer.Select((legend, i) => new CapabilityMember(i, $"Ряд {(i / 12) + 1}, {legend}"))];

    /// <summary>What a selected key may be reassigned to. Short, and short on purpose.</summary>
    private static IReadOnlyList<CapabilityChoice> Keycodes =>
    [
        new(0, "—"),
        new(1, "Esc"),
        new(2, "Tab"),
        new(3, "Fn"),
        new(4, "Ctrl"),
        new(5, "Alt"),
        new(6, "Win"),
        new(7, "⇧"),
        new(8, "⌫"),
        new(9, "⏎"),
        new(10, "Home"),
        new(11, "End"),
        new(12, "PgUp"),
        new(13, "PgDn"),
    ];
}
