// Non-Windows targets only; on Windows the toast implementation is used instead.
#if !WINDOWS
using System.Diagnostics;
using System.Runtime.Versioning;
using Maxwell.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Maxwell.App.Platform.Linux;

/// <summary>
/// Desktop notifications through notify-send.
///
/// The obvious alternative — DesktopNotifications.FreeDesktop — pulls in Tmds.DBus
/// 0.9.1, which carries a known high-severity advisory (GHSA-xrw6-gwf8-vvr9). Forcing a
/// patched Tmds.DBus would mean jumping several major versions of a transitive
/// dependency belonging to a library that cannot be exercised from here, which would
/// look finished without being verifiable. notify-send talks to the same
/// org.freedesktop.Notifications service, ships with libnotify on every mainstream
/// desktop, and adds nothing to the dependency graph.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxNotifier : INotifier
{
    private readonly ILogger<LinuxNotifier> _logger;
    private bool _unavailable;

    public LinuxNotifier(ILogger<LinuxNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (_unavailable) return;

        var startInfo = new ProcessStartInfo("notify-send")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        // Passed as separate arguments rather than a command line, so a device name or
        // message can never be interpreted as shell syntax.
        startInfo.ArgumentList.Add("--app-name=Maxwell.App");
        startInfo.ArgumentList.Add("--expire-time=8000");
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add(message);

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null) return;

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // libnotify is not installed. That will not change while we run, so stop
            // trying rather than spawning a doomed process on every battery poll.
            _logger.NotificationFailed(ex);
            _unavailable = true;
        }
    }
}
#endif
