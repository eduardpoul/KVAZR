using Avalonia.Threading;
using Maxwell.App.Platform;
using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging;

namespace Maxwell.App.Infrastructure;

/// <summary>
/// "The drivers have been asked at least once."
///
/// Three places need this fact — the dashboard, the tray panel and the tray icon — and
/// they must not disagree about it, or the icon says it is still looking while the
/// window has already given up.
///
/// The obvious source, <see cref="IDeviceRegistry.DevicesChanged"/>, cannot answer it:
/// the registry raises that only when a session's snapshot actually changes, so on a
/// machine with nothing attached it never fires at all, and everything waiting on it
/// would sit on a placeholder until the ten-second timeout turned it into an error —
/// on precisely the machine where "no devices" is the correct and final answer.
///
/// So the sweep is run from here and completion is the call returning. The registry
/// serialises discovery behind its own gate, so this costs at most one extra pass and
/// usually just waits out the one the scheduler already started.
/// </summary>
public sealed class FirstSweep
{
    private readonly IDeviceRegistry _registry;
    private readonly ILogger _logger;
    private bool _started;

    public FirstSweep(IDeviceRegistry registry, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _registry = registry;
        _logger = loggerFactory.CreateLogger("Discovery");
    }

    /// <summary>Raised once, on the UI thread, when the first sweep has finished.</summary>
    public event EventHandler? Completed;

    public bool IsComplete { get; private set; }

    /// <summary>
    /// Starts the sweep if it has not started. Safe to call from anywhere; the second
    /// and later calls do nothing.
    /// </summary>
    public void Begin()
    {
        if (_started) return;
        _started = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await _registry.DiscoverAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failed sweep is still a finished sweep: whatever went
            // wrong, the interface must stop saying "looking" and show what it has.
            catch (Exception ex)
            {
                _logger.FirstSweepFailed(ex);
            }
#pragma warning restore CA1031
            finally
            {
                Dispatcher.UIThread.Post(Finish);
            }
        });
    }

    /// <summary>
    /// Subscribes <paramref name="handler"/>, calling it straight away if the sweep is
    /// already done. Every consumer of this is built at a different moment, and a plain
    /// event would silently never fire for whichever one was late.
    /// </summary>
    public void WhenComplete(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (IsComplete)
        {
            handler();
            return;
        }

        Completed += (_, _) => handler();
    }

    private void Finish()
    {
        if (IsComplete) return;

        IsComplete = true;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
