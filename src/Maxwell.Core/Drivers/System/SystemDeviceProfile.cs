using Maxwell.Core.Devices;
using Maxwell.Race.Abstractions;

namespace Maxwell.Core.Drivers.System;

/// <summary>
/// Turns a pile of operating-system device nodes into something with a name and a class.
///
/// Pure, and separated from the driver for the same reason the polling policy is: every
/// rule here was derived from one machine's device tree, most of them are wrong for some
/// machine somewhere, and being wrong is only cheap if the rule can be re-run against a
/// captured node list without the hardware being present.
/// </summary>
internal static class SystemDeviceProfile
{
    private static readonly Guid HidClass = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private static readonly Guid KeyboardClass = new("4d36e96b-e325-11ce-bfc1-08002be10318");
    private static readonly Guid MouseClass = new("4d36e96f-e325-11ce-bfc1-08002be10318");
    private static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    private static readonly Guid AudioEndpointClass = new("c166523c-fe0c-4a94-a586-f1a80cfbbf3e");
    private static readonly Guid XnaComposite = new("d61ca365-5af4-4486-998b-9db4734c6ca3");
    private static readonly Guid XboxPeripheral = new("bb7df450-25cd-4504-9e7d-c435f514e5d0");
    private static readonly Guid BluetoothClass = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");

    /// <summary>
    /// Setup classes that mean a person plugged this in. Everything else on the USB
    /// enumerators is the wiring that carries peripherals rather than a peripheral.
    ///
    /// Declared after the fields it is built from, and that is load-bearing: static
    /// initialisers run in declaration order, so putting this first silently filled it
    /// with eight copies of the empty guid and the device list came back nearly bare.
    /// </summary>
    private static readonly HashSet<Guid> Peripheral =
    [
        HidClass, KeyboardClass, MouseClass, MediaClass, AudioEndpointClass, XnaComposite, XboxPeripheral,
        BluetoothClass,
    ];

    public static bool IsPeripheral(IEnumerable<SystemDeviceNode> nodes) =>
        nodes.Any(n => Peripheral.Contains(n.ClassGuid));

    /// <summary>
    /// What to call the device, and the order is not the obvious one. A Bluetooth device
    /// carries the name its owner gave it on the device root node only; every profile
    /// node beneath is named after the profile, which is how a headset ends up called
    /// "Avrcp Transport". On USB the product string is the bus-reported description,
    /// because the friendly name there is the driver's own label and reads "USB Input
    /// Device" for a mouse, a microphone and a keyboard alike.
    /// </summary>
    public static string Name(IReadOnlyList<SystemDeviceNode> nodes) =>
        nodes.FirstOrDefault(n => n.IsDeviceRoot && !string.IsNullOrWhiteSpace(n.Name))?.Name
        ?? nodes.Select(n => n.BusReportedName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
        ?? nodes.Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
        ?? "Неопознанное устройство";

    /// <summary>
    /// What kind of thing this is, from what the device says about itself rather than
    /// from recognising the model.
    ///
    /// Bluetooth answers properly: the Class of Device field is a standard every device
    /// fills in, so a headset says headset without anyone having heard of the brand. USB
    /// has no equivalent, and the setup classes Windows assigns are the nearest thing —
    /// good enough for pointers, keyboards and controllers, and silent about audio, where
    /// nothing at this level separates a microphone from a headset. That gap is left as
    /// <see cref="DeviceClass.Unknown"/> on purpose rather than guessed at from the name.
    /// </summary>
    public static DeviceClass Classify(IReadOnlyList<SystemDeviceNode> nodes)
    {
        if (nodes.Select(n => n.ClassOfDevice).FirstOrDefault(c => c.HasValue) is { } cod)
        {
            uint major = (cod >> 8) & 0x1F;
            uint minor = (cod >> 2) & 0x3F;

            if (major == 0x04)
            {
                return minor switch
                {
                    0x01 or 0x02 => DeviceClass.Headset,
                    0x06 => DeviceClass.Earbuds,
                    0x0A or 0x0B => DeviceClass.Speaker,
                    _ => DeviceClass.Unknown,
                };
            }

            // The peripheral minor class is two fields sharing six bits, not one value:
            // the top two say keyboard, pointer or both, and the bottom four say what
            // kind of thing it is. Reading it as a single number puts a gamepad — 0b0010
            // in the lower field — nowhere at all.
            if (major == 0x05)
            {
                return ((minor >> 4) & 0x03, minor & 0x0F) switch
                {
                    (0x03, _) => DeviceClass.Receiver,
                    (0x01, _) => DeviceClass.Keyboard,
                    (0x02, _) => DeviceClass.Mouse,
                    (_, 0x01 or 0x02) => DeviceClass.Gamepad,
                    _ => DeviceClass.Unknown,
                };
            }

            return DeviceClass.Unknown;
        }

        // Gamepad evidence first, and this order is a lesson from a real device. The
        // Flydigi dongle emulates a keyboard and a mouse alongside its XInput function,
        // so the keyboard-plus-mouse rule below called a gamepad a receiver. An XInput
        // marker cannot be emulation of something else: IG_ in an instance id and the
        // Xna setup classes exist only for controllers.
        if (nodes.Any(n =>
                n.ClassGuid == XnaComposite
                || n.ClassGuid == XboxPeripheral
                || n.InstanceId.Contains("IG_", StringComparison.OrdinalIgnoreCase)))
        {
            return DeviceClass.Gamepad;
        }

        bool mouse = nodes.Any(n => n.ClassGuid == MouseClass);
        bool keyboard = nodes.Any(n => n.ClassGuid == KeyboardClass);

        // Both at once is not a combo device: it is a receiver with two things paired to
        // it. Only the vendor protocol can say what those two things are.
        if (mouse && keyboard) return DeviceClass.Receiver;
        if (mouse) return DeviceClass.Mouse;
        if (keyboard) return DeviceClass.Keyboard;

        return DeviceClass.Unknown;
    }

    /// <summary>
    /// Everything that identifies this device to the operating system, so a vendor driver
    /// finding the same hardware can be recognised as talking about the same object.
    /// </summary>
    public static HashSet<string> HardwareIds(Guid container, IEnumerable<SystemDeviceNode> nodes)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { $"container:{container:D}" };

        foreach (SystemDeviceNode node in nodes)
        {
            if (node is { VendorId: { } vid, ProductId: { } pid }) ids.Add($"usb:{vid:x4}:{pid:x4}");
            if (node.Address is { Length: > 0 } address) ids.Add($"bt:{address.ToLowerInvariant()}");
        }

        return ids;
    }

