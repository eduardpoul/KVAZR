using Maxwell.Core.Firmware;
using Maxwell.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

public sealed class FirmwareVersionTests
{
    [Theory]
    [InlineData("v1.0.1.74", 1, 0, 1, 74)]
    [InlineData("1.0.1.74", 1, 0, 1, 74)]
    [InlineData("  V1.0.1.61  ", 1, 0, 1, 61)]
    public void Parses_TheFormsAudezeUses(string text, int major, int minor, int patch, int build) =>
        Assert.Equal(new FirmwareVersion(major, minor, patch, build), FirmwareVersion.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.1")]
    [InlineData("1.0.1.74.2")]
    [InlineData("v1.0.1.x")]
    [InlineData("latest")]
    public void Rejects_WhatItCannotUnderstand(string? text) =>
        Assert.Null(FirmwareVersion.Parse(text));

    /// <summary>
    /// The reason this is a numeric type rather than a string comparison: sorted as
    /// text, "1.0.1.9" comes after "1.0.1.74", and the app would cheerfully offer a
    /// two-release downgrade as an update.
    /// </summary>
    [Fact]
    public void Compares_Numerically_NotAsText()
    {
        FirmwareVersion nine = FirmwareVersion.Parse("v1.0.1.9")!.Value;
        FirmwareVersion seventyFour = FirmwareVersion.Parse("v1.0.1.74")!.Value;

        Assert.True(seventyFour > nine);
        Assert.True(string.CompareOrdinal("v1.0.1.9", "v1.0.1.74") > 0);
    }

    [Fact]
    public void RoundTrips_ThroughTheTextTheDeviceReports()
    {
        FirmwareVersion version = FirmwareVersion.Parse("v1.0.1.74")!.Value;

        Assert.Equal("v1.0.1.74", version.ToString());
        Assert.Equal(version, FirmwareVersion.Parse(version.ToString()));
    }
}

public sealed class FirmwareCheckTests
{
    private static FirmwareRelease Release(string version) =>
        new(FirmwareVersion.Parse(version)!.Value, null, null, null, null);

    [Fact]
    public void UpdateAvailable_OnlyWhenThePublishedOneIsNewer()
    {
        Assert.True(Check("v1.0.1.61", "v1.0.1.74").UpdateAvailable);
        Assert.False(Check("v1.0.1.74", "v1.0.1.74").UpdateAvailable);
        Assert.False(Check("v1.0.1.74", "v1.0.1.63").UpdateAvailable);
    }

    /// <summary>
    /// An unread headset is not an out-of-date headset. Prompting on missing data is
    /// how a notice trains its user to dismiss it unread.
    /// </summary>
    [Fact]
    public void UpdateAvailable_IsFalse_WhenTheInstalledVersionIsUnknown() =>
        Assert.False(new FirmwareCheck(null, Release("v1.0.1.74"), [], DateTimeOffset.UnixEpoch)
            .UpdateAvailable);

    [Fact]
    public void AllReleases_MergesTheRollbacks_NewestFirst()
    {
        var check = new FirmwareCheck(
            FirmwareVersion.Parse("v1.0.1.61"),
            Release("v1.0.1.74"),
            [Release("v1.0.1.61"), Release("v1.0.1.63"), Release("v1.0.1.74")],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(
            ["v1.0.1.74", "v1.0.1.63", "v1.0.1.61"],
            check.AllReleases.Select(r => r.Version.ToString()));
    }

    private static FirmwareCheck Check(string installed, string latest) =>
        new(FirmwareVersion.Parse(installed), Release(latest), [], DateTimeOffset.UnixEpoch);
}

public sealed class FirmwareUpdateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACheckIsDue_WhenItHasNeverRun()
    {
        using Fixture fixture = new();

        Assert.True(fixture.Service.IsDue());
    }

    [Fact]
    public async Task ACheckIsNotDue_WithinTheInterval()
    {
        using Fixture fixture = new();
        await fixture.Service.CheckAsync("v1.0.1.74", cancellationToken: TestContext.Current.CancellationToken);

        fixture.Clock.Now = Now.AddDays(3);

        Assert.False(fixture.Service.IsDue());
    }

    [Fact]
    public async Task ACheckIsDue_OnceTheWeekHasPassed()
    {
        using Fixture fixture = new();
        await fixture.Service.CheckAsync("v1.0.1.74", cancellationToken: TestContext.Current.CancellationToken);

        fixture.Clock.Now = Now + FirmwareUpdateService.CheckInterval;

        Assert.True(fixture.Service.IsDue());
    }

