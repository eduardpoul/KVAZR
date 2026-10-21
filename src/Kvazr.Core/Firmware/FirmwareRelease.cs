using Kvazr.Race.Abstractions;

namespace Kvazr.Core.Firmware;

/// <summary>
/// One published firmware, with the four binaries Audeze ships per release: a headset
/// image and a dongle image, each in an Xbox and a PlayStation flavour.
///
/// Which pair applies is decided by the hardware in front of us, not by the user. The
/// wrong flavour is exactly the sort of thing that turns a headset into a paperweight.
/// </summary>
public sealed record FirmwareRelease(
    FirmwareVersion Version,
    Uri? XboxHeadset,
    Uri? XboxDongle,
    Uri? PlayStationHeadset,
    Uri? PlayStationDongle)
{
    /// <summary>The headset image matching the connected hardware, or null when unknown.</summary>
    public Uri? HeadsetImageFor(MaxwellTransport transport) => transport switch
    {
        MaxwellTransport.XboxDongle or MaxwellTransport.XboxUsbC => XboxHeadset,
        MaxwellTransport.PlayStationDongle or MaxwellTransport.PlayStationUsbC => PlayStationHeadset,
        _ => null,
    };

    /// <summary>The dongle image matching the connected hardware, or null when unknown.</summary>
    public Uri? DongleImageFor(MaxwellTransport transport) => transport switch
    {
        MaxwellTransport.XboxDongle or MaxwellTransport.XboxUsbC => XboxDongle,
        MaxwellTransport.PlayStationDongle or MaxwellTransport.PlayStationUsbC => PlayStationDongle,
        _ => null,
    };

    /// <summary>True when both images this hardware needs are present in the manifest.</summary>
    public bool IsCompleteFor(MaxwellTransport transport) =>
        HeadsetImageFor(transport) is not null && DongleImageFor(transport) is not null;
}

/// <summary>What a check against Audeze's manifests turned up.</summary>
public sealed record FirmwareCheck(
    FirmwareVersion? Installed,
    FirmwareRelease? Latest,
    IReadOnlyList<FirmwareRelease> Rollbacks,
    DateTimeOffset CheckedAt)
{
    public static readonly FirmwareCheck Never = new(null, null, [], default);

    /// <summary>
    /// True only when the published version is genuinely newer than what is installed.
    /// An unknown installed version is not treated as "needs updating": the headset may
    /// simply not have been read yet, and nagging on missing data is how users learn to
    /// ignore the notice.
    /// </summary>
    public bool UpdateAvailable =>
        Installed is { } installed && Latest is { } latest && latest.Version > installed;

    /// <summary>
    /// The same findings judged against a different installed version. Restored checks
    /// arrive before the headset has been read, so the comparison has to be redone once
    /// the device answers.
    /// </summary>
    public FirmwareCheck WithInstalled(string? installedVersion) =>
        this with { Installed = FirmwareVersion.Parse(installedVersion) };

    /// <summary>Every version on offer, newest first, including the current release.</summary>
    public IReadOnlyList<FirmwareRelease> AllReleases =>
        (Latest is null ? Rollbacks : Rollbacks.Append(Latest))
        .DistinctBy(r => r.Version)
        .OrderByDescending(r => r.Version)
        .ToList();
}