    public static IReadOnlyList<TransportInfo> Transports(
        IReadOnlyList<SystemDeviceNode> nodes,
        DeviceClass deviceClass,
        IReadOnlySet<string>? dongleUsb = null,
        IReadOnlySet<string>? dockUsb = null)
    {
        var transports = new List<TransportInfo>();
        bool audio = nodes.Any(n => n.ClassGuid == MediaClass || n.ClassGuid == AudioEndpointClass);

        static string? UsbId(SystemDeviceNode node) =>
            node is { VendorId: { } vid, ProductId: { } pid } ? $"{vid:x4}:{pid:x4}" : null;

        // The USB nodes split by what the catalogue says the plug is: the model's own
        // dongle, its dock, or a plain cable. One chip each — a gamepad whose dongle
        // and charging dock are both in shows both, because both are really there.
        List<SystemDeviceNode> usb = [.. nodes.Where(n => n.Link == SystemDeviceLink.Usb)];

        bool viaDongle = dongleUsb is not null && usb.Any(n => UsbId(n) is { } id && dongleUsb.Contains(id));
        bool viaDock = dockUsb is not null && usb.Any(n => UsbId(n) is { } id && dockUsb.Contains(id));
        bool plainUsb = usb.Any(n => UsbId(n) is not { } id
            || ((dongleUsb is null || !dongleUsb.Contains(id)) && (dockUsb is null || !dockUsb.Contains(id))));

        if (viaDongle || (plainUsb && deviceClass == DeviceClass.Receiver))
        {
            transports.Add(new TransportInfo
            {
                Kind = TransportKind.Dongle,
                Label = viaDongle ? "USB-донгл" : "Приёмник",
                IsPresent = true,
                CarriesControl = false,
                CarriesAudio = audio,
            });
        }
        else if (plainUsb)
        {
            transports.Add(new TransportInfo
            {
                Kind = TransportKind.UsbCable,
                Label = "USB",
                IsPresent = true,
                CarriesControl = false,
                CarriesAudio = audio,
            });
        }

        if (viaDock)
        {
            transports.Add(new TransportInfo
            {
                Kind = TransportKind.UsbCable,
                Label = "Док",
                IsPresent = true,
                CarriesControl = false,
                CarriesAudio = false,
            });
        }

        foreach (SystemDeviceLink link in nodes.Select(n => n.Link).Distinct().OrderBy(l => l))
        {
            if (link == SystemDeviceLink.Usb) continue;

            transports.Add(new TransportInfo
            {
                Kind = link is SystemDeviceLink.Bluetooth or SystemDeviceLink.BluetoothLe
                    ? TransportKind.Bluetooth
                    : TransportKind.Unknown,
                Label = link switch
                {
                    SystemDeviceLink.Bluetooth => "Bluetooth",
                    SystemDeviceLink.BluetoothLe => "Bluetooth LE",
                    _ => "Неизвестно",
                },
                IsPresent = true,

                // Nothing at this tier can change a setting. Saying otherwise would put
                // controls on a page that have no way to reach the device.
                CarriesControl = false,
                CarriesAudio = audio,
            });
        }

        return transports;
    }
}
