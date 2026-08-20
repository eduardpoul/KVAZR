using Maxwell.Core.Abstractions;
using Maxwell.Core.Model;
using Maxwell.Core.Notifications;
using Microsoft.Extensions.Hosting;
using Maxwell.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maxwell.Core.Services;

public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    /// <summary>
    /// How often the battery is read. This is a battery-powered device on a relayed
    /// link — polling harder buys nothing and costs power.
    /// </summary>
    public TimeSpan BatteryInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to look for hardware while nothing is connected.</summary>
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<int> BatteryThresholds { get; set; } = [30, 15, 5];

    public bool NotifyOnLowBattery { get; set; } = true;

    public bool NotifyOnChargeComplete { get; set; } = true;
}

/// <summary>
/// Keeps the device state warm and raises battery alerts. Deliberately the only
/// timer in the application: everything else reads from <see cref="IDeviceService"/>.
/// </summary>
public sealed class DevicePollingService : BackgroundService
{
    private readonly IDeviceService _device;
    private readonly INotifier _notifier;
    private readonly IOptionsMonitor<PollingOptions> _options;
    private readonly ILogger<DevicePollingService> _logger;

    private BatteryWatchState _watch = BatteryWatchState.Initial;

    public DevicePollingService(
        IDeviceService device,
        INotifier notifier,
        IOptionsMonitor<PollingOptions> options,
        ILogger<DevicePollingService> logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _device = device;
        _notifier = notifier;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.PollingStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            PollingOptions options = _options.CurrentValue;

            try
            {
                await _device.DiscoverAsync(stoppingToken).ConfigureAwait(false);

                if (_device.State.CanControl)
                {
                    await _device.RefreshBatteryAsync(stoppingToken).ConfigureAwait(false);
                    await EvaluateBatteryAsync(options, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // A polling loop must survive any single failure.
            catch (Exception ex)
            {
                _logger.PollingIterationFailed(ex);
            }
#pragma warning restore CA1031

            TimeSpan delay = _device.State.CanControl ? options.BatteryInterval : options.DiscoveryInterval;

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.PollingStopped();
    }

    private async Task EvaluateBatteryAsync(PollingOptions options, CancellationToken cancellationToken)
    {
        DeviceState state = _device.State;

        (BatteryWatchState next, BatteryDecision decision) = BatteryNotifications.Evaluate(
            _watch, state.BatteryPercent, state.IsCharging, options.BatteryThresholds);

        _watch = next;

        if (!decision.ShouldNotify) return;

        switch (decision.Alert)
        {
            case BatteryAlert.LowBattery when options.NotifyOnLowBattery:
                await _notifier.NotifyAsync(
                    "Maxwell battery low",
                    $"{state.BatteryPercent}% remaining.",
                    cancellationToken).ConfigureAwait(false);
                break;

            case BatteryAlert.ChargeComplete when options.NotifyOnChargeComplete:
                await _notifier.NotifyAsync(
                    "Maxwell charged",
                    "The headset is fully charged.",
                    cancellationToken).ConfigureAwait(false);
                break;

            case BatteryAlert.None:
            default:
                break;
        }
    }
}
