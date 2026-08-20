namespace Maxwell.App.Platform;

/// <summary>A global shortcut and what it triggers.</summary>
public sealed record HotkeyBinding(string ActionId, string Gesture);

/// <summary>Registers system-wide shortcuts that work while the window is hidden.</summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler<string>? Triggered;

    /// <summary>Replaces every registration. Returns the actions that could not be bound.</summary>
    IReadOnlyList<string> Apply(IReadOnlyList<HotkeyBinding> bindings);
}
