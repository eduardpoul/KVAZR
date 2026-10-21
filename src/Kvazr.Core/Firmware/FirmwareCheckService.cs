using Kvazr.Core.Abstractions;
using Kvazr.Core.Logging;
using Kvazr.Core.Services;
using Kvazr.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kvazr.Core.Firmware;

/// <summary>
/// Runs the firmware check in the background: shortly after launch, then once a day it
/// asks whether the weekly interval has elapsed.
///
/// The startup delay is not politeness. The check needs the installed version, which
/// comes from the headset, and the headset has not been read yet at the moment the host
/// starts — checking immediately would compare against nothing and conclude nothing.
/// </summary>
public sealed class FirmwareCheckService : BackgroundService
{
    /// <summary>Long enough for discovery and the first full refresh to have landed.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    /// <summary>How often to reconsider; the weekly interval itself lives in the service.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromHours(6);

    private readonly FirmwareUpdateService _updates;
    private readonly IDeviceService _device;
    private readonly INotifier _notifier;
    private readonly ISettingsStore _settings;
    private readonly ILogger<FirmwareCheckService> _logger;

    public FirmwareCheckService(
        FirmwareUpdateService updates,
        IDeviceService device,
        INotifier notifier,
        ISettingsStore settings,
        ILogger<FirmwareCheckService> logger)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _updates = updates;
        _device = device;
        _notifier = notifier;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(Tick, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down; nothing to report.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_updates.IsDue()) return;

        // Without a version to compare against, a check can only produce a false
        // negative. Leave the interval untouched and try again on the next tick.
        if (_device.State.FirmwareVersion is not { } installed) return;

        try
        {
            FirmwareCheck check = await _updates.CheckAsync(installed, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!_updates.ShouldAnnounce(check) || check.Latest is null) return;
            if (!_settings.Current.CheckFirmwareUpdates) return;

            // Matching DevicePollingService, notification text is English here rather
            // than localised: the catalogue lives in the app layer and the core does not
            // reference it. Worth moving both together rather than either alone.
            await _notifier.NotifyAsync(
                "Maxwell firmware update",
                $"Version {check.Latest.Version} is available; {check.Installed} is installed.",
                cancellationToken).ConfigureAwait(false);

            await _updates.MarkAnnouncedAsync(check, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.FirmwareCheckFailed(ex, "background");
        }
    }
}
