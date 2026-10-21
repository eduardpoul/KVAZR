using Kvazr.Core.Drivers.Logitech;
using Xunit;

namespace Kvazr.Core.Tests;

/// <summary>
/// Putting a mouse on one of its stored resolutions without losing another.
///
/// A Logitech keeps five and cycles them with a button of its own. Which of the five is
/// in force is the mouse's own business — nothing found in its memory or its protocol
/// sets it — so the only way to change what the mouse is doing is to change the rung it
/// happens to be standing on.
///
/// Done naively that costs a resolution. Standing on 800 and asked for 1600, writing 1600
/// over the 800 leaves 1600/1200/1600/2400/3200: two of one, none of the other, and the
/// mouse's own button now walks that. It happened to somebody, on their own mouse, and
/// this is the arithmetic that stops it happening again — separately from any device,
/// because a write like that succeeds either way and the device never says a word.
/// </summary>
public sealed class PointerLadderTests
{
    /// <summary>Five entries of five bytes from offset four: across, down, lift-off.</summary>
    private static byte[] Profile(params int[] rungs)
    {
        byte[] sector = new byte[255];

        for (int slot = 0; slot < rungs.Length && slot < 5; slot++)
        {
            int at = 4 + (slot * 5);

            sector[at] = (byte)(rungs[slot] & 0xFF);
            sector[at + 1] = (byte)(rungs[slot] >> 8);
            sector[at + 2] = sector[at];
            sector[at + 3] = sector[at + 1];
            sector[at + 4] = 2;
        }

        return sector;
    }

    private static int[] Rungs(byte[] profile) =>
        [.. Enumerable.Range(0, 5).Select(slot =>
        {
            int at = 4 + (slot * 5);
            return profile[at] | (profile[at + 1] << 8);
        })];

    /// <summary>The same, read off the second pair of bytes, which is the other axis.</summary>
    private static int[] Down(byte[] profile) =>
        [.. Enumerable.Range(0, 5).Select(slot =>
        {
            int at = 4 + (slot * 5);
            return profile[at + 2] | (profile[at + 3] << 8);
        })];

    [Fact]
    public void MovingToAStoredStep_TradesPlacesWithIt()
    {
        byte[] profile = Profile(800, 1200, 1600, 2400, 3200);

        Assert.True(PointerWriter.Trade(profile, current: 800, wanted: 1600));

        // The mouse is on the first rung, so the first rung has to carry 1600 — and the
        // rung that held 1600 takes the 800 rather than letting it go.
        Assert.Equal([1600, 1200, 800, 2400, 3200], Rungs(profile));
    }

    [Fact]
    public void NothingIsLost_WhateverIsAskedFor()
    {
        foreach (int wanted in (int[])[1200, 1600, 2400, 3200])
        {
            byte[] profile = Profile(800, 1200, 1600, 2400, 3200);

            Assert.True(PointerWriter.Trade(profile, current: 800, wanted));

            Assert.Equal(wanted, Rungs(profile)[0]);
            Assert.Equal([800, 1200, 1600, 2400, 3200], Rungs(profile).Order());
        }
    }

    /// <summary>
    /// A value the ladder does not have — which the row cannot offer, since its options
    /// are the ladder, but the writer is not entitled to assume that.
    /// </summary>
    [Fact]
    public void AValueNotOnTheLadder_IsSimplyWritten()
    {
        byte[] profile = Profile(800, 1200, 1600, 2400, 3200);

        Assert.True(PointerWriter.Trade(profile, current: 800, wanted: 5000));

        Assert.Equal([5000, 1200, 1600, 2400, 3200], Rungs(profile));
    }

    [Fact]
    public void BothAxesMove_Together()
    {
        byte[] profile = Profile(800, 1200, 1600, 2400, 3200);

        PointerWriter.Trade(profile, current: 800, wanted: 2400);

        // Written apart once, and the mouse was fast up the screen and slow along it.
        Assert.Equal(Rungs(profile), Down(profile));
    }

    [Fact]
    public void AMouseStandingOnSomethingTheLadderDoesNotHave_IsLeftAlone()
    {
        byte[] profile = Profile(800, 1200, 1600, 2400, 3200);

        Assert.False(PointerWriter.Trade(profile, current: 999, wanted: 1600));
        Assert.Equal([800, 1200, 1600, 2400, 3200], Rungs(profile));
    }

    /// <summary>
    /// A ladder that already carries the same value twice — which is what the old write
    /// left behind, so a mouse can arrive here in that state.
    /// </summary>
    [Fact]
    public void ADuplicatedLadder_TradesWithTheOtherCopy()
    {
        byte[] profile = Profile(1600, 1200, 1600, 2400, 3200);

        Assert.True(PointerWriter.Trade(profile, current: 1600, wanted: 1200));

        Assert.Equal([1200, 1600, 1600, 2400, 3200], Rungs(profile));
    }
}
