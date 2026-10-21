namespace Kvazr.Race.Abstractions;

/// <summary>
/// One reading of an Apple earphone's state, out of the beacon it broadcasts.
///
/// Every level is a nibble on the wire — eleven steps, nothing finer exists — so a
/// percentage here is a multiple of ten and null means the device said "unknown", which
/// it does for a pod that is away or a case whose lid is shut.
/// </summary>
/// <param name="ProductId">
/// What the beacon calls the model, and the one thing that ties it to a device Windows
/// knows. The Bluetooth address in a beacon is random and rotates, so it can tie nothing
/// to anything; the model bytes read the other way round are exactly the product id
/// Windows carries for the paired device.
/// </param>
public sealed record ProximityReading(
    ushort ProductId,
    int? Left,
    int? Right,
    int? Case,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    short SignalStrength,
    DateTimeOffset At)
{
    /// <summary>
    /// The one number for a device that has two batteries, which is the emptier of them.
    ///
    /// An average would say a comfortable seventy while one earphone is at forty and
    /// about to stop; the pair only lasts as long as its worse half.
    /// </summary>
    public int? Lowest =>
        new[] { Left, Right }.Where(p => p is not null).Select(p => p!.Value) is var levels && levels.Any()
            ? levels.Min()
            : null;

    public bool IsCharging => LeftCharging || RightCharging;
}

/// <summary>
/// What is in the air right now, from devices that broadcast their state without being
/// asked.
///
/// Listening is not free — it is a radio scanning — so this is one shared listener rather
/// than one per device, and it hands out the last thing it heard rather than promising to
/// hear on demand. Apple's earphones only beacon when they are being looked for: in the
/// case with the lid open, or freshly out of it. In ears with the case shut they say
/// nothing at all, measured. So a reading has an age, and what to do with an old one is
/// the caller's business.
/// </summary>
public interface IProximityListener
{
    /// <summary>The most recent reading for a model, or null if none has been heard.</summary>
    ProximityReading? Latest(ushort productId);
}
