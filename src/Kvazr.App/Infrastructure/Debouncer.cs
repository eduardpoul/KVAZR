namespace Kvazr.App.Infrastructure;

/// <summary>
/// Collapses a burst of calls into one, delayed, execution.
///
/// This is not a nicety. Dragging a gain slider raises a value change per pixel, and
/// every RACE write is a round trip that also persists to the headset's NVDM — sending
/// one per frame would flood the device and wear the flash. Only the value the user
/// settles on is written.
/// </summary>
internal sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    public Debouncer(TimeSpan delay) => _delay = delay;

    public void Schedule(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _pending, cts);

        previous?.Cancel();
        previous?.Dispose();

        _ = RunAsync(action, cts.Token);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await action(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer value; that is the whole point.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancellationTokenSource? pending = Interlocked.Exchange(ref _pending, null);
        pending?.Cancel();
        pending?.Dispose();
    }
}
