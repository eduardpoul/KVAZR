using Maxwell.Core.Catalogue;
using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.System;
using Maxwell.Race.Windows;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maxwell.Cli;

/// <summary>
/// Writes a curve through the same path the window uses — registry, session, driver —
/// rather than straight down the wire.
///
/// The distinction is the point. Pushing coefficients from a raw session is known to work
/// and says nothing about whether the application's own write reaches the device: between
/// the two sit the capability's declared access, the session's write gate and the driver's
/// dispatch, and a curve that springs back under the wearer's finger looks identical
/// whether it was refused at any of them or never sent at all.
/// </summary>
internal static class EqWriteProbe
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        IDeviceDriver[] drivers =
        [
            new AudezeRaceDriver(
                new WindowsHidEnumerator(),
                new WindowsHidTransportFactory(),
                new WindowsBluetoothMonitor()),
            new SystemDeviceDriver(new WindowsSystemDeviceEnumerator(), new DeviceCatalogue()),
        ];

        using var registry = new DeviceRegistry(drivers, new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);
        await registry.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        IDeviceSession? session = registry.Devices
            .Where(d => d.Capabilities.ContainsKey(CapabilityIds.AudioEqGraphic))
            .Select(d => registry.Session(d.Info.Key))
            .FirstOrDefault(s => s is not null);

        if (session is null)
        {
            Console.WriteLine("No device declaring an equaliser curve.");
            return 1;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await session.RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"full read of every capability : {clock.ElapsedMilliseconds} ms");

        CapabilityState state = session.Snapshot.Capabilities[CapabilityIds.AudioEqGraphic];
        Console.WriteLine($"capability: access {state.Descriptor.Access}, writable {state.CanWrite}");
        Console.WriteLine($"reading now: {Describe(state.Value)}");

        int[] wanted = [-12, -12, -12, -12, -12, -12, -12, -12, -12, -12];
        Console.WriteLine($"writing:     {string.Join(" ", wanted)}");

        clock.Restart();
        bool wrote = await session.WriteAsync(
            CapabilityIds.AudioEqGraphic,
            CapabilityValue.Vector([.. wanted.Select(CapabilityValue.Of)]),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"writing the curve             : {clock.ElapsedMilliseconds} ms   accepted={wrote}");

        clock.Restart();
        await session.RefreshAsync([CapabilityIds.AudioEqGraphic], cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"reading the curve back        : {clock.ElapsedMilliseconds} ms");

        clock.Restart();
        await session.WriteAsync(
            CapabilityIds.AudioEqPreset, CapabilityValue.Choice(6), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"switching preset              : {clock.ElapsedMilliseconds} ms");

        clock.Restart();
        await session.RefreshAsync([CapabilityIds.AudioEqGraphic], cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"reading the curve after it    : {clock.ElapsedMilliseconds} ms");
        Console.WriteLine($"reading back: {Describe(session.Snapshot.Capabilities[CapabilityIds.AudioEqGraphic].Value)}");

        Console.WriteLine();
        Console.WriteLine(wrote
            ? "The write went through. If the curve still springs back in the window, the"
              + " reading that follows it is what overwrites it."
            : "The write was refused before it reached the wire.");

        return wrote ? 0 : 2;
    }

    private static string Describe(CapabilityValue value) =>
        value is VectorValue v
            ? string.Join(" ", v.Items.Select(i => i is NumberValue n ? n.Value.ToString() : "—"))
            : "—";
}
