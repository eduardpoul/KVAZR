using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Abstractions;
using Maxwell.Race.Native;

namespace Maxwell.Race.Diagnostics;

/// <summary>One axis or wheel a pointer declares, with the range it declares for it.</summary>
public sealed record PointerAxis(ushort UsagePage, ushort Usage, string Name, int Bits, int Min, int Max)
{
    /// <summary>
    /// Whether the range is wider than a byte, which is the practical difference between
    /// a mouse that can be moved quickly and one that saturates.
    /// </summary>
    public bool IsWide => Bits > 8;
}

/// <summary>
/// What a pointing device says about itself in its own report descriptor.
///
/// This is the floor: every mouse on every link declares it, because Windows cannot drive
/// one that does not, and it needs no vendor protocol, no driver and no permission. It is
/// also all there is for most mice — the interesting things a gaming mouse can be asked
/// (its resolution, its polling rate, its onboard profiles) live in vendor protocols, one
/// per manufacturer, and a device from a maker nobody has written one for still has to
/// have a page worth opening.
/// </summary>
public sealed record PointerFacts
{
    public required HidInterface Face { get; init; }

    /// <summary>USB, Bluetooth or Bluetooth LE, read off the device path.</summary>
    public required string Link { get; init; }

    /// <summary>
    /// How many buttons it declares. Declared rather than observed: the descriptor names
    /// the run of button usages, so this is known before anybody clicks anything.
    /// </summary>
    public required int Buttons { get; init; }

    public required IReadOnlyList<PointerAxis> Axes { get; init; }

    /// <summary>
    /// Whether it declares a resolution multiplier — the high-resolution wheel, where one
    /// physical detent is reported as several steps. Its presence is the only thing that
    /// distinguishes a free-spinning wheel from a clicky one without turning it.
    /// </summary>
    public bool HasFineWheel => Axes.Any(a => a is { UsagePage: 0x01, Usage: 0x48 });

    public bool HasTilt => Axes.Any(a => a is { UsagePage: 0x0C, Usage: 0x0238 });
}

/// <summary>
/// Reads the above out of any HID collection that says it is a mouse.
///
/// Opened with no access rights at all — <c>CreateFile</c> with a zero desired access —
/// which is what lets this describe a mouse the operating system is actively using. A
/// read-write open of a mouse is refused on Windows, and that refusal is why a pointing
/// device has to be understood from its declaration rather than from its traffic.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PointerSurvey
{
    /// <summary>Generic Desktop, usage Mouse: the collection Windows drives a pointer from.</summary>
    private const ushort GenericDesktop = 0x01;
    private const ushort MouseUsage = 0x02;
    private const ushort ButtonPage = 0x09;

    public static bool IsPointer(HidInterface face)
    {
        ArgumentNullException.ThrowIfNull(face);

        return face is { UsagePage: GenericDesktop, Usage: MouseUsage };
    }

    public static PointerFacts? Describe(HidInterface face)
    {
        ArgumentNullException.ThrowIfNull(face);

        IntPtr handle = HidNative.CreateFile(
            face.Path, 0, HidNative.FileShareReadWrite, IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);

        if (handle == HidNative.InvalidHandle) return null;

        try
        {
            if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;

            try
            {
                var caps = new HidNative.HidpCaps();
                if (HidNative.HidP_GetCaps(preparsed, ref caps) != HidNative.HidpStatusSuccess) return null;

                return new PointerFacts
                {
                    Face = face,
                    Link = LinkOf(face.Path),
                    Buttons = CountButtons(preparsed, caps.NumberInputButtonCaps),
                    Axes = ReadAxes(preparsed, caps.NumberInputValueCaps),
                };
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            HidNative.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Which kind of lead this is, from the device path.
    ///
    /// A dongle cannot be told from a cable here and should not be: to the operating
    /// system a receiver is a USB device with a mouse-shaped collection on it, which is
    /// exactly why a mouse on a dongle needs a vendor protocol before anything can be
    /// said about the mouse rather than about the receiver.
    /// </summary>
    private static string LinkOf(string path)
    {
        string lower = path.ToLowerInvariant();

        if (lower.Contains("bthle", StringComparison.Ordinal)
            || lower.Contains("{00001812", StringComparison.Ordinal))
        {
            return "Bluetooth LE";
        }

        return lower.Contains("bthenum", StringComparison.Ordinal) ? "Bluetooth" : "USB";
    }

    private static int CountButtons(IntPtr preparsed, ushort declared)
    {
        if (declared == 0) return 0;

        var caps = new HidNative.HidpButtonCaps[declared];
        ushort length = declared;

        if (HidNative.HidP_GetButtonCaps(HidNative.HidpInput, caps, ref length, preparsed)
            != HidNative.HidpStatusSuccess)
        {
            return 0;
        }

        int buttons = 0;

        for (int i = 0; i < length; i++)
        {
            if (caps[i].UsagePage != ButtonPage) continue;

            buttons += caps[i].IsRange != 0 ? caps[i].UsageMax - caps[i].UsageMin + 1 : 1;
        }

        return buttons;
    }

    private static List<PointerAxis> ReadAxes(IntPtr preparsed, ushort declared)
    {
        var axes = new List<PointerAxis>();
        if (declared == 0) return axes;

        var caps = new HidNative.HidpValueCaps[declared];
        ushort length = declared;

        if (HidNative.HidP_GetValueCaps(HidNative.HidpInput, caps, ref length, preparsed)
            != HidNative.HidpStatusSuccess)
        {
            return axes;
        }

        for (int i = 0; i < length; i++)
        {
            HidNative.HidpValueCaps cap = caps[i];

            // The range and single-usage forms are a union in the native structure, and
            // the single usage sits where the range's lower bound does — so one field
            // reads both, and a value cap that is a range is a range of one axis anyway.
            ushort usage = cap.UsageMin;

            axes.Add(new PointerAxis(
                cap.UsagePage,
                usage,
                NameOf(cap.UsagePage, usage),
                cap.BitSize,
                cap.LogicalMin,
                cap.LogicalMax));
        }

        return axes;
    }

    /// <summary>The handful of usages a pointer actually declares, named.</summary>
    private static string NameOf(ushort page, ushort usage) => (page, usage) switch
    {
        (GenericDesktop, 0x30) => "X",
        (GenericDesktop, 0x31) => "Y",
        (GenericDesktop, 0x38) => "Wheel",
        (GenericDesktop, 0x48) => "Resolution multiplier",
        (0x0C, 0x0238) => "Horizontal wheel",
        (0x0C, _) => $"consumer 0x{usage:X2}",
        _ => $"page 0x{page:X2} usage 0x{usage:X2}",
    };
}
