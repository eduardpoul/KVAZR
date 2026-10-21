namespace Kvazr.App.Infrastructure;

/// <summary>What a waiting area should be showing right now.</summary>
internal enum LoadingPhase
{
    /// <summary>Nothing is being waited for.</summary>
    Idle,

    /// <summary>
    /// Waiting, but not long enough to say so. The area holds its previous content —
    /// or nothing — and stays completely still.
    /// </summary>
    Waiting,

    /// <summary>Placeholders, at the height the real content will take.</summary>
    Skeleton,

    /// <summary>The real thing.</summary>
    Content,

    /// <summary>Waited too long. Error text, not a spinner that never stops.</summary>
    Failed,
}

/// <summary>
/// The timing rules for a skeleton, on their own so they can be tested without a
/// screen.
///
/// The three numbers are the whole of it, and each exists because of a specific way
/// that loading indicators go wrong:
///
/// <list type="bullet">
///   <item><b>300 ms before appearing.</b> Most polls answer faster than that, and a
///   placeholder that appears and vanishes inside a frame is worse than no placeholder:
///   the user sees a flash and cannot tell what it was.</item>
///   <item><b>500 ms minimum on screen.</b> The same flash, from the other end. Once
///   the skeleton is up it stays up long enough to be read as a state rather than as
///   a glitch, even if the data lands a millisecond later.</item>
///   <item><b>10 s and it becomes an error.</b> A skeleton says "shortly". After ten
///   seconds that is a lie, and a placeholder that pulses forever is the most common
///   way a UI silently loses a request.</item>
/// </list>
///
/// The caller supplies the time. A gate that read the clock itself would need the
/// clock mocked to be tested, and this is small enough that passing the instant is
/// cheaper than the abstraction.
/// </summary>
internal sealed class LoadingGate
{
    /// <summary>How long a wait has to last before it is worth drawing.</summary>
    public static TimeSpan AppearAfter { get; } = TimeSpan.FromMilliseconds(300);

    /// <summary>How long a drawn skeleton stays, once drawn.</summary>
    public static TimeSpan MinimumVisible { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>When waiting stops being waiting and becomes a failure.</summary>
    public static TimeSpan GiveUpAfter { get; } = TimeSpan.FromSeconds(10);

    private DateTimeOffset _startedAt;
    private DateTimeOffset _shownAt;
    private bool _resolved;

    public LoadingPhase Phase { get; private set; } = LoadingPhase.Idle;

    /// <summary>A request went out.</summary>
    public void Begin(DateTimeOffset now)
    {
        _startedAt = now;
        _resolved = false;
        Phase = LoadingPhase.Waiting;
    }

    /// <summary>
    /// The answer arrived. Returns the phase now in force — which may still be
    /// <see cref="LoadingPhase.Skeleton"/>, because a skeleton that has just appeared
    /// owes the user half a second before it is allowed to disappear again.
    /// </summary>
    public LoadingPhase Resolve(DateTimeOffset now)
    {
        _resolved = true;

        if (Phase == LoadingPhase.Skeleton && now - _shownAt < MinimumVisible) return Phase;

        Phase = LoadingPhase.Content;
        return Phase;
    }

    /// <summary>
    /// Moves the gate on to whatever the clock now permits. Called from a timer; the
    /// timer only needs to fire at <see cref="NextDue"/>.
    /// </summary>
    public LoadingPhase Advance(DateTimeOffset now)
    {
        switch (Phase)
        {
            case LoadingPhase.Waiting when _resolved:
                Phase = LoadingPhase.Content;
                break;

            case LoadingPhase.Waiting when now - _startedAt >= GiveUpAfter:
                Phase = LoadingPhase.Failed;
                break;

            case LoadingPhase.Waiting when now - _startedAt >= AppearAfter:
                Phase = LoadingPhase.Skeleton;
                _shownAt = now;
                break;

            case LoadingPhase.Skeleton when _resolved && now - _shownAt >= MinimumVisible:
                Phase = LoadingPhase.Content;
                break;

            // Giving up is measured from the request, not from when the skeleton
            // appeared: the user has been waiting the whole time either way.
            case LoadingPhase.Skeleton when !_resolved && now - _startedAt >= GiveUpAfter:
                Phase = LoadingPhase.Failed;
                break;

            case LoadingPhase.Failed when _resolved:
                Phase = LoadingPhase.Content;
                break;

            default:
                break;
        }

        return Phase;
    }

    /// <summary>
    /// When <see cref="Advance"/> would next change anything, or null if nothing is
    /// pending. Lets the caller arm one timer instead of polling.
    /// </summary>
    public DateTimeOffset? NextDue(DateTimeOffset now)
    {
        switch (Phase)
        {
            case LoadingPhase.Waiting when _resolved:
                return now;

            case LoadingPhase.Waiting:
                return Earlier(_startedAt + AppearAfter, _startedAt + GiveUpAfter);

            case LoadingPhase.Skeleton when _resolved:
                return _shownAt + MinimumVisible;

            case LoadingPhase.Skeleton:
                return _startedAt + GiveUpAfter;

            case LoadingPhase.Failed when _resolved:
                return now;

            default:
                return null;
        }
    }

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
}
