using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// The auto power-off timer, read raw.
///
/// Audeze HQ declares this one as <c>writeAutoPowerOff(unsigned short)</c> and reads the
/// answer as two bytes — low then high — while our own decoder takes the last payload
/// byte, which is what a one-byte feature needs. Before rewriting either side, this
/// prints what the headset actually sends back, so the two halves of the value can be
/// located in our own framing instead of guessed across theirs.
///
/// Read only. Nothing here writes.
/// </summary>
internal static class AutoOffProbe
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var factory = new WindowsHidTransportFactory();

        IReadOnlyList<MaxwellInterface> found = enumerator.EnumerateRaceInterfaces();
        if (found.Count == 0)
        {
            Console.WriteLine("No Audeze RACE interface found.");
            return 1;
        }

        MaxwellInterface target = found[0];
        using var session = new RaceSession(factory.Open(target));

        Console.WriteLine($"interface: PID 0x{target.ProductId:X4}  {target.Transport}");
        Console.WriteLine();

        // The documented read: 0x2C83, sub 0x01, in the short read-shaped form.
        byte[] body = [0x80, 0x05, 0x5A, 0x04, 0x00, 0x83, 0x2C, 0x01];

        IReadOnlyList<RacePacket> packets = await session
            .ProbeAsync(body, 500, cancellationToken)
            .ConfigureAwait(false);

        if (packets.Count == 0)
        {
            Console.WriteLine("No packets came back.");
            return 1;
        }

        foreach (RacePacket p in packets)
        {
            Console.WriteLine(
                $"  kind 0x{p.Kind:X2}  cmd 0x{p.Command:X4}  len {p.Payload.Length,2}  payload {p.Hex}");

            if (p.Command != 0x2C83 || p.Payload.Length < 4) continue;

            Console.WriteLine($"      our Value (last byte)      = {p.Value}");

            for (int i = 0; i + 1 < p.Payload.Length; i++)
            {
                int le = p.Payload[i] | (p.Payload[i + 1] << 8);
                Console.WriteLine($"      bytes[{i}],[{i + 1}] little-endian = {le}");
            }
        }

        int seconds = await session.ReadAutoPowerOffAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"  decoded timeout: {seconds} s ({seconds / 60.0:0.#} min)");

        // Round trip on real hardware, then put the original back. Nothing is left changed.
        int probe = seconds == 1800 ? 900 : 1800;

        Console.WriteLine();
        Console.WriteLine($"  writing {probe} s ...");
        bool wrote = await session.WriteAutoPowerOffAsync(probe, cancellationToken).ConfigureAwait(false);
        int after = await session.ReadAutoPowerOffAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  accepted={wrote}  read back {after} s");

        Console.WriteLine($"  restoring {seconds} s ...");
        bool back = await session.WriteAutoPowerOffAsync(seconds, cancellationToken).ConfigureAwait(false);
        int final = await session.ReadAutoPowerOffAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"  restored={back}  now {final} s");

        Console.WriteLine();
        Console.WriteLine(wrote && after == probe && final == seconds
            ? "RESULT: the long-form write works and round-trips."
            : "RESULT: still refused — the frame is not right yet.");

        return wrote ? 0 : 2;
    }
}
