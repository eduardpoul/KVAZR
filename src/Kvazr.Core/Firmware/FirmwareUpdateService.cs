using Kvazr.Core.Logging;
using Kvazr.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Kvazr.Core.Firmware;

/// <summary>
/// Decides when to ask Audeze what the current firmware is, and remembers the answer.
///
/// The interval matters more than it looks. Firmware ships a couple of times a year,
/// so checking on every launch would be several hundred pointless requests per user
/// per year, and — since the check reaches out to a third party — several hundred
/// unnecessary disclosures that this machine is running the app.
/// </summary>
public sealed class FirmwareUpdateService : IDisposable
{
    /// <summary>Audeze published four firmwares in three years. Once a week is generous.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);

    private readonly IFirmwareCatalog _catalog;
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<FirmwareUpdateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FirmwareCheck Last { get; private set; } = FirmwareCheck.Never;

    public event EventHandler<FirmwareCheck>? Checked;

    public FirmwareUpdateService(
        IFirmwareCatalog catalog,
        ISettingsStore settings,
        TimeProvider clock,
        ILogger<FirmwareUpdateService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _settings = settings;
        _clock = clock;
        _logger = logger;

        Last = Restore();
    }

    /// <summary>
    /// Rebuild the last result from settings so the screen is populated at launch rather
    /// than blank until the weekly interval next comes round.
    ///
    /// The download URLs are not stored, only the versions — nothing currently needs
    /// them, and a stored URL would be the one piece of this that could go stale without
    /// anyone noticing. Reviving the downloader means forcing a fresh check first.
    /// </summary>
    private FirmwareCheck Restore()
    {
        if (_settings.Current.LastFirmwareCheck is not { } checkedAt) return FirmwareCheck.Never;

        FirmwareRelease? latest = ToRelease(_settings.Current.LatestFirmware);

        List<FirmwareRelease> rollbacks = _settings.Current.AvailableFirmware
            .Select(ToRelease)
            .OfType<FirmwareRelease>()
            .ToList();

        return new FirmwareCheck(null, latest, rollbacks, checkedAt);
    }

    private static FirmwareRelease? ToRelease(string? version) =>
        FirmwareVersion.Parse(version) is { } parsed
            ? new FirmwareRelease(parsed, null, null, null, null)
            : null;

    /// <summary>
    /// True when a background check is due: the feature is on, and either it has never
    /// run or the interval has elapsed. A clock that has moved backwards — a corrected
    /// system time, a restored VM — also counts as due rather than locking the check
    /// out until the stored date is reached again.
    /// </summary>
    public bool IsDue()
    {
        if (!_settings.Current.CheckFirmwareUpdates) return false;
        if (_settings.Current.LastFirmwareCheck is not { } last) return true;

        DateTimeOffset now = _clock.GetUtcNow();
        return now - last >= CheckInterval || now < last;
    }

    /// <summary>
    /// Run a check if one is due. <paramref name="force"/> is the user pressing the
    /// button, which ignores both the interval and the preference — an explicit request
    /// is not something to defer.
    /// </summary>
    public async Task<FirmwareCheck> CheckAsync(
        string? installedVersion, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && !IsDue()) return Last;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FirmwareRelease? latest = await _catalog.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<FirmwareRelease> rollbacks =
                await _catalog.GetRollbacksAsync(cancellationToken).ConfigureAwait(false);

            // A failed fetch must not be recorded as a successful check, or one flaky
            // week would push the next attempt out by another seven days.
            if (latest is null && rollbacks.Count == 0) return Last;

            var check = new FirmwareCheck(
                FirmwareVersion.Parse(installedVersion), latest, rollbacks, _clock.GetUtcNow());

            Last = check;

            _settings.Current.LastFirmwareCheck = check.CheckedAt;
            _settings.Current.LatestFirmware = latest?.Version.ToString();
            _settings.Current.AvailableFirmware =
                check.AllReleases.Select(r => r.Version.ToString()).ToList();
            await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.FirmwareChecked(
                    check.Installed?.ToString() ?? "unknown",
                    latest?.Version.ToString() ?? "unknown");
            }

            Checked?.Invoke(this, check);
            return check;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// True when this version has not been announced yet. The notice fires once per
    /// published version, not once per check — a weekly reminder of the same update is
    /// how a useful notice becomes noise the user mutes.
    /// </summary>
    public bool ShouldAnnounce(FirmwareCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!check.UpdateAvailable || check.Latest is null) return false;

        return !string.Equals(
            _settings.Current.AnnouncedFirmware,
            check.Latest.Version.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task MarkAnnouncedAsync(FirmwareCheck check, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Latest is null) return;

        _settings.Current.AnnouncedFirmware = check.Latest.Version.ToString();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _gate.Dispose();
}
