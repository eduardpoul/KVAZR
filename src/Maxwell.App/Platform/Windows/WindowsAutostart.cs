// Windows-only: the registry only exists on Windows.
#if WINDOWS
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Maxwell.Core.Abstractions;
using Maxwell.App.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Maxwell.App.Platform.Windows;

/// <summary>
/// Registers the app in the per-user Run key. Deliberately HKCU rather than a
/// scheduled task or an HKLM entry: neither needs elevation to be undone, and a tray
/// utility has no business writing machine-wide state.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostart : IAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Not renamed with the rest of the product. This is the key the previous version
    // wrote, and writing a new one would leave the old entry behind: two lines in the
    // Startup tab, one of them launching a path that may no longer exist. The name is
    // not shown to the user — Task Manager lists the executable's own description.
    private const string ValueName = "Maxwell.App";
    private const string MinimisedArgument = "--minimised";

    private readonly ILogger<WindowsAutostart> _logger;

    public WindowsAutostart(ILogger<WindowsAutostart> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public bool IsEnabled => ReadCommand() is not null;

    public bool StartsMinimised =>
        ReadCommand()?.Contains(MinimisedArgument, StringComparison.OrdinalIgnoreCase) ?? false;

    public void Enable(bool startMinimised)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

            string executable = ExecutablePath();
            string command = startMinimised
                ? string.Create(CultureInfo.InvariantCulture, $"\"{executable}\" {MinimisedArgument}")
                : string.Create(CultureInfo.InvariantCulture, $"\"{executable}\"");

            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.AutostartFailed(ex);
        }
    }

    public void Disable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.AutostartFailed(ex);
        }
    }

    private static string? ReadCommand()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>
    /// The real executable, not the managed dll. A single-file or apphost launch
    /// reports the dll from Assembly.Location, which would register an entry Windows
    /// cannot run.
    /// </summary>
    private static string ExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
}
#endif
