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
            Title = "Signal strength",
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
            Title = "Sensor resolution",
            Subtitle = "The steps are read from the device",
            DeclaredStatus = CapabilityStatus.Untested,
            Range = new CapabilityRange { Min = 200, Max = 8000, Step = 50, Unit = "DPI" },
        },

        new()
        {
            Id = CapabilityIds.PointerDpiXy,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.RangeGroup,
            Title = "Resolution per axis",
            Subtitle = "X and Y apart, for anyone who tracks horizontally",
            DeclaredStatus = CapabilityStatus.Untested,

            // One range for both axes rather than one each. The sensor has a single
            // scale and saying so twice would let the two copies drift apart the first
            // time a firmware moved the ceiling.
            Range = new CapabilityRange { Min = 200, Max = 8000, Step = 50, Unit = "DPI" },
            Members = [new(0, "X axis"), new(1, "Y axis")],
        },

        new()
        {
            Id = CapabilityIds.PointerReportRate,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Report rate",
            Subtitle = "Higher is more responsive and flattens the battery faster",
            DeclaredStatus = CapabilityStatus.Untested,
            Choices =
            [
                new(0, "125 Hz"),
                new(1, "250 Hz"),
                new(2, "500 Hz"),
                new(3, "1000 Hz"),
            ],
        },

        new()
        {
            Id = CapabilityIds.PointerResponse,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.Curve,
            Title = "Response curve",
            Subtitle = "Written into the mouse and works without the application",
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
            Title = "Button assignment",
            Subtitle = "The layout is written into the mouse",
            DeclaredStatus = CapabilityStatus.Untested,

            // The DPI button cycles steps on the device itself, so the application is
            // not the only writer and the reading has to be polled.
            IsVolatile = true,

            Members =
            [
                new(0, "Button 4"),
                new(1, "Button 5"),
                new(2, "Wheel click"),
                new(3, "DPI button") { Hint = "moves the value from outside" },
            ],

            // Straight from libratbag's normalised action types, which already did the
            // work of reconciling what each vendor calls these.
            Choices =
            [
                new(0, "Nothing"),
                new(1, "Back"),
                new(2, "Forward"),
                new(3, "Middle button"),
                new(4, "DPI step"),
                new(5, "Profile change"),
                new(6, "Show the charge"),
                new(7, "Macro"),
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
            Title = "Zone colour",
            Subtitle = "Two zones, each its own colour",
            DeclaredStatus = CapabilityStatus.Untested,
            Members = [new(0, "Logo"), new(1, "Wheel")],
        },

        new()
        {
            Id = CapabilityIds.ProfilesOnboard,
            Domain = CapabilityDomain.Profiles,
            Primitive = CapabilityPrimitive.ProfileSet,
            Title = "Device profiles",

            // Not the same thing as the application's profiles, and the interface has to
            // keep the two apart: these live in the device and travel with it.
            Subtitle = "Three slots in the mouse's memory",
            DeclaredStatus = CapabilityStatus.Untested,
            Members =
            [
                new(0, "Onboard 1 · Gaming") { Hint = "3200 DPI · 1000 Hz · SmartShift off" },
                new(1, "Onboard 2 · Work") { Hint = "1600 DPI · 500 Hz · SmartShift on" },
                new(2, "Onboard 3")
                {
                    Hint = "The slot reads; writing to it has not been confirmed",
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
            Title = "Layout",

            // The distinction people care about: VIA writes the layout into the keyboard,
            // so it keeps working with the application closed and on another computer.
            Subtitle = "Written into the keyboard and works without the application running",
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
            Title = "Layers",
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
            Title = "Colour",

            // Artboard 10c states the limitation as a fact about the board rather than
            // as a missing feature, and so does this line.
            Subtitle = "One colour for the whole board — not settable per key",
            DeclaredStatus = CapabilityStatus.Untested,
            Members = [new(0, "Whole board")],
        },

        new()
        {
            Id = CapabilityIds.ProfilesOnboard,
            Domain = CapabilityDomain.Profiles,
            Primitive = CapabilityPrimitive.ProfileSet,
            Title = "Device profiles",
            Subtitle = "A profile is the layers, the macros and the lighting, all of it",
            DeclaredStatus = CapabilityStatus.Untested,
            Members =
            [
                new(0, "Windows") { Hint = "4 layers · 2 macros · static light" },
                new(1, "macOS") { Hint = "Cmd and Option swapped · 4 layers" },
                new(2, "Gaming") { Hint = "Win locked · WASD lit" },
            ],
        },
    ];

    // ---------- pieces shared by both ----------

    private static Capability Battery => new()
    {
        Id = CapabilityIds.PowerBattery,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charge",
        Access = CapabilityAccess.Read,
        DeclaredStatus = CapabilityStatus.Untested,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private static Capability Charging => new()
    {
        Id = CapabilityIds.PowerCharging,
        Domain = CapabilityDomain.Power,
        Primitive = CapabilityPrimitive.Readout,
        Title = "Charging",
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
        Title = "Hosts",
        Subtitle = "Switched with the button underneath the mouse too",
        DeclaredStatus = CapabilityStatus.Untested,

        // The user can press the switch button on the device itself, so the app is not
        // the only writer here.
        IsVolatile = true,

        Members =
        [
            new(0, "Host 1 · Lightspeed dongle") { Hint = "This computer" },
            new(1, "Host 2 · Bluetooth") { Hint = "Laptop · last seen yesterday" },
            new(2, "Host 3 · free")
            {
                Hint = "Hold the button underneath for 3 seconds",
                IsEmpty = true,
            },
        ],
    };

    private static Capability KeyboardHosts => new()
    {
        Id = CapabilityIds.LinkHosts,
        Domain = CapabilityDomain.Link,
        Primitive = CapabilityPrimitive.HostSlots,
        Title = "Hosts",
        Subtitle = "Switched from the keyboard too — Fn + 1…3",
        DeclaredStatus = CapabilityStatus.Untested,
        IsVolatile = true,

        Members =
        [
            new(0, "Host 1 · Bluetooth") { Hint = "Work laptop" },
            new(1, "Host 2 · Bluetooth") { Hint = "This computer · connected for 3 hours" },
            new(2, "Host 3 · free")
            {
                Hint = "Hold Fn + 3 for 3 seconds to pair",
                IsEmpty = true,
            },
        ],
    };

    private static Capability LightOn => new()
    {
        Id = CapabilityIds.LightOn,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Toggle,
        Title = "Lighting",
        DeclaredStatus = CapabilityStatus.Untested,
    };

    private static Capability Brightness => new()
    {
        Id = CapabilityIds.LightBrightness,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Range,
        Title = "Lighting brightness",
        DeclaredStatus = CapabilityStatus.Untested,
        Range = new CapabilityRange { Min = 0, Max = 100, Unit = "%" },
    };

    private static Capability LightEffect => new()
    {
        Id = CapabilityIds.LightEffect,
        Domain = CapabilityDomain.Light,
        Primitive = CapabilityPrimitive.Choice,
        Title = "Effect",
        DeclaredStatus = CapabilityStatus.Untested,
        Choices =
        [
            new(0, "Off"),
            new(1, "Steady"),
            new(2, "Breathing"),
            new(3, "Cycle"),
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
        [.. BaseLayer.Select((legend, i) => new CapabilityMember(i, $"Row {(i / 12) + 1}, {legend}"))];

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
