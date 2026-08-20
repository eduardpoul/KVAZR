using System.Runtime.Versioning;
using DesktopNotifications;
using DesktopNotifications.Windows;
using Maxwell.Core.Abstractions;
using Maxwell.App.Platform;
using Microsoft.Extensions.Logging;

namespace Maxwell.App.Platform.Windows;

/// <summary>
/// Windows toast notifications for an unpackaged app.
///
/// Initialisation is deferred to the first notification rather than done at startup:
/// registering the toast identity touches the Start Menu, and a user who never hits a
/// low battery should not pay for that on every launch.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNotifier : INotifier, IDisposable
{
    private readonly ILogger<WindowsNotifier> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WindowsNotificationManager? _manager;
    private bool _initialisationFailed;
    private bool _disposed;

    public WindowsNotifier(ILogger<WindowsNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (_disposed || _initialisationFailed) return;

        try
        {
            WindowsNotificationManager manager =
                await EnsureManagerAsync(cancellationToken).ConfigureAwait(false);

            // The second argument is the expiry; null lets the shell decide, which is
            // what a battery warning wants.
            await manager.ShowNotification(
                new Notification { Title = title, Body = message },
                expirationTime: null).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed toast must never take down the polling loop.
        catch (Exception ex)
        {
            _logger.NotificationFailed(ex);

            // One failure here is almost always a permanent environment problem
            // (notifications disabled, no toast identity), so stop retrying every poll.
            _initialisationFailed = true;
        }
#pragma warning restore CA1031
    }

    private async Task<WindowsNotificationManager> EnsureManagerAsync(CancellationToken cancellationToken)
    {
        if (_manager is not null) return _manager;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_manager is not null) return _manager;

            var context = WindowsApplicationContext.FromCurrentProcess("Maxwell.App");
            var manager = new WindowsNotificationManager(context);
            await manager.Initialize().ConfigureAwait(false);

            _manager = manager;
            return manager;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _manager?.Dispose();
        _manager = null;
        _gate.Dispose();
    }
}
