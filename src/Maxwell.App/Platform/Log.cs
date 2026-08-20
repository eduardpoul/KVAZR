using Microsoft.Extensions.Logging;

namespace Maxwell.App.Platform;

/// <summary>
/// Source-generated log messages for the platform integration layer. Separate from the
/// core's own set so neither project has to widen its internals for the other.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning,
        Message = "Hotkey {Gesture} could not be registered (Win32 {Error})")]
    public static partial void HotkeyRejected(this ILogger logger, string gesture, int error);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Notification could not be shown")]
    public static partial void NotificationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Autostart registration failed")]
    public static partial void AutostartFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error,
        Message = "Hotkey message window could not be created (Win32 {Error}); global shortcuts are unavailable")]
    public static partial void HotkeyWindowFailed(this ILogger logger, int error);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "Hotkey {Gesture} registered as id {Id}")]
    public static partial void HotkeyRegistered(this ILogger logger, string gesture, int id);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information, Message = "Notification shown: {Title}")]
    public static partial void NotificationShown(this ILogger logger, string title);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information,
        Message = "Tray icon created: visible={Visible}, icon={HasIcon}, registered={Registered}")]
    public static partial void TrayCreated(this ILogger logger, bool visible, bool hasIcon, int registered);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Error, Message = "Tray icon could not be created")]
    public static partial void TrayFailed(this ILogger logger, Exception exception);
}