    /// <summary>
    /// A clock that jumped backwards — corrected system time, a restored snapshot —
    /// must not lock checking out until the stored date comes round again.
    /// </summary>
    [Fact]
    public async Task ACheckIsDue_WhenTheClockWentBackwards()
    {
        using Fixture fixture = new();
        await fixture.Service.CheckAsync("v1.0.1.74", cancellationToken: TestContext.Current.CancellationToken);

        fixture.Clock.Now = Now.AddYears(-1);

        Assert.True(fixture.Service.IsDue());
    }

    [Fact]
    public void NoCheckIsDue_WhenTheUserTurnedItOff()
    {
        using Fixture fixture = new();
        fixture.Settings.Current.CheckFirmwareUpdates = false;

        Assert.False(fixture.Service.IsDue());
    }

    /// <summary>
    /// A week of no network must not be recorded as a check, or the next attempt slides
    /// out by another week for every failure.
    /// </summary>
    [Fact]
    public async Task AFailedFetch_DoesNotCountAsAcheck()
    {
        using Fixture fixture = new();
        fixture.Catalog.Offline = true;

        await fixture.Service.CheckAsync("v1.0.1.74", force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(fixture.Settings.Current.LastFirmwareCheck);
        Assert.True(fixture.Service.IsDue());
    }

    [Fact]
    public async Task AnExplicitRequest_RunsEvenWhenNotDue()
    {
        using Fixture fixture = new();
        await fixture.Service.CheckAsync("v1.0.1.74", cancellationToken: TestContext.Current.CancellationToken);
        fixture.Catalog.Calls = 0;

        await fixture.Service.CheckAsync("v1.0.1.74", force: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Catalog.Calls);
    }

    [Fact]
    public async Task AnUpdate_IsAnnouncedOncePerVersion_NotOncePerCheck()
    {
        using Fixture fixture = new();

        FirmwareCheck check = await fixture.Service.CheckAsync("v1.0.1.61",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(fixture.Service.ShouldAnnounce(check));

        await fixture.Service.MarkAnnouncedAsync(check, TestContext.Current.CancellationToken);

        Assert.False(fixture.Service.ShouldAnnounce(check));
    }

    /// <summary>
    /// The findings survive a restart. Without this the screen sits empty until the
    /// weekly interval comes round again, which is up to seven days of a section that
    /// looks broken to anyone who opens it.
    /// </summary>
    [Fact]
    public async Task TheLastResult_SurvivesARestart()
    {
        using Fixture first = new();
        await first.Service.CheckAsync("v1.0.1.61", cancellationToken: TestContext.Current.CancellationToken);

        // Same settings, new service: exactly what the next launch does.
        using var restarted = new FirmwareUpdateService(
            first.Catalog, first.Settings, first.Clock, NullLogger<FirmwareUpdateService>.Instance);

        Assert.Equal("v1.0.1.74", restarted.Last.Latest?.Version.ToString());
        Assert.NotEmpty(restarted.Last.AllReleases);
    }

    /// <summary>
    /// A restored result predates reading the headset, so it cannot know whether an
    /// update applies until the device answers.
    /// </summary>
    [Fact]
    public async Task ARestoredResult_JudgesTheUpdateOnlyOnceTheDeviceIsRead()
    {
        using Fixture first = new();
        await first.Service.CheckAsync("v1.0.1.61", cancellationToken: TestContext.Current.CancellationToken);

        using var restarted = new FirmwareUpdateService(
            first.Catalog, first.Settings, first.Clock, NullLogger<FirmwareUpdateService>.Instance);

        Assert.False(restarted.Last.UpdateAvailable);
        Assert.True(restarted.Last.WithInstalled("v1.0.1.61").UpdateAvailable);
        Assert.False(restarted.Last.WithInstalled("v1.0.1.74").UpdateAvailable);
    }

    // ---------- fixture ----------

    private sealed class Fixture : IDisposable
    {
        public FakeClock Clock { get; } = new() { Now = Now };

        public FakeSettings Settings { get; } = new();

        public FakeCatalog Catalog { get; } = new();

        public FirmwareUpdateService Service { get; }

        public Fixture() =>
            Service = new FirmwareUpdateService(
                Catalog, Settings, Clock, NullLogger<FirmwareUpdateService>.Instance);

        public void Dispose() => Service.Dispose();
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeCatalog : IFirmwareCatalog
    {
        public bool Offline { get; set; }

        public int Calls { get; set; }

        public Task<FirmwareRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<FirmwareRelease?>(Offline
                ? null
                : new FirmwareRelease(FirmwareVersion.Parse("v1.0.1.74")!.Value, null, null, null, null));
        }

        public Task<IReadOnlyList<FirmwareRelease>> GetRollbacksAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FirmwareRelease>>([]);
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
    }
}
