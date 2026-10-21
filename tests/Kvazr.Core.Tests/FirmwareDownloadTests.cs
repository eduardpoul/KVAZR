// PARKED — not compiled. Re-enable by changing the directive below to `#if true`.
//
// This downloads Audeze's firmware images. It works and it is tested, but it has no
// caller: flashing is done by Audeze's own application, which fetches its own copies,
// and that application covers rollback as well, so there is no situation in which a
// user needs the file this produces. Shipping the button would have been a control
// that does nothing.
//
// It is parked rather than deleted because it is the half of an update that has to
// exist before a flash can, and it has already been checked against the live bucket:
// the URLs it resolves serve 2,052,268 and 1,241,391 bytes, byte for byte the files
// Audeze's app downloaded onto this machine.
//
// If the FOTA protocol is never reverse-engineered, delete both this file and
// FirmwareDownloadTests.cs rather than leaving them here indefinitely.
#if false

using System.Net;
using Kvazr.Core.Firmware;
using Kvazr.Race.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kvazr.Core.Tests;

/// <summary>
/// Downloading firmware is the one place in this app where a corrupt file has physical
/// consequences, and Audeze's manifests carry neither checksum nor signature. Size is
/// the only thing left to check, so these tests are mostly about what gets refused.
/// </summary>
public sealed class FirmwareDownloadTests : IDisposable
{
    private const string HeadsetUrl = "https://example.invalid/Maxwell_XBOX_headset.bin";
    private const string DongleUrl = "https://example.invalid/Maxwell_XBOX_dongle.bin";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "maxwell-fw-tests", Guid.NewGuid().ToString("N"));

    private static FirmwareRelease Release() =>
        new(FirmwareVersion.Parse("v1.0.1.74")!.Value,
            new Uri(HeadsetUrl), new Uri(DongleUrl), null, null);

    private FirmwareDownloader Build(FakeHandler handler) =>
        new(new HttpClient(handler), NullLogger<FirmwareDownloader>.Instance, _root);

    [Fact]
    public async Task Downloads_BothImages_ForTheConnectedHardware()
    {
        var handler = new FakeHandler(bytes: 1_500_000);
        FirmwareDownloader downloader = Build(handler);

        FirmwareDownload? result = await downloader.DownloadAsync(
            Release(), MaxwellTransport.XboxUsbC,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(File.Exists(result.HeadsetPath));
        Assert.True(File.Exists(result.DonglePath));
        Assert.Equal(3_000_000, result.TotalBytes);
    }

    /// <summary>
    /// A PlayStation release offers no Xbox image. Downloading the wrong flavour is
    /// precisely the mistake that bricks hardware, so the absence is a refusal rather
    /// than a fallback to whatever is available.
    /// </summary>
    [Fact]
    public async Task Refuses_WhenTheReleaseHasNoImageForThisHardware()
    {
        var handler = new FakeHandler(bytes: 1_500_000);
        FirmwareDownloader downloader = Build(handler);

        FirmwareDownload? result = await downloader.DownloadAsync(
            Release(), MaxwellTransport.PlayStationUsbC,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(0, handler.Requests);
    }

    /// <summary>An S3 error page or a captive portal's login form, saved under a .bin name.</summary>
    [Fact]
    public async Task Refuses_AResponseTooSmallToBeFirmware()
    {
        var handler = new FakeHandler(bytes: 900);
        FirmwareDownloader downloader = Build(handler);

        FirmwareDownload? result = await downloader.DownloadAsync(
            Release(), MaxwellTransport.XboxUsbC,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(Directory.GetFiles(downloader.DirectoryFor(FirmwareVersion.Parse("v1.0.1.74")!.Value)));
    }

    [Fact]
    public async Task Refuses_AnHttpError()
    {
        var handler = new FakeHandler(bytes: 1_500_000) { Status = HttpStatusCode.Forbidden };
        FirmwareDownloader downloader = Build(handler);

        FirmwareDownload? result = await downloader.DownloadAsync(
            Release(), MaxwellTransport.XboxUsbC,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    /// <summary>
    /// A half-written file must never be left where a complete one is expected — the
    /// next run would hand it to a flasher as finished firmware.
    /// </summary>
    [Fact]
    public async Task LeavesNoPartialFile_WhenTheTransferFails()
    {
        var handler = new FakeHandler(bytes: 1_500_000) { FailMidStream = true };
        FirmwareDownloader downloader = Build(handler);

        await downloader.DownloadAsync(Release(), MaxwellTransport.XboxUsbC,
            cancellationToken: TestContext.Current.CancellationToken);

        string directory = downloader.DirectoryFor(FirmwareVersion.Parse("v1.0.1.74")!.Value);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task ReusesAnAlreadyDownloadedImage_RatherThanFetchingItAgain()
    {
        var handler = new FakeHandler(bytes: 1_500_000);
        FirmwareDownloader downloader = Build(handler);

        await downloader.DownloadAsync(Release(), MaxwellTransport.XboxUsbC,
            cancellationToken: TestContext.Current.CancellationToken);
        int afterFirst = handler.Requests;

        await downloader.DownloadAsync(Release(), MaxwellTransport.XboxUsbC,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, afterFirst);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task ReportsProgress_EndingAtOne()
    {
        var handler = new FakeHandler(bytes: 1_500_000);
        FirmwareDownloader downloader = Build(handler);

        var seen = new List<double>();
        await downloader.DownloadAsync(Release(), MaxwellTransport.XboxUsbC,
            new Progress<double>(seen.Add), TestContext.Current.CancellationToken);

        // Progress is reported from a callback that may still be draining; the contract
        // that matters is that it never runs backwards past the end.
        Assert.All(seen, value => Assert.InRange(value, 0.0, 1.0));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeHandler(long bytes) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public bool FailMidStream { get; init; }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;

            if (Status != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(Status));

            HttpContent content = FailMidStream
                ? new StreamContent(new FailingStream(bytes))
                : new ByteArrayContent(new byte[bytes]);

            content.Headers.ContentLength = bytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>Delivers a little and then breaks, the way a dropped connection does.</summary>
    private sealed class FailingStream(long length) : Stream
    {
        private long _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get => _served; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served > 64 * 1024) throw new IOException("connection reset");

            int served = Math.Min(count, 16 * 1024);
            _served += served;
            return served;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

#endif
