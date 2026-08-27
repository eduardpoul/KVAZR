using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Finds a collection by usage and opens it for asking, on Windows.
///
/// Thin on purpose: the channel underneath already serialises requests and already knows
/// that a reply has to be recognised rather than assumed. All this adds is the search.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidReportPipeFactory : IHidReportPipeFactory
{
    public IHidReportPipe? Open(ushort vendorId, ushort productId, ushort usagePage, byte usage)
    {
        HidInterface? found = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i =>
                i.VendorId == vendorId
                && i.ProductId == productId
                && i.UsagePage == usagePage
                && i.Usage == usage);

        if (found is null) return null;

        return new Pipe(new WindowsHidChannel(found.Path, found.InputReportLength, found.OutputReportLength),
            found.OutputReportLength);
    }

    private sealed class Pipe(WindowsHidChannel channel, int reportLength) : IHidReportPipe
    {
        public int ReportLength => reportLength;

        public byte[]? Request(byte[] report, Func<byte[], bool> accepts, TimeSpan within) =>
            channel.Request(report, accepts, within);

        public void Dispose() => channel.Dispose();
    }
}
