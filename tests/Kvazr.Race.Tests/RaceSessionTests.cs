using Kvazr.Race.Abstractions;
using Kvazr.Race.Protocol;
using Xunit;

namespace Kvazr.Race.Tests;

/// <summary>
/// The exchange against reports replayed in the order the dongle gave them.
///
/// The codec tests pin each rule on its own; this one pins what the panel saw. A battery
/// read on 2026-10-20 returned 86% for a headset at 83%, and the charging bolt came on
/// whenever the true reading slipped through between two of the false ones.
/// </summary>
public sealed class RaceSessionTests
{
    private static readonly MaxwellInterface Dongle =
        new("dongle", 0x4B18, MaxwellInterface.RaceUsagePage, 0x01, 62, 62, "Maxwell");

    [Fact]
    public async Task BatteryOverTheDongle_IsTheFreshAnswerAndNotWhatLiesBehindTheLastOne()
    {
        // As captured: three replays of the status answer that came before, then the
        // battery reply with sixteen new bytes, then that reply replayed.
        var transport = new ScriptedTransport(Dongle,
            CapturedTraffic.DongleStatusReplay,
            CapturedTraffic.DongleStatusReplay,
            CapturedTraffic.DongleStatusReplay,
            CapturedTraffic.DongleStatusReplay,
            CapturedTraffic.DongleBatteryFresh,
            CapturedTraffic.DongleBatteryReplay);

        using var session = new RaceSession(transport);

        Assert.Equal(83, await session.ReadBatteryAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Hands out the reports in order, then keeps repeating the last.</summary>
    private sealed class ScriptedTransport(MaxwellInterface descriptor, params string[] reports) : IHidTransport
    {
        private int _next;

        public MaxwellInterface Descriptor => descriptor;

        public bool Write(byte[] report) => true;

        public byte[]? ReadInputReport(byte reportId) =>
            CapturedTraffic.Bytes(reports[Math.Min(_next++, reports.Length - 1)]);

        public void Dispose()
        {
        }
    }
}
