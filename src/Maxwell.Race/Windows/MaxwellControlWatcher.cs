using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Watches the controls on the headset itself — the volume wheel and the mute button —
/// and says the moment one is used.
///
/// These are ordinary HID controls on the consumer and telephony pages, quite separate
/// from the vendor protocol that carries the settings. They announce themselves
/// immediately and unprompted, which the settings channel never does: ask it nothing and
/// it says nothing, however long you wait.
///
/// That difference is the whole reason this exists. Without it the only way to notice a
/// wheel being turned is to keep asking what the volume is, and the answer arrives
/// whenever the next sweep happens to run — up to ten seconds after the wearer's hand has
/// moved. With it, the turn is known at once and the one reading it could have changed is
/// re-read straight away.
///
/// Read only. Nothing is ever written to these interfaces.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MaxwellControlWatcher : IDisposable
{
    /// <summary>Consumer controls: volume up and down, play, and the wheel that drives them.</summary>
    private const ushort ConsumerPage = 0x000C;

    /// <summary>Telephony: the mute button and the hook switch.</summary>
    private const ushort TelephonyPage = 0x000B;

    private readonly List<WindowsHidChannel> _channels = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _watchers = [];

    private bool _disposed;

    /// <summary>Raised on any activity, with what the reader saw.</summary>
    public event EventHandler<MaxwellControlEvent>? Activity;

    public MaxwellControlWatcher(IReadOnlyList<MaxwellInterface> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);

        foreach (MaxwellInterface face in interfaces)
        {
            if (face.UsagePage is not (ConsumerPage or TelephonyPage)) continue;

            WindowsHidChannel channel;
            try
            {
                channel = new WindowsHidChannel(face.Path, face.InputReportLength, face.OutputReportLength);
            }
            catch (Exception)
            {
                // Another application may hold it exclusively. Nothing here is essential —
                // the settings still poll — so a refused handle costs responsiveness and
                // not correctness.
                continue;
            }

            _channels.Add(channel);
            _watchers.Add(Task.Run(() => WatchAsync(face, channel, _stopping.Token)));
        }
    }

    /// <summary>True when at least one control interface could be opened.</summary>
    public bool IsWatching => _channels.Count > 0;

    private async Task WatchAsync(MaxwellInterface face, WindowsHidChannel channel, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? report;
            try
            {
                // A blocking read with its own timeout, so shutting down does not wait on
                // a wearer who is not touching anything.
                report = channel.Listen(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception)
            {
                return;
            }

            if (report is null || report.Length < 2) continue;

            // Byte 1 is the state of the buttons; zero is the release that follows every
            // press, and reporting it would double every event for no information.
            if (report[1] == 0) continue;

            Activity?.Invoke(this, new MaxwellControlEvent(face.UsagePage, report[1]));

            await Task.Yield();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopping.Cancel();

        // Disposing the channel cancels the read in flight; waiting first would hang for
        // as long as the timeout.
        foreach (WindowsHidChannel channel in _channels) channel.Dispose();

        _stopping.Dispose();
        _channels.Clear();
        _watchers.Clear();
    }
}

/// <summary>One press or turn, as the interface reported it.</summary>
/// <param name="UsagePage">Which set of controls it came from.</param>
/// <param name="State">The button bits, as seen. Zero is never raised.</param>
public readonly record struct MaxwellControlEvent(ushort UsagePage, byte State)
{
    /// <summary>True for the volume wheel and the buttons beside it.</summary>
    public bool IsVolume => UsagePage == 0x000C;

    /// <summary>True for the mute button.</summary>
    public bool IsMute => UsagePage == 0x000B;
}
