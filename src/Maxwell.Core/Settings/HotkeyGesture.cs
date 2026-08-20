namespace Maxwell.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Parses gestures like "Ctrl+Alt+Up" into modifiers and a key token. Platform-neutral
/// on purpose: mapping the token to a virtual key code is the host's job, so this half
/// — where the edge cases live — can be tested without an OS.
/// </summary>
public static class HotkeyGesture
{
    public static bool TryParse(string? gesture, out HotkeyModifiers modifiers, out string key)
    {
        modifiers = HotkeyModifiers.None;
        key = "";

        if (string.IsNullOrWhiteSpace(gesture)) return false;

        foreach (string part in gesture.Split('+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Control; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN" or "META": modifiers |= HotkeyModifiers.Win; break;

                default:
                    // Two non-modifier tokens is not a gesture, it is a typo.
                    if (key.Length > 0) return false;
                    key = part;
                    break;
            }
        }

        if (key.Length == 0) return false;

        // Windows refuses to register a global shortcut without a real modifier, and
        // Shift alone is not one — binding a bare key would swallow it system-wide.
        return modifiers is not (HotkeyModifiers.None or HotkeyModifiers.Shift);
    }

    /// <summary>Canonical spelling, so stored settings do not depend on how the user typed it.</summary>
    public static string Format(HotkeyModifiers modifiers, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");

        parts.Add(key);
        return string.Join('+', parts);
    }
}
