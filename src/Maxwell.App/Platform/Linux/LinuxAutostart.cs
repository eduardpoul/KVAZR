using System.Globalization;
using System.Runtime.Versioning;
using Maxwell.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Maxwell.App.Platform.Linux;

/// <summary>
/// Autostart on the freedesktop convention: a .desktop file under
/// ~/.config/autostart. Every mainstream desktop environment honours it, and removing
/// the file is all it takes to undo — no daemon, no elevated write.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxAutostart : IAutostart
{
    private const string FileName = "maxwell-app.desktop";
    private const string MinimisedArgument = "--minimised";

    private readonly ILogger<LinuxAutostart> _logger;

    public LinuxAutostart(ILogger<LinuxAutostart> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart");

    private static string FilePath => Path.Combine(DirectoryPath, FileName);

    public bool IsEnabled => File.Exists(FilePath);

    public bool StartsMinimised
    {
        get
        {
            try
            {
                return File.Exists(FilePath)
                    && File.ReadAllText(FilePath).Contains(MinimisedArgument, StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    public void Enable(bool startMinimised)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            string executable = Environment.ProcessPath ?? "";
            string command = startMinimised
                ? string.Create(CultureInfo.InvariantCulture, $"{executable} {MinimisedArgument}")
                : executable;

            string entry = string.Create(CultureInfo.InvariantCulture, $"""
                [Desktop Entry]
                Type=Application
                Name=Maxwell.App
                Comment=Audeze Maxwell companion
                Exec={command}
                Terminal=false
                X-GNOME-Autostart-enabled=true

                """);

            File.WriteAllText(FilePath, entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.AutostartFailed(ex);
        }
    }

    public void Disable()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.AutostartFailed(ex);
        }
    }
}
