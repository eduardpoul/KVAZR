using Maxwell.Core.Abstractions;
using Maxwell.Race.Abstractions;

namespace Maxwell.App.Platform;

/// <summary>
/// Stand-ins for a platform whose device layer has not been written.
///
/// They exist so the application starts and says "no device found" rather than failing
/// to resolve a service. That is the honest behaviour: the headset really is
/// unreachable there, and a startup crash would suggest a bug instead of a gap.
/// </summary>
internal sealed class UnsupportedHidEnumerator : IHidEnumerator
{
    public IReadOnlyList<MaxwellInterface> Enumerate() => [];

    /// <summary>No HID stack here at all, so nothing is found rather than something failing.</summary>

    public IReadOnlyList<HidInterface> EnumerateAll() => [];


    public IReadOnlyList<MaxwellInterface> EnumerateRaceInterfaces() => [];
}

internal sealed class UnsupportedHidTransportFactory : IHidTransportFactory
{
    public IHidTransport Open(MaxwellInterface iface) =>
        throw new PlatformNotSupportedException(
            "This platform has no HID transport yet; the headset cannot be reached.");
}

internal sealed class UnsupportedAutostart : IAutostart
{
    public bool IsEnabled => false;

    public bool StartsMinimised => false;

    public void Enable(bool startMinimised)
    {
    }

    public void Disable()
    {
    }
}

/// <summary>Global shortcuts are Windows-only so far; elsewhere nothing is bound.</summary>
internal sealed class NullHotkeyService : IHotkeyService
{
    public event EventHandler<string>? Triggered;

    public IReadOnlyList<string> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        // Reported as rejected rather than silently accepted, so the hotkeys screen
        // tells the truth about what is actually bound.
        _ = Triggered;
        return bindings.Select(b => b.ActionId).ToList();
    }

    public void Dispose()
    {
    }
}
