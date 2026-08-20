namespace Maxwell.Race.Abstractions;

/// <summary>Which physical link a Maxwell HID interface represents.</summary>
public enum MaxwellTransport
{
    Unknown,
    XboxDongle,
    PlayStationDongle,
    PlayStationUsbC,
    XboxUsbC,
}

/// <summary>
/// One enumerated Maxwell HID interface. Platform-neutral: <see cref="Path"/> is
/// whatever the host needs to reopen the device — a Win32 device path, a
/// /dev/hidraw node, or an IOKit service identifier.
/// </summary>
public sealed record MaxwellInterface(
    string Path,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    string ProductName)
{
    public const ushort VendorId = 0x3329;

    /// <summary>Vendor-defined usage page carrying RACE.</summary>
    public const ushort RaceUsagePage = 0xFF13;

    public static readonly ushort[] KnownProductIds = [0x4B18, 0x4B19, 0x4B1A, 0x4B1E];

    public MaxwellTransport Transport => ProductId switch
    {
        0x4B18 => MaxwellTransport.XboxDongle,
        0x4B19 => MaxwellTransport.PlayStationDongle,
        0x4B1A => MaxwellTransport.PlayStationUsbC,
        0x4B1E => MaxwellTransport.XboxUsbC,
        _ => MaxwellTransport.Unknown,
    };

    public bool IsDongle => ProductId is 0x4B18 or 0x4B19;

    /// <summary>
    /// True for the vendor-defined RACE collection. On some units the usage page
    /// reads back differently, so the col02 path is accepted as a fallback.
    /// </summary>
    public bool IsRaceInterface =>
        UsagePage == RaceUsagePage ||
        Path.Contains("col02", StringComparison.OrdinalIgnoreCase);
}
