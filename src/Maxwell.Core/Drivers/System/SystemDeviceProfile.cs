using System.Globalization;
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

    public static bool IsPeripheral(IEnumerable<SystemDeviceNode> nodes)
    {
        List<SystemDeviceNode> all = [.. nodes];

        return all.Any(n => Peripheral.Contains(n.ClassGuid)) && IsConnected(all);
    }

    /// <summary>
    /// Whether a low-energy device is here or merely remembered.
    ///
    /// Measured on a Bluetooth mouse, asleep and then woken: awake it has two HID nodes
    /// alongside its services, and asleep it has only the services. The services are what
    /// pairing leaves behind — they stay for as long as the pairing does, whether or not
    /// anything is listening at the other end.
    ///
    /// Which matters because Windows keeps answering for a device that is not there. It
    /// went on reporting the mouse's charge at a hundred per cent with the mouse asleep on
    /// the desk, and a charge that answers is read here as a device that answers — so the
    /// row sat there claiming to be live. The same lesson as a headset's dongle
    /// remembering its battery: a number out of somebody's memory is not the device
    /// speaking.
    ///
    /// Deliberately not a property. There is one on these nodes that looks like the
    /// answer, and it reads false with the mouse awake and working; every key on the node
    /// was dumped and none of them says connected. What does say it is whether the device
    /// has grown anything but its services.
    /// </summary>
    private static bool IsConnected(List<SystemDeviceNode> nodes)
    {
        if (!nodes.Any(n => n.Link == SystemDeviceLink.BluetoothLe)) return true;

        return nodes.Any(n =>
            !n.InstanceId.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase)
            && !n.InstanceId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase));
    }

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
        ?? "Unidentified device";

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
        // Evidence, not a verdict — and that distinction is a defect, not a nicety.
        //
        // A Class of Device that named something used to end the search, so a device that
        // carried the field without filling it in came out as Unknown with its real
        // evidence never consulted. Which is every low-energy device: Bluetooth LE does
        // not use this field at all, it has an Appearance instead. A mouse that had been
        // reading as a mouse turned into "Unknown" the day its own node was found, on the
        // strength of a field full of zeroes.
        if (FromClassOfDevice(nodes) is var stated && stated != DeviceClass.Unknown) return stated;

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
    /// What classic Bluetooth says about itself, which is the one place a device states
    /// its own kind to a standard. Silence — no field, or a field of zeroes — is not an
    /// answer and must not be read as one.
    /// </summary>
    private static DeviceClass FromClassOfDevice(IReadOnlyList<SystemDeviceNode> nodes)
    {
        if (nodes.Select(n => n.ClassOfDevice).FirstOrDefault(c => c is > 0) is not { } cod)
        {
            return DeviceClass.Unknown;
        }

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

        // The peripheral minor class is two fields sharing six bits, not one value: the
        // top two say keyboard, pointer or both, and the bottom four say what kind of
        // thing it is. Reading it as a single number puts a gamepad — 0b0010 in the lower
        // field — nowhere at all.
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

    /// <summary>
    /// Everything that identifies this device to the operating system, so a vendor driver
    /// finding the same hardware can be recognised as talking about the same object.
    /// </summary>
    /// <summary>
    /// Who the device says made it, ignoring what Windows says. The property beside it
    /// names whoever wrote the driver — Microsoft, for everything on the Bluetooth stack
    /// — which is true and is not what anybody means by a manufacturer.
    /// </summary>
    public static string? Maker(IReadOnlyList<SystemDeviceNode> nodes) =>
        nodes.Select(n => n.Manufacturer)
            .FirstOrDefault(m => m is { Length: > 0 } && !Anonymous.Contains(m));

    /// <summary>The revision it reports, where it reports one.</summary>
    public static string? Revision(IReadOnlyList<SystemDeviceNode> nodes) =>
        nodes.Select(n => n.Revision).FirstOrDefault(r => r is not null) is { } revision
            ? revision.ToString(CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// Names that mean "nobody said". Windows fills the manufacturer in with whoever
    /// supplied the driver, and printing that as the maker of the hardware is worse than
    /// printing nothing: it is wrong, and it is wrong in a way that looks like an answer.
    /// </summary>
    private static readonly HashSet<string> Anonymous =
        new(StringComparer.OrdinalIgnoreCase) { "Microsoft", "(Standard system devices)", "(Standard USB Host Controller)" };

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
                Label = viaDongle ? "USB dongle" : "Receiver",
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
                Label = "Dock",
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
                    _ => "Unknown",
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
