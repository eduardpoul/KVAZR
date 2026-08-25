using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.HyperX;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The two light bars, and the arithmetic that decides what they show.
///
/// Every effect is computed on this side because the microphone has no notion of them:
/// what it accepts is one frame, two colours wide, and it takes the lights back about a
/// second after the last one. So an effect here is a function of time and a few settings,
/// and a function of time is the one part of a driver that can be checked exactly.
/// </summary>
public sealed class HyperXLightingTests
{
    private const int Red = 0xFF0000;
    private const int Blue = 0x0000FF;

    private static LightingState Lit(LightingEffect effect, int upper = Red, int lower = Blue) =>
        LightingState.Default with { On = true, Effect = effect, Upper = upper, Lower = lower };

    [Fact]
    public void Static_ShowsEachBarItsOwnColour()
    {
        (int upper, int lower) = HyperXLighting.Compose(Lit(LightingEffect.Static), 0);

        Assert.Equal(Red, upper);
        Assert.Equal(Blue, lower);
    }

    [Fact]
    public void Static_DoesNotMoveWithTime()
    {
        // The name is the promise. It also decides what the loop costs: a frame that
        // never changes is sent a few times a second to hold the lights, where a moving
        // one is sent twenty.
        Assert.Equal(
            HyperXLighting.Compose(Lit(LightingEffect.Static), 0),
            HyperXLighting.Compose(Lit(LightingEffect.Static), 37.5));
    }

    [Fact]
    public void Brightness_ScalesTheColourRatherThanReplacingIt()
    {
        LightingState half = Lit(LightingEffect.Static) with { Brightness = 50 };

        (int upper, _) = HyperXLighting.Compose(half, 0);

        // Half of full red, and still red: dimming that drifted towards white or black
        // would be a different colour at every level.
        Assert.Equal(0x800000, upper);
    }

    [Fact]
    public void BrightnessOfNothing_IsBlack()
    {
        (int upper, int lower) = HyperXLighting.Compose(Lit(LightingEffect.Static) with { Brightness = 0 }, 0);

        Assert.Equal(0, upper);
        Assert.Equal(0, lower);
    }

    [Fact]
    public void Blink_IsSquare_AndItsOffIsReallyOff()
    {
        LightingState state = Lit(LightingEffect.Blink);

        int[] seen = [.. Times(state, 40).Select(f => f.Upper).Distinct().Order()];

        // Two values and no others. A blink that dipped through a dark red on its way
        // would read as a fault in the microphone rather than as an effect.
        Assert.Equal([0, Red], seen);
    }

    [Fact]
    public void Breathing_PassesThroughManyLevelsOfOneColour()
    {
        LightingState state = Lit(LightingEffect.Breathing);

        int[] levels = [.. Times(state, 40).Select(f => f.Upper).Distinct()];

        Assert.True(levels.Length > 5, $"only {levels.Length} levels");

        // One colour throughout: red dimmed is still red, and green never appears.
        Assert.All(levels, level => Assert.Equal(0, level & 0x00FFFF));
    }

    [Fact]
    public void Wave_PutsTheTwoBarsOutOfStep()
    {
        LightingState state = Lit(LightingEffect.Wave, upper: Red, lower: Red);

        // Same colour in both, so any difference between the bars is the phase and
        // nothing else. Two bars is not much of a wave, but it is what the hardware has.
        Assert.Contains(Times(state, 40), f => f.Upper != f.Lower);
    }

    [Fact]
    public void Rainbow_IgnoresTheChosenColour()
    {
        (int Upper, int Lower)[] red = [.. Times(Lit(LightingEffect.Cycle, Red, Red), 40)];
        (int Upper, int Lower)[] blue = [.. Times(Lit(LightingEffect.Cycle, Blue, Blue), 40)];

        // A rainbow that started from the chosen hue would be a different effect wearing
        // the same name, and the row above it would look like it had stopped working.
        Assert.Equal(red, blue);
        Assert.True(red.Select(f => f.Upper).Distinct().Count() > 5);
    }

    [Fact]
    public void Rainbow_StaysAtFullSaturation()
    {
        foreach ((int upper, _) in Times(Lit(LightingEffect.Cycle), 40))
        {
            int max = Math.Max(upper >> 16 & 0xFF, Math.Max(upper >> 8 & 0xFF, upper & 0xFF));
            int min = Math.Min(upper >> 16 & 0xFF, Math.Min(upper >> 8 & 0xFF, upper & 0xFF));

            // Full one way and empty the other. A hue sweep that washed out would spend
            // most of its time being a slightly coloured white.
            Assert.Equal(255, max);
            Assert.Equal(0, min);
        }
    }

    [Fact]
    public void AFrame_IsTwoMarkedColours()
    {
        byte[] frame = HyperXLighting.Frame64(0x112233, 0x445566);

        // The shape watched going past NGENUITY, byte for byte.
        Assert.Equal<byte[]>([0x81, 0x11, 0x22, 0x33, 0x81, 0x44, 0x55, 0x66], frame);
    }

    [Fact]
    public void OffByDefault()
    {
        // The microphone arrives with lighting of its own. An application that took it
        // over on sight would be answering a question nobody asked.
        Assert.False(LightingState.Default.On);
    }

    private static IEnumerable<(int Upper, int Lower)> Times(LightingState state, int frames) =>
        Enumerable.Range(0, frames).Select(i => HyperXLighting.Compose(state, i * 0.05));
}
