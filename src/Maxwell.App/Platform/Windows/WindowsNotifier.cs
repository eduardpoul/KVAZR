// Windows-only: the toast package only ships for the Windows target.
#if WINDOWS
using System.Runtime.Versioning;
using Avalonia.Threading;
using DesktopNotifications;
using DesktopNotifications.Windows;
using Maxwell.Core.Abstractions;
using Maxwell.App.Platform;
using Microsoft.Extensions.Logging;

namespace Maxwell.App.Platform.Windows;

/// <summary>
/// Windows toast notifications for an unpackaged app.
///
/// Everything runs on the UI thread. The toast objects are COM, and they must live in a
/// long-lived STA apartment: created on a short-lived STA thread instead, they survive
/// initialisation and then fail on Show with CO_E_OBJNOTCONNECTED once that thread has
/// exited and torn its apartment down. The UI thread is STA and lives as long as the app.
///
/// Initialisation is still deferred to the first notification: registering the toast
/// identity writes a Start Menu shortcut, and a user who never hits a low battery should
/// not pay for that on every launch.
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

            // Shown from the same apartment the notifier was created in; the expiry is
            // left to the shell, which is what a battery warning wants.
            await Dispatcher.UIThread.InvokeAsync(() => manager.ShowNotification(
                new Notification { Title = title, Body = message },
                expirationTime: null)).ConfigureAwait(false);

            // Logged on success too: without this, "it worked" and "it was never
            // called" look identical in the log, which is how a silent NullNotifier
            // passed for a working one.
            _logger.NotificationShown(title);
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

            WindowsNotificationManager manager = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // The name on the toast and in the Action Center, so it is the product
                // name and not the assembly's. Changing it re-registers the AUMID under
                // the new display name; nothing that was already delivered is lost.
                var context = WindowsApplicationContext.FromCurrentProcess("KVAZR");
                return new WindowsNotificationManager(context);
            }).GetTask().ConfigureAwait(false);

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
#endif
