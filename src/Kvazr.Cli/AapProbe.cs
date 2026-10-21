using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Kvazr.Cli;

/// <summary>
/// Talks to AirPods the way Windows already knows how: over the Apple Accessory Protocol.
///
/// The battery was expected to arrive in Apple's proximity-pairing advertisement, and the
/// probe for that heard nothing at all — not one advertisement from any device, with the
/// case open and the pods connected. What the machine does have is plainer: enumerating
/// the pods' own service nodes shows one called <c>AAP Server</c>, on Apple's service
/// UUID, sitting on the classic Bluetooth link that is already up for the audio.
///
/// So this connects to it. Windows resolves the service to its channel itself when the
/// address is given a service class id, which is why no channel number appears anywhere
/// here — asking for one would be assuming what SDP is for.
///
/// Three packets go out, in the order Apple's own devices send them: a handshake, then
/// "tell me everything you can", then "and keep telling me". They turn notifications on
/// and change no setting. Their bytes are from librepods' protocol notes rather than from
/// memory — a wrong byte here goes into somebody's earphones.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AapProbe
{
    /// <summary>Apple's accessory service, as the pods publish it over SDP.</summary>
    private static readonly Guid AppleAccessory = new("74EC2172-0BAD-4D01-8F77-997B2BE0722A");

    private static readonly byte[] Handshake =
        [0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    /// <summary>"Everything you have", which is what makes the pods volunteer their state.</summary>
    private static readonly byte[] Everything =
        [0x04, 0x00, 0x04, 0x00, 0x4D, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] KeepTelling =
        [0x04, 0x00, 0x04, 0x00, 0x0F, 0x00, 0xFF, 0xFF, 0xFE, 0xFF];

    /// <summary>The head of a notification, whatever it turns out to be about.</summary>
    private static readonly byte[] Notification = [0x04, 0x00, 0x04, 0x00];

    private const byte Battery = 0x04;

    public static int Run(string address, int seconds, bool quiet)
    {
        if (!ulong.TryParse(address, System.Globalization.NumberStyles.HexNumber, null, out ulong target))
        {
            Console.WriteLine($"Not a Bluetooth address: {address}. Give twelve hex digits, e.g. 6093160A369F.");
            return 1;
        }

        Console.WriteLine($"[aap] {address}, слушаю {seconds} с");
        Console.WriteLine();

        // AF_BTH is 32 and BTHPROTO_RFCOMM is 3; neither has a name in .NET, and both are
        // as stable as any other number in the Windows headers.
        using var socket = new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)3);

        var where = new BluetoothService(target, AppleAccessory);
        SocketAddress raw = where.Serialize();
        Console.WriteLine($"  sockaddr {raw.Size} байт: {Convert.ToHexString([.. Enumerable.Range(0, raw.Size).Select(i => raw[i])])}");

        try
        {
            socket.Connect(where);
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"  не подключилось: {ex.SocketErrorCode} ({ex.NativeErrorCode}) {ex.Message}");

            Native(target, AppleAccessory);

            Console.WriteLine();
            Console.WriteLine("  Наушники должны быть подключены к этому компьютеру, а не к телефону.");
            return 1;
        }

        Console.WriteLine("  соединение открыто");

        foreach ((byte[] packet, string what) in
                 new (byte[], string)[] { (Handshake, "рукопожатие"), (Everything, "всё, что умеешь"), (KeepTelling, "и продолжай") })
        {
            socket.Send(packet);
            Console.WriteLine($"  → {what,-16} {Convert.ToHexString(packet)}");
            Thread.Sleep(150);
        }

        Console.WriteLine();

        socket.ReceiveTimeout = 1000;

        byte[] buffer = new byte[1024];
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        int packets = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (DateTime.UtcNow < until)
        {
            int read;

            try
            {
                read = socket.Receive(buffer);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                continue;
            }

            if (read <= 0) break;

            packets++;

            byte[] packet = buffer[..read];
            string hex = Convert.ToHexString(packet);

            string? reading = Reading(packet);

            // Quiet keeps one line per distinct packet; the pods repeat themselves every
            // few seconds and a wall of identical hex hides the one line that changed.
            if (quiet && !seen.Add(hex + reading)) continue;

            Console.WriteLine($"  ← {hex}");
            if (reading is not null) Console.WriteLine($"     {reading}");
        }

        Console.WriteLine();
        Console.WriteLine(packets == 0
            ? "  Ни одного пакета. Соединение есть, разговора нет."
            : $"  {packets} пакетов.");

        return packets > 0 ? 0 : 1;
    }

    /// <summary>
    /// What a packet says, where it is one this understands. Null otherwise, and the hex
    /// above it still stands — an unrecognised packet is a thing to look at, not to hide.
    /// </summary>
    private static string? Reading(byte[] p)
    {
        if (p.Length < 7 || !p.AsSpan(0, 4).SequenceEqual(Notification)) return null;
        if (p[4] != Battery) return null;

        int parts = p[6];
        var said = new List<string>();

        // Five bytes each: which part, a constant, the percentage, what it is doing, and
        // another constant.
        for (int i = 0; i < parts; i++)
        {
            int at = 7 + (i * 5);
            if (at + 3 >= p.Length) break;

            string part = p[at] switch
            {
                0x02 => "правый",
                0x04 => "левый",
                0x08 => "кейс",
                _ => $"0x{p[at]:X2}",
            };

            string doing = p[at + 3] switch
            {
                0x01 => " ⚡",
                0x02 => string.Empty,
                0x04 => " (нет)",
                _ => " ?",
            };

            said.Add($"{part} {p[at + 2]}%{doing}");
        }

        return said.Count > 0 ? "заряд: " + string.Join(", ", said) : null;
    }



    /// <summary>
    /// Every shape of the question at once, because one refusal says nothing.
    ///
    /// Windows answered "that address is not valid in its context" and no more, and that
    /// one sentence covers a wrong struct layout, a service the device does not publish,
    /// and a Bluetooth address the stack will not route to. They are told apart by asking
    /// several ways and seeing which answers differ: a layout fault refuses everything, a
    /// service fault refuses only its own row.
    /// </summary>
    public static int Matrix(ulong address)
    {
        (string name, Guid id)[] services =
        [
            ("AAP", AppleAccessory),
            ("Handsfree", new Guid("0000111E-0000-1000-8000-00805F9B34FB")),
            ("Serial port", new Guid("00001101-0000-1000-8000-00805F9B34FB")),
            ("AVRCP", new Guid("0000110E-0000-1000-8000-00805F9B34FB")),
        ];

        // Winsock, started explicitly. The .NET Socket does it as a side effect, and the
        // first run of this without one answered 10093 — "not initialised" — for every
        // row, which looks exactly like a device refusing everything.
        byte[] started = new byte[512];
        int wsa = WSAStartup(0x0202, started);

        Console.WriteLine($"[aap] пробую {address:X12} всеми способами   (WSAStartup {wsa})");
        Console.WriteLine();

        foreach ((string name, Guid id) in services)
        {
            foreach ((string shape, byte[] sockaddr) in
                     new (string, byte[])[] { ("упакован, 30", Packed(address, id)), ("выровнен, 40", Aligned(address, id)) })
            {
                Console.WriteLine($"  {name,-12} {shape,-14} {Attempt(sockaddr)}");
            }
        }

        // And a channel number instead of a service, which is the other way to name an
        // endpoint. One to five covers where the common services sit.
        Console.WriteLine();

        for (uint channel = 1; channel <= 5; channel++)
        {
            byte[] sockaddr = Packed(address, Guid.Empty);
            BitConverter.GetBytes(channel).CopyTo(sockaddr, 26);

            Console.WriteLine($"  канал {channel,-6} {"упакован, 30",-14} {Attempt(sockaddr)}");
        }

        return 0;
    }

    private static byte[] Packed(ulong address, Guid service)
    {
        byte[] name = new byte[30];
        BitConverter.GetBytes((ushort)32).CopyTo(name, 0);
        BitConverter.GetBytes(address).CopyTo(name, 2);
        service.ToByteArray().CopyTo(name, 10);
        return name;
    }

    private static byte[] Aligned(ulong address, Guid service)
    {
        byte[] name = new byte[40];
        BitConverter.GetBytes((ushort)32).CopyTo(name, 0);
        BitConverter.GetBytes(address).CopyTo(name, 8);
        service.ToByteArray().CopyTo(name, 16);
        return name;
    }

    private static string Attempt(byte[] sockaddr)
    {
        nint handle = socket(32, 1, 3);
        if (handle == -1) return $"socket() отказал, WSA {WSAGetLastError()}";

        try
        {
            return connect(handle, sockaddr, sockaddr.Length) == 0
                ? "открылось"
                : $"WSA {WSAGetLastError()}";
        }
        finally
        {
            _ = closesocket(handle);
        }
    }

    /// <summary>
    /// The same connection attempt, made without .NET in the way.
    ///
    /// Worth the duplication once: a refusal that comes back through a framework can be
    /// the framework's own opinion, and the two have to be told apart before anything is
    /// concluded about the device. Winsock is already started by the time this runs — the
    /// Socket above did it.
    /// </summary>
    private static int Native(ulong address, Guid service)
    {
        const int AfBth = 32;
        const int SockStream = 1;
        const int BthProtoRfcomm = 3;

        nint handle = socket(AfBth, SockStream, BthProtoRfcomm);

        if (handle == -1)
        {
            Console.WriteLine($"  [native] socket() отказал, WSA {WSAGetLastError()}");
            return -1;
        }

        try
        {
            byte[] name = new byte[30];
            BitConverter.GetBytes((ushort)AfBth).CopyTo(name, 0);
            BitConverter.GetBytes(address).CopyTo(name, 2);
            service.ToByteArray().CopyTo(name, 10);

            int result = connect(handle, name, name.Length);

            Console.WriteLine(result == 0
                ? "  [native] connect() прошёл"
                : $"  [native] connect() отказал, WSA {WSAGetLastError()}");

            return result;
        }
        finally
        {
            _ = closesocket(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("ws2_32.dll", SetLastError = true)]
    private static extern nint socket(int af, int type, int protocol);

    [System.Runtime.InteropServices.DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int connect(nint s, byte[] name, int namelen);

    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int closesocket(nint s);

    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int WSAGetLastError();

    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int WSAStartup(ushort version, byte[] data);

    /// <summary>
    /// A Bluetooth device and the service wanted on it, in the shape Windows expects.
    ///
    /// SOCKADDR_BTH: the family, the address, the service class id, and a channel. Packed
    /// to the byte — ws2bth.h says so, and laying it out the way the compiler would have
    /// aligned it puts the address eight bytes in instead of two, which Windows answers
    /// with "that address is not valid in its context" and no further hint.
    ///
    /// The channel is left at zero on purpose: given a service class id, Windows looks the
    /// channel up over SDP, which is the whole point of a device publishing one.
    /// </summary>
    private sealed class BluetoothService(ulong address, Guid service) : EndPoint
    {
        private const int Size = 30;

        public override AddressFamily AddressFamily => (AddressFamily)32;

        public override SocketAddress Serialize()
        {
            var socketAddress = new SocketAddress(AddressFamily, Size);

            byte[] bytes = BitConverter.GetBytes(address);
            for (int i = 0; i < 8; i++) socketAddress[2 + i] = bytes[i];

            byte[] guid = service.ToByteArray();
            for (int i = 0; i < 16; i++) socketAddress[10 + i] = guid[i];

            return socketAddress;
        }

        public override EndPoint Create(SocketAddress socketAddress) => this;

        public override string ToString() => $"{address:X12}/{service:B}";
    }
}
