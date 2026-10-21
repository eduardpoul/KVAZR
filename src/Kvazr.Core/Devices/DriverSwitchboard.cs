namespace Kvazr.Core.Devices;

/// <summary>
/// Which drivers and contributors are allowed to run.
///
/// Every device this application understands is understood at some tier — what Windows
/// already knows, a protocol shared by a class of hardware, or one vendor's own — and the
/// registry always keeps the deepest one that recognises a device. That is right for a
/// user and unhelpful for whoever is building the tier underneath: the only headset on
/// this desk is an Audeze, so the shared headset page could be guessed at and never seen
/// while the vendor driver was in the build.
///
/// There has been a <c>--no-vendor</c> flag for that since early on, and it worked by
/// leaving the module out of the composition root, which means it answers only at
/// startup and only all-or-nothing. This is the same idea made switchable one library at
/// a time and while the application is running, so a page can be compared against its own
/// baseline without a rebuild — which is the whole point, since the two are meant to
/// describe the same hardware.
///
/// Ids are the ones the system already uses on itself: <see cref="IDeviceDriver.DriverId"/>
/// and <see cref="ICapabilityContributor.ContributorId"/>, the same strings that appear in
/// the log lines and in <see cref="DeviceInfo.DriverId"/>. A debug surface that invented
/// friendlier names would be a second vocabulary to keep in step with the first.
/// </summary>
public sealed class DriverSwitchboard
{
    private readonly object _lock = new();

    private HashSet<string> _off = new(StringComparer.Ordinal);
    private int _version;

    /// <summary>Raised after any change, on the thread that made it.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Bumped by every change.
    ///
    /// The registry keeps the number it last swept with, which is what lets it notice
    /// that the answer to "who owns this device" may have changed without needing to be
    /// told twice or to subscribe to anything. A device found by a driver that has since
    /// been switched off must not linger as an offline row: the same hardware is about to
    /// come back under another driver's key, and two rows for one microphone is exactly
    /// the confusion this screen exists to remove.
    /// </summary>
    public int Version
    {
        get
        {
            lock (_lock) return _version;
        }
    }

    /// <summary>Whether a driver or contributor may run. Anything unknown is on.</summary>
    public bool IsOn(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_lock) return !_off.Contains(id);
    }

    /// <summary>What is currently switched off, for persisting.</summary>
    public IReadOnlyCollection<string> SwitchedOff
    {
        get
        {
            lock (_lock) return [.. _off];
        }
    }

    public void Set(string id, bool on)
    {
        ArgumentNullException.ThrowIfNull(id);

        lock (_lock)
        {
            bool changed = on ? _off.Remove(id) : _off.Add(id);
            if (!changed) return;

            _version++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the whole set, for restoring what was saved.</summary>
    public void Replace(IEnumerable<string> off)
    {
        ArgumentNullException.ThrowIfNull(off);

        var wanted = new HashSet<string>(off, StringComparer.Ordinal);

        lock (_lock)
        {
            if (wanted.SetEquals(_off)) return;

            _off = wanted;
            _version++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
