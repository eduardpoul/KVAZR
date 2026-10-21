using Avalonia;
using Avalonia.Media;
using Kvazr.App.Branding;
using Xunit;

namespace Kvazr.App.Tests;

/// <summary>
/// The identity's own numbers, asserted against the implementation.
///
/// These are not tests of drawing. They are tests that the values in
/// <c>Design/kvazr/KVAZR Identity.dc.html</c> survived the transcription — the one
/// failure mode of a design system that nobody notices, because a mark drawn in the
/// wrong blue still looks like a mark.
/// </summary>
public class KvazrIdentityTests
{
    /// <summary>How far the canvas's rounded literals may sit from the derived value.</summary>
    private const double Rounding = 0.01;

    /// <summary>
    /// Every planet colour in the identity's <c>states</c> array, on both surfaces.
    /// The planet is the whole message at 16 px, so this is the table that matters.
    /// </summary>
    [Theory]
    [InlineData(KvazrMarkState.AllConnected, KvazrSurface.Light, "#0F6CBD")]
    [InlineData(KvazrMarkState.Searching, KvazrSurface.Light, "#B7C6D4")]
    [InlineData(KvazrMarkState.FirmwareUpdating, KvazrSurface.Light, "#7B4EC4")]
    [InlineData(KvazrMarkState.NoDevices, KvazrSurface.Light, "#A8B4C0")]
    [InlineData(KvazrMarkState.LowBattery, KvazrSurface.Light, "#B26A00")]
    [InlineData(KvazrMarkState.LinkError, KvazrSurface.Light, "#C42B1C")]
    [InlineData(KvazrMarkState.AllConnected, KvazrSurface.Dark, "#60CDFF")]
    [InlineData(KvazrMarkState.Searching, KvazrSurface.Dark, "#5E7690")]
    [InlineData(KvazrMarkState.FirmwareUpdating, KvazrSurface.Dark, "#B39CFF")]
    [InlineData(KvazrMarkState.NoDevices, KvazrSurface.Dark, "#7C8B9A")]
    [InlineData(KvazrMarkState.LowBattery, KvazrSurface.Dark, "#E8A33D")]
    [InlineData(KvazrMarkState.LinkError, KvazrSurface.Dark, "#F0625A")]
    public void Planet_matches_the_identity(KvazrMarkState state, KvazrSurface surface, string expected) =>
        Assert.Equal(Color.Parse(expected), KvazrPalettes.For(state, surface).Planet);

    [Theory]
    [InlineData(KvazrMarkState.AllConnected, KvazrSurface.Light, "#16324D")]
    [InlineData(KvazrMarkState.Searching, KvazrSurface.Light, "#16324D")]
    [InlineData(KvazrMarkState.FirmwareUpdating, KvazrSurface.Light, "#16324D")]
    [InlineData(KvazrMarkState.LowBattery, KvazrSurface.Light, "#16324D")]
    [InlineData(KvazrMarkState.LinkError, KvazrSurface.Light, "#16324D")]
    [InlineData(KvazrMarkState.NoDevices, KvazrSurface.Light, "#9AA7B4")]
    [InlineData(KvazrMarkState.AllConnected, KvazrSurface.Dark, "#A8BFD6")]
    [InlineData(KvazrMarkState.NoDevices, KvazrSurface.Dark, "#5A6B7C")]
    public void Orbit_matches_the_identity(KvazrMarkState state, KvazrSurface surface, string expected) =>
        Assert.Equal(Color.Parse(expected), KvazrPalettes.For(state, surface).Ring);

    /// <summary>
    /// "Нет устройств" takes the whole mark grey — core included. Every other state
    /// leaves the core in the accent, which is what makes the planet the thing being
    /// read rather than the glyph as a whole.
    /// </summary>
    [Theory]
    [InlineData(KvazrMarkState.AllConnected)]
    [InlineData(KvazrMarkState.Searching)]
    [InlineData(KvazrMarkState.FirmwareUpdating)]
    [InlineData(KvazrMarkState.LowBattery)]
    [InlineData(KvazrMarkState.LinkError)]
    public void Core_stays_accent_in_every_state_but_one(KvazrMarkState state)
    {
        Assert.Equal(KvazrPalettes.Accent, KvazrPalettes.For(state, KvazrSurface.Light).Core);
        Assert.Equal(KvazrPalettes.AccentDark, KvazrPalettes.For(state, KvazrSurface.Dark).Core);
    }

    [Fact]
    public void No_devices_takes_the_core_grey_too()
    {
        Assert.NotEqual(KvazrPalettes.Accent, KvazrPalettes.For(KvazrMarkState.NoDevices, KvazrSurface.Light).Core);
        Assert.NotEqual(KvazrPalettes.AccentDark, KvazrPalettes.For(KvazrMarkState.NoDevices, KvazrSurface.Dark).Core);
    }

