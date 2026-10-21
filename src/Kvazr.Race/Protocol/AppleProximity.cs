using Kvazr.Race.Abstractions;

namespace Kvazr.Race.Protocol;

/// <summary>
/// Apple's proximity-pairing beacon, read out.
///
/// This is how a pair of AirPods says how it is: not by answering, but by broadcasting a
/// few bytes into the air every second or so, for Apple's own devices to notice. There is
/// nothing to connect to — the accessory protocol they publish runs over L2CAP, and
/// Windows gives user-mode sockets RFCOMM only — so hearing it is the whole conversation.
///
/// Every field below was settled against a pair on the desk in three states, because two
/// published write-ups disagree with each other about byte 7 and neither is right about
/// everything. Where they disagreed, the device decided.
/// </summary>
public static class AppleProximity
{
    /// <summary>Apple, as the Bluetooth registry numbers manufacturers.</summary>
    public const ushort Company = 0x004C;

    /// <summary>The message type that carries the state. Apple sends several others.</summary>
    private const byte ProximityPairing = 0x07;

    /// <summary>A nibble of fifteen is the device saying it does not know.</summary>
    private const int Unknown = 0x0F;

    /// <summary>
    /// The bit of the status byte that says which physical earphone the low position
    /// refers to — the low nibble of the battery byte, and the low bit of the flags.
    ///
    /// Established at the desk, because the write-ups disagree. With the left earphone
    /// charging in the case and the right one out, two frames arrived within seconds of
    /// each other: status 0x71 with the low charging bit set, and status 0x11 with the
    /// high one. Read with this bit as the swap, both say the left is charging — which is
    /// what was true. Read without it, they contradict each other.
    /// </summary>
    private const byte LeftIsLow = 0x20;

    /// <summary>
    /// One beacon, or null for anything that is not an earphone saying how it is.
    ///
    /// <code>
    /// 07 19 01 13 20 55 AA B1 …
    /// │  │  │  └──┬──┘ │  │  └── flags in the high nibble, case level in the low
    /// │  │  │     │    │  └───── an earphone in each nibble
    /// │  │  │     │    └──────── status, whose 0x20 bit says which nibble is the left one
    /// │  │  │     └───────────── model, and the same bytes reversed are the product id
    /// │  │  │                    Windows carries for the paired device
    /// │  │  └─────────────────── 1 once paired, 0 while pairing
    /// │  └────────────────────── length of the rest
    /// └───────────────────────── the message type
    /// </code>
    ///
    /// The three states that settled byte 7: in ears with the case shut it read 0x8F —
    /// nothing charging, case unknown; both in the case with the lid open, 0xB1 — both
    /// earphones charging, the case itself not on a cable, and the case at ten per cent;
    /// one earphone out, 0x91 and 0xA1 — one charging, one not. A reading that puts the
    /// case in the high nibble makes the first of those a case at eighty per cent with
    /// every charging flag set at once, which is not what was on the desk.
    /// </summary>
    public static ProximityReading? Read(ReadOnlySpan<byte> payload, short signalStrength, DateTimeOffset at)
    {
        if (payload.Length < 8 || payload[0] != ProximityPairing) return null;

        // Reversed deliberately. Windows lists these earphones under 004C:2013 and the
        // beacon spells the same number 13 20. That is the only thing tying a beacon to a
        // device: a beacon carries no serial, and its Bluetooth address is random and
        // rotates, so it can point at nothing.
        ushort product = (ushort)((payload[4] << 8) | payload[3]);

        bool leftIsLow = (payload[5] & LeftIsLow) != 0;

        int low = payload[6] & 0x0F;
        int high = payload[6] >> 4;
        int flags = payload[7] >> 4;

        return new ProximityReading(
            ProductId: product,
            Left: Level(leftIsLow ? low : high),
            Right: Level(leftIsLow ? high : low),
            Case: Level(payload[7] & 0x0F),
            LeftCharging: (flags & (leftIsLow ? 0x1 : 0x2)) != 0,
            RightCharging: (flags & (leftIsLow ? 0x2 : 0x1)) != 0,
            CaseCharging: (flags & 0x4) != 0,
            SignalStrength: signalStrength,
            At: at);
    }

    /// <summary>
    /// A nibble as a percentage, or null where the device said it does not know.
    ///
    /// Elevenths of the way, and no finer number exists on the wire: ten is a hundred per
    /// cent and nine is ninety. Other readers show ninety-five for that nine — the middle
    /// of the ten it stands for — which is a fair guess and still a guess. This reports
    /// what was said.
    /// </summary>
    private static int? Level(int nibble) => nibble is Unknown or > 10 ? null : nibble * 10;
}
