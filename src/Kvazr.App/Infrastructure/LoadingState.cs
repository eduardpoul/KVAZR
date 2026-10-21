using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kvazr.App.Infrastructure;

/// <summary>
/// A <see cref="LoadingGate"/> wired to the dispatcher and exposed as three booleans a
/// view can bind to.
///
/// The timer is armed for the one instant the gate says something will change, rather
/// than ticking. There is nothing to animate here — the pulsing is done by the
/// skeleton control itself — so a running timer would be waking the UI thread to
/// decide, every frame, that it is still waiting.
/// </summary>
internal sealed partial class LoadingState : ObservableObject, IDisposable
{
    private readonly LoadingGate _gate = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private bool _disposed;

    public LoadingState()
    {
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Publish();
        };
    }

    /// <summary>Placeholders are on screen.</summary>
    public bool ShowSkeleton => Phase == LoadingPhase.Skeleton;

    /// <summary>The answer is in and may be drawn.</summary>
    public bool ShowContent => Phase == LoadingPhase.Content;

    /// <summary>Waited too long. The view shows text, not a placeholder.</summary>
    public bool ShowError => Phase == LoadingPhase.Failed;

    [ObservableProperty]
    public partial LoadingPhase Phase { get; private set; } = LoadingPhase.Idle;

    /// <summary>A request went out.</summary>
    public void Begin()
    {
        _gate.Begin(DateTimeOffset.UtcNow);
        Publish();
    }

    /// <summary>The answer arrived, whatever it turned out to be.</summary>
    public void Resolve()
    {
        _gate.Resolve(DateTimeOffset.UtcNow);
        Publish();
    }

    private void Publish()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Phase = _gate.Advance(now);

        _timer.Stop();

        if (_gate.NextDue(now) is not { } due) return;

        // A zero or negative interval is rejected by DispatcherTimer, and "due now"
        // is a real answer from the gate — the minimum keeps it on the next tick.
        _timer.Interval = due <= now ? TimeSpan.FromMilliseconds(1) : due - now;
        _timer.Start();
    }

    partial void OnPhaseChanged(LoadingPhase value)
    {
        OnPropertyChanged(nameof(ShowSkeleton));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
    }
}
