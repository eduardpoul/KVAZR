using System.Text.Json;
using System.Text.Json.Serialization;
using Maxwell.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Maxwell.Core.Firmware;

/// <summary>Reads Audeze's published firmware manifests.</summary>
public interface IFirmwareCatalog
{
    /// <summary>The current release, or null when the manifest could not be read.</summary>
    Task<FirmwareRelease?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Older releases Audeze still publishes, newest first. Empty on failure.</summary>
    Task<IReadOnlyList<FirmwareRelease>> GetRollbacksAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Audeze publishes firmware as plain JSON in a public S3 bucket — no key, no token,
/// no signature. The addresses below are lifted from the strings compiled into their
/// own application.
///
/// Note what the manifests do NOT carry: no checksum and no signature over the
/// binaries. Anything that eventually flashes these images has to establish their
/// integrity by some other means, because the manifest cannot vouch for them.
/// </summary>
public sealed class FirmwareCatalog : IFirmwareCatalog
{
    private static readonly Uri LatestManifest =
        new("https://audeze-maxwell.s3.us-east-1.amazonaws.com/maxwell-fw.json");

    /// <summary>
    /// Two rollback indexes exist and they overlap; the vendor picks between them by
    /// installed version. Both are read and merged instead, which costs one extra
    /// request and cannot silently hide a release from the user.
    /// </summary>
    private static readonly Uri[] RollbackIndexes =
    [
        new("https://audeze-maxwell.s3.us-east-1.amazonaws.com/maxwell-new-rollback.json"),
        new("https://audeze-maxwell.s3.us-east-1.amazonaws.com/maxwell-old-rollback.json"),
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;
    private readonly ILogger<FirmwareCatalog> _logger;

    public FirmwareCatalog(HttpClient client, ILogger<FirmwareCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _logger = logger;
    }

    public Task<FirmwareRelease?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        ReadReleaseAsync(LatestManifest, cancellationToken);

    public async Task<IReadOnlyList<FirmwareRelease>> GetRollbacksAsync(
        CancellationToken cancellationToken = default)
    {
        var links = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);

        foreach (Uri index in RollbackIndexes)
        {
            RollbackIndex? listing = await ReadJsonAsync<RollbackIndex>(index, cancellationToken)
                .ConfigureAwait(false);
            if (listing?.Firmwares is null) continue;

            foreach (RollbackEntry entry in listing.Firmwares)
            {
                if (entry.Version is null || entry.Link is null) continue;
                if (!Uri.TryCreate(entry.Link, UriKind.Absolute, out Uri? link)) continue;

                links[entry.Version] = link;
            }
        }

        var releases = new List<FirmwareRelease>();
        foreach (Uri link in links.Values)
        {
            FirmwareRelease? release = await ReadReleaseAsync(link, cancellationToken).ConfigureAwait(false);
            if (release is not null) releases.Add(release);
        }

        return releases.OrderByDescending(r => r.Version).ToList();
    }

    private async Task<FirmwareRelease?> ReadReleaseAsync(Uri manifest, CancellationToken cancellationToken)
    {
        ReleaseManifest? payload = await ReadJsonAsync<ReleaseManifest>(manifest, cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Version is null) return null;
        if (FirmwareVersion.Parse(payload.Version) is not { } version) return null;

        return new FirmwareRelease(
            version,
            ToUri(payload.XboxHeadset),
            ToUri(payload.XboxDongle),
            ToUri(payload.PsHeadset),
            ToUri(payload.PsDongle));
    }

    private async Task<T?> ReadJsonAsync<T>(Uri address, CancellationToken cancellationToken)
        where T : class
    {
        // A manifest is a few hundred bytes. If it has not arrived in this long, the
        // network is not going to produce it, and a background check must not hang on to
        // the download-sized budget the shared client is configured with.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(address, deadline.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.FirmwareManifestFailed(address.ToString(), (int)response.StatusCode);
                return null;
            }

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);

            return await JsonSerializer
                .DeserializeAsync<T>(stream, SerializerOptions, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // No network, a captive portal, or a manifest that changed shape. None of
            // these is worth interrupting the user over — the check simply did not
            // happen and will be retried on the next interval.
            _logger.FirmwareCheckFailed(ex, address.ToString());
            return null;
        }
    }

    private static Uri? ToUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;

    // ---------- manifest shapes ----------

    private sealed class ReleaseManifest
    {
        public string? Version { get; init; }

        [JsonPropertyName("xbox_headset")]
        public string? XboxHeadset { get; init; }

        [JsonPropertyName("xbox_dongle")]
        public string? XboxDongle { get; init; }

        [JsonPropertyName("ps_headset")]
        public string? PsHeadset { get; init; }

        [JsonPropertyName("ps_dongle")]
        public string? PsDongle { get; init; }
    }

    private sealed class RollbackIndex
    {
        public List<RollbackEntry>? Firmwares { get; init; }
    }

    private sealed class RollbackEntry
    {
        public string? Version { get; init; }

        public string? Link { get; init; }
    }
}
