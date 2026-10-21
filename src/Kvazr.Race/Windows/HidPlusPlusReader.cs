using System.Runtime.Versioning;
using System.Text;

using Kvazr.Race.Protocol;

namespace Kvazr.Race.Windows;

/// <summary>
/// The read half of HID++ over an open channel: who is in a pairing slot, what it is,
/// and how charged. Shared by the CLI probe and the driver so the protocol lessons live
/// once — most expensively, that the first ping to a sleeping device is also what wakes
/// it, and its answer can take most of two seconds to swim back.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HidPlusPlusReader
{
    /// <summary>Patience for the wake-up ping. Everything after it is brisk.</summary>
    public static readonly TimeSpan WakeWait = TimeSpan.FromMilliseconds(1800);

    public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(600);

    /// <summary>Device kinds as feature 0x0005 numbers them.</summary>
    public enum Kind
    {
        Keyboard = 0,
        Remote = 1,
        Numpad = 2,
        Mouse = 3,
        Touchpad = 4,
        Trackball = 5,
        Presenter = 6,
        Receiver = 7,
    }

    /// <summary>A battery reading: how full, and whether power is flowing in.</summary>
    public sealed record Charge(int Percent, bool IsCharging);

    /// <summary>A device that answered its slot: what it said about itself.</summary>
    public sealed record Paired(byte Slot, string Name, Kind? Type, Charge? Battery);

    /// <summary>
    /// A pairing as the receiver's own table records it, whether or not the device is on
    /// the air. The serial is the device's unit id, the number it gives for itself over
    /// its own cable too.
    /// </summary>
    public sealed record Remembered(byte Slot, string Serial, Kind? Type, string? Name);

    /// <summary>
    /// The index a device answers on when it is plugged in directly rather than sitting
    /// behind a receiver: the wired mouse is its own only device.
    /// </summary>
    public const byte DirectDevice = 0xFF;

    private const byte GetLongRegister = 0x83;
    private const byte LegacyError = 0x8F;
    private const byte ReceiverInfo = 0xB5;

    // Sub-registers of 0xB5, one of each per slot, counted from slot one at zero.
    private const byte PairingInfo = 0x20;
    private const byte ExtendedPairingInfo = 0x30;
    private const byte PairedName = 0x40;

    /// <summary>One turn on each path while an answer may come on either.</summary>
    private static readonly TimeSpan Turn = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// How many devices are paired to the receiver, read off its short channel — HID++
    /// 1.0 register 0x02, which the receiver answers instantly and unconditionally. This
    /// is what keeps discovery cheap: pinging an empty slot costs two wake timeouts, and
    /// a receiver with nothing paired says so in one exchange.
    ///
    /// Paired, not on the air. This was read as "connected right now" until a Lightspeed
    /// receiver answered one with its only mouse charging on a cable and silent on the
    /// radio.
    /// </summary>
    public static int? PairedCount(WindowsHidChannel shortChannel)
    {
        ArgumentNullException.ThrowIfNull(shortChannel);

        byte[] request = new byte[shortChannel.OutputLength];
        request[0] = HidPlusPlus.ShortReport;
        request[1] = HidPlusPlus.Receiver;
        request[2] = 0x81;
        request[3] = 0x02;

        byte[]? reply = shortChannel.Request(
            request,
            r => r.Length >= 6 && r[1] == HidPlusPlus.Receiver && (r[2] == 0x81 || HidPlusPlus.IsError(r)),
            Wait);

        if (reply is null || HidPlusPlus.IsError(reply)) return null;

        return reply[5];
    }

    /// <summary>
    /// Asks one slot, waking the device if the first ping catches it asleep. Null when
    /// the slot is empty or whatever is paired there is off and stays off.
    /// </summary>
    public static Paired? Interrogate(WindowsHidChannel channel, byte slot)
    {
        ArgumentNullException.ThrowIfNull(channel);

        byte[]? pong = Ping(channel, slot) ?? Ping(channel, slot);
        if (pong is null || !HidPlusPlus.Pong(pong)) return null;

        string? name = Name(channel, slot);
        if (name is null) return null;

        return new Paired(slot, name, Type(channel, slot), Battery(channel, slot));
    }

    /// <summary>
    /// Who the receiver says is paired to it, from its own table: HID++ 1.0 register 0xB5,
    /// three sub-registers per slot. Empty when the receiver keeps no such table, or keeps
    /// it without serials — Bolt receivers file pairings elsewhere — and the caller falls
    /// back to pinging slots.
    ///
    /// Asked of the receiver rather than of the devices, so it costs no wake-up and still
    /// answers with the mouse switched off or charging on its cable. That is the point of
    /// it. Measured on a Lightspeed receiver with the mouse on its cable and silent on the
    /// radio: the table still named it, and named it by the very unit id the mouse gives
    /// over the cable — which is what lets one mouse be one device by either route, and the
    /// receiver be filed under it rather than shown on its own.
    ///
    /// Two paths, because on Windows the short and long reports are separate collections.
    /// The register is long, so it is asked with a short report and answered with a long
    /// one: the question goes out on the short path and the answer arrives on the long
    /// one, while a refusal is short and comes back where the question went. The long
    /// channel has to be open before the first question — a handle only hears what
    /// arrives while it exists.
    /// </summary>
    public static IReadOnlyList<Remembered> Pairings(WindowsHidChannel shortChannel, WindowsHidChannel longChannel)
    {
        ArgumentNullException.ThrowIfNull(shortChannel);
        ArgumentNullException.ThrowIfNull(longChannel);

        var table = new List<Remembered>();

        for (byte slot = 1; slot <= 6; slot++)
        {
            byte n = (byte)(slot - 1);

            // Extended pairing: the serial at 5–8. Without one there is nothing to know the
            // device by, so the slot is not worth the other two questions.
            if (Register(shortChannel, longChannel, (byte)(ExtendedPairingInfo + n)) is not { } extended) continue;

            string serial = Convert.ToHexString(extended, 5, 4);
            if (serial is "00000000" or "FFFFFFFF") continue;

            // Pairing: the device kind in the low nibble of 11. Name: its length at 5,
            // the characters after it.
            byte[]? pairing = Register(shortChannel, longChannel, (byte)(PairingInfo + n));
            byte[]? named = Register(shortChannel, longChannel, (byte)(PairedName + n));

            table.Add(new Remembered(
                slot,
                serial,
                pairing is null ? null : RegisteredKind(pairing[11] & 0x0F),
                named is null ? null : RegisteredName(named)));
        }

        return table;
    }

    /// <summary>
    /// The device's unit id, from feature 0x0003: the same four bytes wherever it is
    /// asked — over its own cable or through a receiver's slot — and the number the
    /// receiver's pairing table files it under.
    /// </summary>
    public static string? UnitId(WindowsHidChannel channel, byte device)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Index(channel, device, HidPlusPlus.FeatureDeviceInformation) is not { } feature) return null;

        byte[]? reply = Ask(channel, device, feature, 0x00);

        // The entity count, then four bytes of unit id.
        if (reply is not { Length: > 8 } || HidPlusPlus.IsError(reply)) return null;

        string unit = Convert.ToHexString(reply, 5, 4);

        return unit is "00000000" or "FFFFFFFF" ? null : unit;
    }

    /// <summary>One sub-register of 0xB5: the answer, or null for a refusal or silence.</summary>
    private static byte[]? Register(WindowsHidChannel shortChannel, WindowsHidChannel longChannel, byte sub)
    {
        byte[] question = new byte[shortChannel.OutputLength];

        question[0] = HidPlusPlus.ShortReport;
        question[1] = HidPlusPlus.Receiver;
        question[2] = GetLongRegister;
        question[3] = ReceiverInfo;
        question[4] = sub;

        if (!shortChannel.Send(question)) return null;

        DateTime deadline = DateTime.UtcNow + Wait;

        while (DateTime.UtcNow < deadline)
        {
            if (longChannel.Listen(Turn) is { } heard
                && heard.Length >= 17
                && heard[0] == HidPlusPlus.LongReport
                && heard[1] == HidPlusPlus.Receiver
                && heard[2] == GetLongRegister
                && heard[3] == ReceiverInfo
                && heard[4] == sub)
            {
                return heard;
            }

            // A refusal does not repeat the sub-register, so it is taken as the answer to
            // whatever was asked last. The receiver refuses at once, which is what keeps
            // that from going stale.
            if (shortChannel.Listen(Turn) is { } said
                && said.Length >= 5
                && said[1] == HidPlusPlus.Receiver
                && said[2] == LegacyError
                && said[3] == GetLongRegister
                && said[4] == ReceiverInfo)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The receiver's device kinds, which are numbered differently from feature 0x0005's.
    /// Only the two that are certain: a mouse was read off a live table as 2, and a
    /// keyboard is the 1 below it. Anything else is left for the device to say when it is
    /// on the air.
    /// </summary>
    private static Kind? RegisteredKind(int kind) => kind switch
    {
        0x01 => Kind.Keyboard,
        0x02 => Kind.Mouse,
        _ => null,
    };

    private static string? RegisteredName(byte[] reply)
    {
        int length = Math.Min(reply[5], reply.Length - 6);
        if (length < 3) return null;

        string read = string.Concat(reply.Skip(6).Take(length).Select(b => (char)b));

        return read.All(c => c is >= ' ' and <= '~') ? read : null;
    }

    public static Charge? Battery(WindowsHidChannel channel, byte device)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // Two features mean the same thing to a person and neither is universal: newer
        // devices carry 0x1004, older ones 0x1000, so both are asked.
        if (Index(channel, device, HidPlusPlus.FeatureUnifiedBattery) is { } unified)
        {
            byte[]? reply = Ask(channel, device, unified, 0x01);

            // p0 = state of charge %, p2 = status: 0 discharging, 1 charging, and the
            // richer charging states (nearly full, full) still mean the cable is in.
            if (reply is not null && !HidPlusPlus.IsError(reply))
                return new Charge(reply[4], reply[6] is >= 1 and <= 3);
        }

        if (Index(channel, device, HidPlusPlus.FeatureBatteryLevel) is { } level)
        {
            byte[]? reply = Ask(channel, device, level, 0x00);

            // p0 = level %, p2 = status: 1 recharging, 2 charge in final stage,
            // 3 charge complete — all three are "the cable is in".
            if (reply is not null && !HidPlusPlus.IsError(reply))
                return new Charge(reply[4], reply[6] is >= 1 and <= 3);
        }

        return null;
    }

    private static byte[]? Ping(WindowsHidChannel channel, byte device) =>
        channel.Request(
            HidPlusPlus.Ping(channel.OutputLength, device, channel.SoftwareId),
            reply => HidPlusPlus.Answers(reply, device, HidPlusPlus.RootIndex, 0x01, channel.SoftwareId),
            WakeWait);

    private static string? Name(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, HidPlusPlus.FeatureDeviceName) is not { } feature) return null;

        // Length first: the name comes back sixteen bytes at a time and the last chunk
        // is padded, so reading without knowing where to stop appends buffer leftovers.
        byte[]? count = Ask(channel, device, feature, 0x00);
        if (count is null || HidPlusPlus.IsError(count)) return null;

        int length = count[4];
        var name = new StringBuilder();

        for (byte offset = 0; offset < length; offset += 16)
        {
            byte[]? chunk = Ask(channel, device, feature, 0x01, offset);
            if (chunk is null || HidPlusPlus.IsError(chunk)) break;

            for (int i = 4; i < chunk.Length && name.Length < length; i++)
            {
                if (chunk[i] == 0) break;
                name.Append((char)chunk[i]);
            }
        }

        // And it has to look like a name. This is a net under the software id above
        // rather than a second cure: nothing of ours can take another reader's answer any
        // more, but another program signing its requests the way this channel happens to
        // is not something we can prevent — and what arrives then is not a name, it is
        // some other reading spelt out as characters. A model name is printable text; a
        // byte below a space never is.
        string read = name.ToString();

        return read.Length >= 3 && read.All(c => c is >= ' ' and <= '~') ? read : null;
    }

    private static Kind? Type(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, HidPlusPlus.FeatureDeviceName) is not { } feature) return null;

        byte[]? reply = Ask(channel, device, feature, 0x02);
        if (reply is null || HidPlusPlus.IsError(reply)) return null;

        return reply[4] <= (byte)Kind.Receiver ? (Kind)reply[4] : null;
    }

    /// <summary>
    /// Where a feature sits on this device, or null if it does not have one.
    ///
    /// Public because a feature table is the whole point of the protocol: anything that
    /// wants to read a device's resolution has to ask the device where its resolution
    /// feature is first, and the answer differs between two mice of the same age.
    /// </summary>
    public static byte? FeatureIndex(WindowsHidChannel channel, byte device, ushort feature) =>
        Index(channel, device, feature);

    /// <summary>One question to one feature. Read-only by discipline rather than by type.</summary>
    public static byte[]? Ask(
        WindowsHidChannel channel, byte device, byte feature, byte function, params byte[] parameters) =>
        Put(channel, device, feature, function, parameters);

    private static byte? Index(WindowsHidChannel channel, byte device, ushort feature)
    {
        byte[]? reply = channel.Request(
            HidPlusPlus.GetFeature(channel.OutputLength, device, feature, channel.SoftwareId),
            r => HidPlusPlus.Answers(r, device, HidPlusPlus.RootIndex, 0x00, channel.SoftwareId),
            Wait);

        if (reply is null || HidPlusPlus.IsError(reply) || reply[4] == 0) return null;

        return reply[4];
    }

    private static byte[]? Put(
        WindowsHidChannel channel, byte device, byte feature, byte function, params byte[] parameters) =>
        channel.Request(
            HidPlusPlus.Request(channel.OutputLength, device, feature, function, channel.SoftwareId, parameters),
            reply => HidPlusPlus.Answers(reply, device, feature, function, channel.SoftwareId),
            Wait);
}
