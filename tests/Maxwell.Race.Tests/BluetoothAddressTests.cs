using Maxwell.Race.Windows;
using Xunit;

namespace Maxwell.Race.Tests;

/// <summary>
/// The address parsed here decides which device gets unpaired, and unpairing is the one
/// thing this application does that nobody can undo from inside it. Windows reports the
/// same address in several shapes depending on which property it came from, so every
/// shape it has actually been seen in is pinned.
/// </summary>
public sealed class BluetoothAddressTests
{
    [Theory]
    [InlineData("6C:29:95:1B:AE:07")]
    [InlineData("6c:29:95:1b:ae:07")]
    [InlineData("6c2995 1bae07")]
    [InlineData("6C299 51BAE07")]
    [InlineData("6c-29-95-1b-ae-07")]
    [InlineData("6C29951BAE07")]
    public void EveryShapeWindowsUses_ParsesToTheSameAddress(string reported)
    {
        Assert.Equal(0x6C2995_1BAE07UL, WindowsBluetoothPairing.Parse(reported));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("6C:29:95:1B:AE")]
    [InlineData("6C:29:95:1B:AE:07:11")]
    [InlineData("not an address")]
    public void AnythingElseIsRefused(string? reported)
    {
        // Refusing is the whole safety property. A half-parsed address is a different
        // device, and there is no way back from unpairing the wrong one.
        Assert.Null(WindowsBluetoothPairing.Parse(reported));
    }
}
