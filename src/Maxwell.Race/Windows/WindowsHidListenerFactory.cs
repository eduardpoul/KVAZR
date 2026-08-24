using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Opens an ear on the first collection of a given vendor and usage page.
///
/// The first, not all of them: two identical microphones on one desk would need the
/// container id to tell their channels apart, and nothing here has one to hand. That is a
/// real limitation and it is written down rather than guarded against, because the guard
/// would be a guess at which of the two the caller meant.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidListenerFactory : IHidListenerFactory
{
    public IHidListener? Open(ushort vendorId, ushort usagePage)
    {
        HidInterface? found = new WindowsHidEnumerator().EnumerateAll()
            .FirstOrDefault(i => i.VendorId == vendorId && i.UsagePage == usagePage && i.InputReportLength > 1);

        return found is { } collection
            ? new WindowsHidListener(collection)
            : null;
    }

    private sealed class WindowsHidListener : IHidListener
    {
        private readonly WindowsHidChannel _channel;

        public WindowsHidListener(HidInterface collection) =>
            _channel = new WindowsHidChannel(
                collection.Path, collection.InputReportLength, collection.OutputReportLength);

        public byte[]? Listen(TimeSpan wait) => _channel.Listen(wait);

        public void Dispose() => _channel.Dispose();
    }
}
