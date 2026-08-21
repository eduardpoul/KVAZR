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

using System.Globalization;
using Maxwell.Core.Logging;
using Maxwell.Race.Abstractions;
using Microsoft.Extensions.Logging;

namespace Maxwell.Core.Firmware;

/// <summary>Where a release's two images ended up on disk.</summary>
public sealed record FirmwareDownload(string HeadsetPath, string DonglePath, long TotalBytes)
{
    public string Directory => Path.GetDirectoryName(HeadsetPath) ?? "";
}

public interface IFirmwareDownloader
{
    /// <summary>
    /// Fetch the headset and dongle images for one release, or null when they could not
    /// be had. Already-downloaded files of the right size are reused.
    /// </summary>
    Task<FirmwareDownload?> DownloadAsync(
        FirmwareRelease release,
        MaxwellTransport transport,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>The folder a release's images live in, whether or not they are there yet.</summary>
    string DirectoryFor(FirmwareVersion version);
}

/// <summary>
/// Downloads firmware images into a local cache.
///
/// The manifests carry no checksum and no signature, so nothing here can prove a file
/// is the firmware Audeze built. What it can do is refuse the failure modes that would
/// otherwise be written to disk under a .bin name: an S3 error page, a captive portal's
/// login form, a truncated transfer. Those are caught by size, because a real image is
/// between one and three megabytes and an error page is a few hundred bytes.
/// </summary>
public sealed class FirmwareDownloader : IFirmwareDownloader
{
    /// <summary>Smallest plausible image. Every published image is over a megabyte.</summary>
    private const long MinimumSize = 256 * 1024;

    /// <summary>Largest plausible image, well clear of the 2.1 MB ones Audeze ship.</summary>
    private const long MaximumSize = 16 * 1024 * 1024;

    private readonly HttpClient _client;
    private readonly ILogger<FirmwareDownloader> _logger;
    private readonly string _root;

    public FirmwareDownloader(HttpClient client, ILogger<FirmwareDownloader> logger)
        : this(client, logger, DefaultRoot())
    {
    }

    public FirmwareDownloader(HttpClient client, ILogger<FirmwareDownloader> logger, string root)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _client = client;
        _logger = logger;
        _root = root;
    }

    /// <summary>
    /// Local rather than roaming: these are several megabytes of machine-specific cache
    /// and have no business travelling with a domain profile.
    /// </summary>
    private static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Maxwell.App",
        "firmware");

    public string DirectoryFor(FirmwareVersion version) =>
        Path.Combine(_root, version.ToString());

    public async Task<FirmwareDownload?> DownloadAsync(
        FirmwareRelease release,
        MaxwellTransport transport,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.HeadsetImageFor(transport) is not { } headsetUri) return null;
        if (release.DongleImageFor(transport) is not { } dongleUri) return null;

        string directory = DirectoryFor(release.Version);
        System.IO.Directory.CreateDirectory(directory);

        string headsetPath = Path.Combine(directory, FileName(headsetUri));
        string donglePath = Path.Combine(directory, FileName(dongleUri));

        // Two files, so each is half the bar. Coarse, but the alternative is a HEAD
        // request per file purely to make a progress bar smoother.
        long headsetBytes = await FetchAsync(headsetUri, headsetPath, Scaled(progress, 0.0), cancellationToken)
            .ConfigureAwait(false);
        if (headsetBytes <= 0) return null;

        long dongleBytes = await FetchAsync(dongleUri, donglePath, Scaled(progress, 0.5), cancellationToken)
            .ConfigureAwait(false);
        if (dongleBytes <= 0) return null;

        progress?.Report(1.0);
        return new FirmwareDownload(headsetPath, donglePath, headsetBytes + dongleBytes);
    }

    private async Task<long> FetchAsync(
        Uri address, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var existing = new FileInfo(destination);
        if (existing.Exists && existing.Length >= MinimumSize)
        {
            progress?.Report(0.5);
            return existing.Length;
        }

        // Written under a temporary name and moved into place, so an interrupted
        // download can never be mistaken for a complete image on the next run.
        string temporary = destination + ".part";

        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.FirmwareManifestFailed(address.ToString(), (int)response.StatusCode);
                return 0;
            }

            long? declared = response.Content.Headers.ContentLength;
            if (declared is { } size && (size < MinimumSize || size > MaximumSize))
            {
                _logger.FirmwareImageRejected(address.ToString(), size);
                return 0;
            }

            await using (Stream source = await response.Content
                             .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream target = File.Create(temporary))
            {
                await CopyAsync(source, target, declared, progress, cancellationToken).ConfigureAwait(false);
            }

            var written = new FileInfo(temporary);
            if (written.Length < MinimumSize || written.Length > MaximumSize)
            {
                _logger.FirmwareImageRejected(address.ToString(), written.Length);
                File.Delete(temporary);
                return 0;
            }

            File.Move(temporary, destination, overwrite: true);
            return written.Length;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger.FirmwareCheckFailed(ex, address.ToString());
            TryDelete(temporary);
            return 0;
        }
    }

    private static async Task CopyAsync(
        Stream source, Stream target, long? total, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long copied = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;

            if (total is { } size and > 0) progress?.Report(Math.Min(0.5, copied / (double)size * 0.5));
        }
    }

    /// <summary>Shifts a half-file progress report into its half of the whole.</summary>
    private static Shifted? Scaled(IProgress<double>? progress, double offset) =>
        progress is null ? null : new Shifted(progress, offset);

    private static string FileName(Uri address)
    {
        string name = Path.GetFileName(address.LocalPath);

        // Never trust a remote path to name a local file.
        return string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"image-{address.GetHashCode():X8}.bin")
            : Path.GetFileName(name);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover .part is harmless; it is overwritten on the next attempt.
        }
    }

    private sealed class Shifted(IProgress<double> inner, double offset) : IProgress<double>
    {
        public void Report(double value) => inner.Report(offset + value);
    }
}

#endif
