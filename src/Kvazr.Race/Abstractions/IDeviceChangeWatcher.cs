namespace Kvazr.Race.Abstractions;

/// <summary>
/// Tells the application when the machine's set of devices changed — something plugged
/// in, something unplugged, a Bluetooth device connected or gone.
///
/// The alternative is asking, and asking has to pick a number: ask often and the machine
/// is woken constantly for an answer that is almost always "nothing"; ask rarely and a
/// headset switched on sits unnoticed for as long as the interval. The operating system
/// already knows the moment it happens, so the honest arrangement is to be told and to
/// keep a slow sweep only as a safety net.
///
/// Implementations raise <see cref="Changed"/> from whatever thread the platform uses;
/// consumers must not assume the UI thread.
/// </summary>
public interface IDeviceChangeWatcher : IDisposable
{
    /// <summary>
    /// Something about the machine's devices changed. Deliberately carries no detail:
    /// the registry's own discovery is what decides what that meant, and a watcher that
    /// tried to say which device would be a second, competing source of truth.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>Begins watching. Safe to call more than once.</summary>
    void Start();
}

/// <summary>
/// Watches nothing, for the platforms whose notification API is not written yet. The
/// slow discovery sweep still finds everything; it simply finds it later.
/// </summary>
public sealed class NullDeviceChangeWatcher : IDeviceChangeWatcher
{
    public static readonly NullDeviceChangeWatcher Instance = new();

    private NullDeviceChangeWatcher()
    {
    }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void Dispose()
    {
    }
}
