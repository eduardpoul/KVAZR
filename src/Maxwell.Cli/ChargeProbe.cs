using Maxwell.Race.Abstractions;
using Maxwell.Race.Protocol;
using Maxwell.Race.Windows;

namespace Maxwell.Cli;

/// <summary>
/// Looks for a real charging flag.
///
/// Today the application does not read one — it infers it, by watching whether the
/// battery percentage went up since the last poll. That guess sticks: unplug a headset
/// sitting at 93% and the level does not fall for a long while, so "charging" stays on
/// screen long after the cable is out. This prints the readings the device actually
/// gives, so the flag can be found instead of guessed: run it on the cable, run it off
/// the cable, and compare.
/// </summary>
internal static class ChargeProbe
{
    public static async Task<int> RunAsync(int samples, CancellationToken cancellationToken = default)
    {
        var enumerator = new WindowsHidEnumerator();
        var factory = new WindowsHidTransportFactory();

        IReadOnlyList<MaxwellInterface> found = enumerator.EnumerateRaceInterfaces();
        MaxwellInterface? target = found.Count > 0 ? found[0] : null;

        if (target is null)
        {
            Console.WriteLine("No Audeze RACE interface found.");
            return 1;
        }

        using var session = new RaceSession(factory.Open(target));

        Console.WriteLine($"interface: PID 0x{target.ProductId:X4}  {target.Transport}");
        Console.WriteLine();

        for (int i = 0; i < samples; i++)
        {
            int battery = await session.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
            byte[]? status = await session.ReadStatusAsync(cancellationToken).ConfigureAwait(false);

            string bytes = status is null
                ? "(no answer)"
                : string.Join(' ', status.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

            Console.WriteLine($"  battery {battery,3}%   status 0x0910: {bytes}");

            if (i < samples - 1) await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine("Run this on the charger and off it; whichever byte or bit differs is the flag.");

        return 0;
    }
}
