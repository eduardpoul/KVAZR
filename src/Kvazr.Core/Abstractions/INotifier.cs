namespace Kvazr.Core.Abstractions;

/// <summary>
/// Sends a desktop notification. Implemented per platform: AppNotificationBuilder on
/// Windows, org.freedesktop.Notifications over D-Bus on Linux,
/// UNUserNotificationCenter on macOS. No framework abstracts this for us.
/// </summary>
public interface INotifier
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}

/// <summary>Controls whether the app starts with the session, and whether it starts hidden.</summary>
public interface IAutostart
{
    bool IsEnabled { get; }

    /// <summary>True when the app is configured to start minimised to the tray.</summary>
    bool StartsMinimised { get; }

    void Enable(bool startMinimised);

    void Disable();
}

/// <summary>A notifier that does nothing. Keeps the core testable and headless runs quiet.</summary>
public sealed class NullNotifier : INotifier
{
    public static readonly NullNotifier Instance = new();

    public Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
