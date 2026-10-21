using Kvazr.Core.Settings;
using Xunit;

namespace Kvazr.Core.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", HotkeyModifiers.Control | HotkeyModifiers.Alt, "N")]
    [InlineData("Ctrl+Alt+Up", HotkeyModifiers.Control | HotkeyModifiers.Alt, "Up")]
    [InlineData("Win+Shift+F5", HotkeyModifiers.Win | HotkeyModifiers.Shift, "F5")]
    [InlineData("Control+Space", HotkeyModifiers.Control, "Space")]
    public void ValidGesture_Parses(string gesture, HotkeyModifiers expected, string key)
    {
        Assert.True(HotkeyGesture.TryParse(gesture, out HotkeyModifiers modifiers, out string parsed));
        Assert.Equal(expected, modifiers);
        Assert.Equal(key, parsed);
    }

    [Fact]
    public void Spelling_IsCaseAndSpaceInsensitive()
    {
        Assert.True(HotkeyGesture.TryParse("  ctrl + ALT + n ", out HotkeyModifiers modifiers, out string key));
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, modifiers);
        Assert.Equal("n", key);
    }

    /// <summary>
    /// Windows will not register a global shortcut without a real modifier, and binding
    /// a bare key would swallow it everywhere else on the system.
    /// </summary>
    [Theory]
    [InlineData("N")]
    [InlineData("Shift+N")]
    [InlineData("F5")]
    public void GestureWithoutRealModifier_IsRejected(string gesture)
    {
        Assert.False(HotkeyGesture.TryParse(gesture, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    public void EmptyOrModifierOnlyGesture_IsRejected(string? gesture)
    {
        Assert.False(HotkeyGesture.TryParse(gesture, out _, out _));
    }

    /// <summary>Two non-modifier tokens is a typo, not a chord.</summary>
    [Fact]
    public void TwoKeys_AreRejected()
    {
        Assert.False(HotkeyGesture.TryParse("Ctrl+A+B", out _, out _));
    }

    [Fact]
    public void Format_ProducesCanonicalOrder()
    {
        string formatted = HotkeyGesture.Format(
            HotkeyModifiers.Win | HotkeyModifiers.Alt | HotkeyModifiers.Control, "N");

        Assert.Equal("Ctrl+Alt+Win+N", formatted);
    }

    /// <summary>Whatever spelling the user typed, storing and re-reading must be stable.</summary>
    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        Assert.True(HotkeyGesture.TryParse("alt+ctrl+DOWN", out HotkeyModifiers modifiers, out string key));

        string canonical = HotkeyGesture.Format(modifiers, key);

        Assert.True(HotkeyGesture.TryParse(canonical, out HotkeyModifiers again, out string keyAgain));
        Assert.Equal(modifiers, again);
        Assert.Equal(key, keyAgain);
    }

    /// <summary>Every shipped default has to be registrable, or the app starts half-bound.</summary>
    [Fact]
    public void DefaultBindings_AreAllValid()
    {
        var settings = new AppSettings();

        foreach ((string action, string gesture) in settings.Hotkeys)
        {
            Assert.True(HotkeyGesture.TryParse(gesture, out _, out _),
                $"default binding for {action} is not a valid gesture: {gesture}");
        }
    }
}
