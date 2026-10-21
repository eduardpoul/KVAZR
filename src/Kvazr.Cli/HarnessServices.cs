using Kvazr.Core.Settings;
using Microsoft.Extensions.Options;

namespace Kvazr.Cli;

/// <summary>
/// Settings that live only as long as the process. The harness must not touch the
/// user's real settings file — a diagnostic run should never be able to change what
/// the application does tomorrow.
/// </summary>
internal sealed class MemorySettingsStore : ISettingsStore
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

/// <summary>An options monitor over one fixed value, for hosts without configuration.</summary>
internal sealed class StaticOptions<T> : IOptionsMonitor<T>
    where T : class
{
    public StaticOptions(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