    /// <summary>
    /// Motion carries meaning. A stopped link must not spin — the identity is explicit
    /// that the rotation stops — and a grey mark spinning would be looking for devices
    /// it has already decided are not there.
    /// </summary>
    [Theory]
    [InlineData(KvazrMarkState.Searching, true)]
    [InlineData(KvazrMarkState.FirmwareUpdating, true)]
    [InlineData(KvazrMarkState.AllConnected, false)]
    [InlineData(KvazrMarkState.NoDevices, false)]
    [InlineData(KvazrMarkState.LowBattery, false)]
    [InlineData(KvazrMarkState.LinkError, false)]
    public void Only_the_working_states_turn(KvazrMarkState state, bool turns) =>
        Assert.Equal(turns, KvazrPalettes.Turns(state));

    /// <summary>
    /// The gap is the one thing the identity forbids changing, so it is worth an
    /// assertion rather than a comment.
    /// </summary>
    [Fact]
    public void Gap_is_fifty_five_degrees()
    {
        Assert.Equal(305, KvazrMark.Sweep);
        Assert.Equal(55, KvazrMark.Gap);
    }

    /// <summary>
    /// The canvas ships the arc as <c>M32 6 A26 26 0 1 1 10.7 17.09</c>. Deriving the
    /// same two points from the sweep angle is what lets the tile reuse the geometry at
    /// a different radius; if the derivation drifts, the tile drifts silently and only
    /// the full-size mark still matches the design.
    /// </summary>
    [Fact]
    public void Arc_endpoints_match_the_published_path()
    {
        Point start = KvazrMark.OnOrbit(26, 0);
        Point end = KvazrMark.OnOrbit(26, KvazrMark.Sweep);

        // The canvas quotes its path in rounded units, so the comparison is to the
        // precision it was written at. Anything tighter would be asserting that the
        // designer typed more digits than they did.
        Assert.Equal(32, start.X, Rounding);
        Assert.Equal(6, start.Y, Rounding);
        Assert.Equal(10.70, end.X, Rounding);
        Assert.Equal(17.09, end.Y, Rounding);
    }

    /// <summary>The planet, at both radii the identity publishes.</summary>
    [Fact]
    public void Planet_sits_where_the_canvas_puts_it()
    {
        Point large = KvazrMark.OnOrbit(26, KvazrMark.PlanetAngle);
        Assert.Equal(20.00, large.X, Rounding);
        Assert.Equal(8.94, large.Y, Rounding);

        // The tile's literals are quoted to one decimal, not two.
        Point tile = KvazrMark.OnOrbit(20, KvazrMark.PlanetAngle);
        Assert.Equal(22.8, tile.X, 0.05);
        Assert.Equal(14.3, tile.Y, 0.05);
    }

    /// <summary>
    /// One geometry, two sets of dimensions, and no third invented anywhere. The tile's
    /// stroke is deliberately not the proportional 2.31.
    /// </summary>
    [Fact]
    public void Only_two_sets_of_metrics_exist()
    {
        Assert.Equal(26, KvazrMark.Standalone.RingRadius);
        Assert.Equal(3, KvazrMark.Standalone.RingThickness);
        Assert.Equal(11, KvazrMark.Standalone.CoreRadius);
        Assert.Equal(5, KvazrMark.Standalone.PlanetRadius);

        Assert.Equal(20, KvazrMark.Tile.RingRadius);
        Assert.Equal(2.6, KvazrMark.Tile.RingThickness);
        Assert.Equal(8.5, KvazrMark.Tile.CoreRadius);
        Assert.Equal(3.9, KvazrMark.Tile.PlanetRadius);
    }

    /// <summary>The identity's timings, to the tenth of a second it states them in.</summary>
    [Fact]
    public void Timings_are_the_published_ones()
    {
        Assert.Equal(TimeSpan.FromSeconds(2.8), KvazrTimings.Motion(KvazrMotion.Loading));
        Assert.Equal(TimeSpan.FromSeconds(1.4), KvazrTimings.Motion(KvazrMotion.Fast));
        Assert.Equal(TimeSpan.FromSeconds(6), KvazrTimings.Motion(KvazrMotion.Breathing));
        Assert.Equal(TimeSpan.Zero, KvazrTimings.Motion(KvazrMotion.Still));
    }

    /// <summary>
    /// Adjacent skeleton blocks are shifted 0.1–0.15 s apart. Outside that band the row
    /// stops reading as one object: too little and the blocks pulse in unison, too much
    /// and they look like separate things blinking at each other.
    /// </summary>
    [Fact]
    public void Skeleton_phase_step_is_inside_the_published_band()
    {
        Assert.InRange(KvazrTimings.SkeletonPhaseStep.TotalSeconds, 0.1, 0.15);
        Assert.Equal(TimeSpan.FromSeconds(1.4), KvazrTimings.SkeletonPulse);
    }
}
