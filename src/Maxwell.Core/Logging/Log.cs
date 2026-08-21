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

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Settings at {Path} could not be read; defaults are in use")]
    public static partial void SettingsUnreadable(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Settings could not be written to {Path}")]
    public static partial void SettingsNotSaved(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Firmware manifest {Address} returned HTTP {Status}")]
    public static partial void FirmwareManifestFailed(this ILogger logger, string address, int status);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Warning, Message = "Firmware check against {Address} did not complete")]
    public static partial void FirmwareCheckFailed(this ILogger logger, Exception exception, string address);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "Firmware check: installed {Installed}, published {Latest}")]
    public static partial void FirmwareChecked(this ILogger logger, string installed, string latest);

    // Used only by the parked downloader; kept so re-enabling it is one flag.
    [LoggerMessage(EventId = 1403, Level = LogLevel.Warning,
        Message = "Firmware image at {Address} rejected: {Bytes} bytes is not a plausible size")]
    public static partial void FirmwareImageRejected(this ILogger logger, string address, long bytes);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Information,
        Message = "Firmware {Version} downloaded to {Directory} ({Bytes} bytes)")]
    public static partial void FirmwareDownloaded(this ILogger logger, string version, string directory, long bytes);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information,
        Message = "{DeviceKey} is present but does not answer; asking again on the next sweep")]
    public static partial void DeviceHasNoControlChannel(this ILogger logger, Exception exception, string deviceKey);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "{DeviceKey} read failed; dropping the channel")]
    public static partial void DeviceReadFailed(this ILogger logger, Exception exception, string deviceKey);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "{DeviceKey} write to {CapabilityId} failed")]
    public static partial void DeviceWriteFailed(
        this ILogger logger, Exception exception, string deviceKey, string capabilityId);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "{DeviceKey} appeared, driver {Driver}")]
    public static partial void DeviceAppeared(this ILogger logger, string deviceKey, string driver);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Information, Message = "{DeviceKey} disappeared")]
    public static partial void DeviceDisappeared(this ILogger logger, string deviceKey);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "Discovery failed for driver {Driver}")]
    public static partial void DiscoveryFailed(this ILogger logger, Exception exception, string driver);

    [LoggerMessage(
        EventId = 1306,
        Level = LogLevel.Debug,
        Message = "{DeviceKey} from {Driver} shadowed by a driver that knows it better")]
    public static partial void DeviceShadowed(this ILogger logger, string deviceKey, string driver);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Device polling started")]
    public static partial void PollingStarted(this ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Device polling stopped")]
    public static partial void PollingStopped(this ILogger logger);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Polling iteration failed")]
    public static partial void PollingIterationFailed(this ILogger logger, Exception exception);
}
