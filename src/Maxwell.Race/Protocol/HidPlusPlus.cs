namespace Maxwell.Race.Protocol;

/// <summary>
/// Logitech HID++ 2.0, the read half.
///
/// The point of implementing this rather than a vendor integration is arithmetic: one
/// protocol covers every Logitech peripheral of the last decade, where a per-model driver
/// covers one. It is a class protocol in the same sense VIA is — found by usage page
/// rather than by product id — and that is what makes the long tail affordable.
///
/// Everything here reads. Nothing writes. A wrong write to a mouse is a mouse with a DPI
/// nobody asked for; a wrong write to a receiver can unpair the device driving the cursor,
/// and getting the read path proven first costs a day and saves that.
/// </summary>
public static class HidPlusPlus
{
    /// <summary>Short report: seven bytes, for the root feature and for errors.</summary>
    public const byte ShortReport = 0x10;

    /// <summary>Long report: twenty bytes, everything else.</summary>
    public const byte LongReport = 0x11;

    /// <summary>The receiver itself rather than anything paired to it.</summary>
    public const byte Receiver = 0xFF;

    /// <summary>
    /// Marks our requests so a reply can be told from another application's. Any non-zero
    /// nibble does; zero is reserved for notifications the device sends unprompted, which
    /// is exactly the traffic that has to be filtered out.
    ///
    /// One number for the whole application is not enough, and the reason cost a day.
    /// Telling our traffic from G HUB's was never the hard part: the hard part is telling
    /// our traffic from our own. A feature lookup names the feature it wants in the
    /// request and the answer carries no echo of it — so two lookups in flight at once are
    /// identical questions with interchangeable answers, and each reader takes whichever
    /// arrives first. Measured on a live mouse, forty races swapped forty times. What it
    /// cost on screen was a mouse named "Logitech b" with a control character after it:
    /// the name lookup had been handed the battery's index, so the name was read out of
    /// the charge reading — 98%, which is the letter b.
    ///
    /// So this is the floor rather than the answer. Each channel takes an id of its own
    /// from <see cref="Marked"/>, which is what the nibble is for.
    /// </summary>
    public const byte SoftwareId = 0x05;

    /// <summary>
    /// The function byte: which function in the high nibble, who is asking in the low one.
    /// </summary>
    public static byte Marked(byte function, byte softwareId) =>
        (byte)((function << 4) | (softwareId & 0x0F));

    // Features, by their published ids.
    public const ushort FeatureRoot = 0x0000;
    public const ushort FeatureDeviceInformation = 0x0003;
    public const ushort FeatureDeviceName = 0x0005;
    public const ushort FeatureBatteryLevel = 0x1000;
    public const ushort FeatureUnifiedBattery = 0x1004;

    /// <summary>The root feature always sits at index zero; everything else is looked up.</summary>
    public const byte RootIndex = 0x00;

    /// <summary>A HID++ 2.0 error comes back in this slot where a feature index would be.</summary>
    private const byte ErrorMarker = 0xFF;

    /// <summary>And this one for a HID++ 1.0 error, which older receivers still answer with.</summary>
    private const byte LegacyErrorMarker = 0x8F;

    /// <summary>
    /// Builds a request at the given report size. The report is always exactly the length
    /// the interface declares — HID refuses a short buffer rather than padding it.
    ///
    /// Which size to use is not a free choice: on Windows the short and long reports are
    /// separate top-level collections and therefore separate device paths, and a reply
    /// only ever arrives on the collection that owns its report id. Asking on the long
    /// interface and listening there got silence from a receiver that answers the short
    /// one immediately.
    /// </summary>
    public static byte[] Request(byte device, byte feature, byte function, params byte[] parameters) =>
        Request(20, device, feature, function, parameters);

