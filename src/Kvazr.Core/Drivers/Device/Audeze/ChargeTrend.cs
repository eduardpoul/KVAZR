namespace Kvazr.Core.Drivers.Audeze;

/// <summary>
/// Whether the headset is on a cable, inferred from where its battery reading is going.
///
/// Inferred because there is nothing else: no charging flag has been found anywhere in
/// the protocol, and a bolt drawn over a battery is the one thing on the panel that
/// nobody can check against the device — the headset has no light that says so either.
/// So this is a guess, and the whole of it is here, apart from the connection and away
/// from any hardware, because a guess that cannot be run against a made-up series of
/// readings is a guess nobody can correct.
///
/// The rules, each of them a thing that went wrong:
///
/// A rise has to be bigger than the jitter. The reading moves by one on its own — 92,
/// 93, 93 — and a single rising sample used to be enough, so noise lit the bolt.
///
/// A claim expires. It was only ever cleared by a fall, and at 93% a fall does not come
/// for hours: the bolt stayed lit long after the cable was out.
///
/// And expiry is against the clock, not against the next reading. That distinction did
/// not exist while a battery was read every thirty seconds no matter what; it does now,
/// because a headset that stops answering is not read at all, and a claim examined only
/// inside a reading simply froze — lit over a headset asleep in a drawer.
///
/// A gap is not a trend. When the readings stop and start again, the two either side
/// have an unknown time between them and possibly a cable, so the second one is a first
/// reading rather than a step. <see cref="Lost"/> says so.
/// </summary>
internal sealed class ChargeTrend
{
    /// <summary>
    /// How far the level has to climb before it counts as charging. The reading moves by
    /// one on its own, so one is noise and two is news.
    /// </summary>
    private const int RiseMeansCharging = 2;

    /// <summary>
    /// How long a claim survives without another rise confirming it. Long enough to cover
    /// the slow last percent of a nearly full battery, short enough that pulling the cable
    /// is noticed while the person is still looking at the panel.
    /// </summary>
    private static readonly TimeSpan Holds = TimeSpan.FromMinutes(6);

    private int _last = -1;
    private bool _charging;
    private DateTimeOffset _seenAt;

    /// <summary>
    /// Whether anything has been read at all. Nothing read means nothing to say — which
    /// is not the same as "not charging", and the panel draws the two differently.
    /// </summary>
    public bool HasReading => _last >= 0;

    /// <summary>A fresh percentage from the headset.</summary>
    public void Saw(int percent, DateTimeOffset now)
    {
        if (_last >= 0)
        {
            if (percent >= _last + RiseMeansCharging)
            {
                _charging = true;
                _seenAt = now;
            }
            else if (percent < _last)
            {
                _charging = false;
            }
        }

        _last = percent;
    }

    /// <summary>
    /// The headset stopped answering. Everything held here describes a series of readings
    /// and the series has ended, so it is dropped rather than carried across the gap.
    /// </summary>
    public void Lost()
    {
        _last = -1;
        _charging = false;
    }

    /// <summary>The claim, expired against the clock rather than against the next reading.</summary>
    public bool IsCharging(DateTimeOffset now)
    {
        if (_charging && now - _seenAt > Holds) _charging = false;

        return _charging;
    }
}
