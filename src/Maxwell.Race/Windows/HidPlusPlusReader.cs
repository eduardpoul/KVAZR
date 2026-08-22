using System.Runtime.Versioning;
using System.Text;

using Maxwell.Race.Protocol;

namespace Maxwell.Race.Windows;

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

    /// <summary>A device that answered its slot: what it said about itself.</summary>
    public sealed record Paired(byte Slot, string Name, Kind? Type, int? BatteryPercent);

    /// <summary>
    /// How many devices the receiver says are connected right now, read off its short
    /// channel — HID++ 1.0 register 0x02, which the receiver answers instantly and
    /// unconditionally. This is what keeps discovery cheap: pinging an empty slot costs
    /// two wake timeouts, and a receiver with nothing on it says so in one exchange.
    /// </summary>
    public static int? ConnectedCount(WindowsHidChannel shortChannel)
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

    public static int? Battery(WindowsHidChannel channel, byte device)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // Two features mean the same thing to a person and neither is universal: newer
        // devices carry 0x1004, older ones 0x1000, so both are asked.
        if (Index(channel, device, HidPlusPlus.FeatureUnifiedBattery) is { } unified)
        {
            byte[]? reply = Ask(channel, device, unified, 0x01);
            if (reply is not null && !HidPlusPlus.IsError(reply)) return reply[4];
        }

        if (Index(channel, device, HidPlusPlus.FeatureBatteryLevel) is { } level)
        {
            byte[]? reply = Ask(channel, device, level, 0x00);
            if (reply is not null && !HidPlusPlus.IsError(reply)) return reply[4];
        }

        return null;
    }

    private static byte[]? Ping(WindowsHidChannel channel, byte device) =>
        channel.Request(
            HidPlusPlus.Ping(channel.OutputLength, device),
            reply => HidPlusPlus.Answers(reply, device, HidPlusPlus.RootIndex, 0x01),
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

        return name.Length > 0 ? name.ToString() : null;
    }

    private static Kind? Type(WindowsHidChannel channel, byte device)
    {
        if (Index(channel, device, HidPlusPlus.FeatureDeviceName) is not { } feature) return null;

        byte[]? reply = Ask(channel, device, feature, 0x02);
        if (reply is null || HidPlusPlus.IsError(reply)) return null;

        return reply[4] <= (byte)Kind.Receiver ? (Kind)reply[4] : null;
    }

    private static byte? Index(WindowsHidChannel channel, byte device, ushort feature)
    {
        byte[]? reply = channel.Request(
            HidPlusPlus.GetFeature(channel.OutputLength, device, feature),
            r => HidPlusPlus.Answers(r, device, HidPlusPlus.RootIndex, 0x00),
            Wait);

        if (reply is null || HidPlusPlus.IsError(reply) || reply[4] == 0) return null;

        return reply[4];
    }

    private static byte[]? Ask(
        WindowsHidChannel channel, byte device, byte feature, byte function, params byte[] parameters) =>
        channel.Request(
            HidPlusPlus.Request(channel.OutputLength, device, feature, function, parameters),
            reply => HidPlusPlus.Answers(reply, device, feature, function),
            Wait);
}