    public static byte[] Request(int size, byte device, byte feature, byte function, params byte[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return Request(size, device, feature, function, SoftwareId, parameters);
    }

    /// <summary>The same, signed by the reader that is asking rather than by the application.</summary>
    public static byte[] Request(
        int size, byte device, byte feature, byte function, byte softwareId, ReadOnlySpan<byte> parameters)
    {
        byte[] report = new byte[size];

        report[0] = size >= 20 ? LongReport : ShortReport;
        report[1] = device;
        report[2] = feature;
        report[3] = Marked(function, softwareId);

        for (int i = 0; i < parameters.Length && i + 4 < report.Length; i++) report[i + 4] = parameters[i];

        return report;
    }

    /// <summary>
    /// Whether a reply answers this request rather than being something the device
    /// volunteered. Errors count as answers: they are the device saying no to this
    /// question, and treating them as noise would mean waiting out the timeout instead.
    /// </summary>
    public static bool Answers(byte[] reply, byte device, byte feature, byte function) =>
        Answers(reply, device, feature, function, SoftwareId);

    /// <summary>The same, for a reader that signed its request with an id of its own.</summary>
    public static bool Answers(byte[] reply, byte device, byte feature, byte function, byte softwareId)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.Length < 4) return false;
        if (reply[1] != device) return false;

        if (IsError(reply)) return true;

        return reply[2] == feature && reply[3] == Marked(function, softwareId);
    }

    public static bool IsError(byte[] reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return reply.Length >= 3 && reply[2] is ErrorMarker or LegacyErrorMarker;
    }

    /// <summary>
    /// The index a feature currently sits at on this device. Indices are per-device and
    /// per-firmware, so they are asked for rather than assumed — the same feature is at a
    /// different index on a mouse and on the keyboard beside it.
    /// </summary>
    public static byte[] GetFeature(int size, byte device, ushort feature) =>
        GetFeature(size, device, feature, SoftwareId);

    /// <summary>
    /// The same, signed. This is the request the whole software id exists for: it is the
    /// one question whose answer says nothing about what was asked, so two of them at once
    /// can only be told apart by who signed them.
    /// </summary>
    public static byte[] GetFeature(int size, byte device, ushort feature, byte softwareId) =>
        Request(size, device, RootIndex, 0x00, softwareId, [(byte)(feature >> 8), (byte)(feature & 0xFF)]);

    /// <summary>
    /// A ping, and the only reliable way to tell an occupied pairing slot from an empty
    /// one: an unpaired slot answers nothing at all, and a device that is merely asleep
    /// answers an error rather than silence.
    /// </summary>
    public static byte[] Ping(int size, byte device) => Ping(size, device, SoftwareId);

    /// <summary>The same, signed.</summary>
    public static byte[] Ping(int size, byte device, byte softwareId) =>
        Request(size, device, RootIndex, 0x01, softwareId, [0x00, 0x00, 0xAA]);

    /// <summary>
    /// What the device said no about. The two error dialects put the code in different
    /// places, and telling "nothing is paired here" from "it is asleep" is the difference
    /// between a slot worth showing and one worth skipping.
    /// </summary>
    public static string Explain(byte[] reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (!IsError(reply) || reply.Length < 6) return string.Empty;

        // HID++ 1.0 keeps the code at byte 5; 2.0 uses a different set at the same place.
        byte code = reply[5];

        if (reply[2] == LegacyErrorMarker)
        {
            return code switch
            {
                0x05 => "не удалось соединиться",
                0x06 => "слотов больше нет",
                0x08 => "занято — устройство не отвечает",
                0x09 => "устройство неизвестно",
                0x0A => "нет ресурсов",
                _ => $"ошибка 1.0 · {code}",
            };
        }

        return code switch
        {
            0x01 => "неизвестная команда",
            0x02 => "неверный аргумент",
            0x03 => "значение вне диапазона",
            0x04 => "аппаратная ошибка",
            0x05 => "не реализовано",
            0x09 => "занято",
            _ => $"ошибка 2.0 · {code}",
        };
    }

    /// <summary>True when a ping came back from a device rather than from an empty slot.</summary>
    public static bool Pong(byte[] reply) =>
        reply.Length >= 7 && !IsError(reply) && reply[6] == 0xAA;
}
