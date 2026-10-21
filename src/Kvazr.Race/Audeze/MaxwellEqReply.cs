namespace Kvazr.Race;

/// <summary>
/// What a slot read came back with. Kept apart from <see cref="MaxwellEqSlot"/> because
/// "the slot is empty" and "the headset never answered" are different facts, and a plain
/// null cannot tell them apart — which matters here, since an untouched headset answers
/// every user slot with a refusal and looks identical to a dead channel.
/// </summary>
/// <param name="Slot">The slot asked for, echoed back by the headset.</param>
/// <param name="Status">
/// Leading byte of the reply. Zero carries bands; anything else is a refusal, and
/// <c>0xFC</c> is the one an unwritten slot gives. Audeze HQ checks this same byte and
/// abandons the read when it is non-zero.
/// </param>
/// <param name="Bands">The decoded curve, or null for any non-zero status.</param>
public sealed record MaxwellEqReply(byte Slot, byte Status, MaxwellEqSlot? Bands)
{
    /// <summary>Status of a reply that carries a curve.</summary>
    public const byte Ok = 0x00;

    /// <summary>Status of a slot that has never been written.</summary>
    public const byte Empty = 0xFC;

    /// <summary>True when the headset answered but had nothing stored.</summary>
    public bool IsEmpty => Status != Ok;
}
