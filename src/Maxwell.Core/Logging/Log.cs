using Maxwell.Race.Abstractions;
using Microsoft.Extensions.Logging;

namespace Maxwell.Core.Logging;

/// <summary>
/// Source-generated log messages. The generator builds the delegates and the
/// argument checks at compile time, so a disabled log level costs nothing at runtime
/// and nothing is boxed on the hot polling path.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Opened {Transport} at {Path}")]
    public static partial void OpenedDevice(this ILogger logger, MaxwellTransport transport, string path);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Could not open {Path}")]
    public static partial void CouldNotOpenDevice(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Battery read failed; dropping the session")]
    public static partial void BatteryReadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Full refresh failed; dropping the session")]
    public static partial void FullRefreshFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Write failed; dropping the session")]
    public static partial void WriteFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Device polling started")]
    public static partial void PollingStarted(this ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Device polling stopped")]
    public static partial void PollingStopped(this ILogger logger);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Polling iteration failed")]
    public static partial void PollingIterationFailed(this ILogger logger, Exception exception);
}
