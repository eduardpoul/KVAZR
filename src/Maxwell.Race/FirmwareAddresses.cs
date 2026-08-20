using Maxwell.Race.Abstractions;

namespace Maxwell.Race;

/// <summary>
/// Flash addresses of the firmware version string, per variant.
/// Readable at runtime via RACE cmd 0x1680.
/// </summary>
public static class FirmwareAddresses
{
    /// <summary>Xbox headset, newest first: v1.0.1.74, then .63/.61.</summary>
    public static readonly uint[] XboxHeadset = { 0x0828EA20, 0x0828E990 };

    /// <summary>PlayStation headset, newest first: v1.0.1.74, then .63.</summary>
    public static readonly uint[] PlayStationHeadset = { 0x0828E5C8, 0x0828E538 };

    /// <summary>Xbox dongle. Stable across .61/.63/.74.</summary>
    public const uint XboxDongle = 0x081EEDD8;

    /// <summary>PlayStation dongle. Stable across .61/.63/.74.</summary>
    public const uint PlayStationDongle = 0x081E78F8;

    public static uint[] For(MaxwellTransport transport) => transport switch
    {
        MaxwellTransport.XboxDongle => new[] { XboxDongle },
        MaxwellTransport.PlayStationDongle => new[] { PlayStationDongle },
        MaxwellTransport.PlayStationUsbC => PlayStationHeadset,
        _ => XboxHeadset,
    };
}
