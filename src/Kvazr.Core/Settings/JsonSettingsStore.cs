using System.Text.Json;
using System.Text.Json.Serialization;
using Kvazr.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Kvazr.Core.Settings;

/// <summary>
/// Stores settings as JSON under the user's roaming profile. Writes go through a
/// temporary file and a replace, so a crash mid-save cannot leave a truncated file
/// that the next start refuses to parse.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Changed;

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
        : this(DefaultPath(), logger)
    {
    }

    public JsonSettingsStore(string path, ILogger<JsonSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = path;
        _logger = logger;
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KVAZR",
        "settings.json");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return;

            string json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            if (loaded is not null) Current = loaded;
        }
        catch (JsonException ex)
        {
            // A corrupt file must not stop the app from starting; defaults are fine.
            _logger.SettingsUnreadable(ex, _path);
        }
        catch (IOException ex)
        {
            _logger.SettingsUnreadable(ex, _path);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, Current);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(Current, SerializerOptions);
            string temporary = _path + ".tmp";

            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.SettingsNotSaved(ex, _path);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, Current);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _gate.Dispose();
    }
}
