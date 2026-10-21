namespace Kvazr.Race.Protocol;

/// <summary>
/// What happened to a coefficient transfer.
///
/// A bool was not enough here, and the shortfall cost a broken-sounding headset: three
/// transfers in a row reported failure and there was no way to tell whether the reports
/// had left the host at all. The two cases need different fixes and one of them leaves
/// the device holding half a filter, so they are worth naming.
/// </summary>
public readonly record struct EqApplyResult(bool Accepted, int SentChunks, int TotalChunks)
{
    /// <summary>The headset echoed the command back.</summary>
    public static EqApplyResult Applied => new(true, 0, 0);

    /// <summary>Everything went out and nothing came back.</summary>
    public static EqApplyResult Silent => new(false, 0, 0);

    /// <summary>
    /// The host refused a chunk part-way through, so the device is holding an incomplete
    /// coefficient set — which is worse than nothing, because it will still be played.
    /// </summary>
    public static EqApplyResult ChunkRefused(int at, int total) => new(false, at, total);

    public bool Truncated => !Accepted && TotalChunks > 0;

    public override string ToString() => this switch
    {
        { Accepted: true } => "accepted",
        { Truncated: true } => $"write refused at chunk {SentChunks + 1} of {TotalChunks} — filter left incomplete",
        _ => "sent, no acknowledgement",
    };
}
