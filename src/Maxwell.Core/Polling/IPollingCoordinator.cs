namespace Maxwell.Core.Polling;

/// <summary>
/// How the interface tells the device layer what it is looking at.
///
/// Without this the scheduler has to guess, and guessing means polling every device at
/// the foreground rate all day for numbers nobody is reading. The shell reports what is
/// on screen; the policy decides what that costs.
/// </summary>
public interface IPollingCoordinator
{
    PollingAttention Attention { get; }

    /// <summary>The device whose page is open, when one is.</summary>
    string? FocusedDeviceKey { get; }

    /// <summary>
    /// Declares what is on screen. Takes effect immediately: raising attention wakes the
    /// scheduler rather than waiting out the current interval, so opening the flyout
    /// does not show a minute-old percentage.
    /// </summary>
    void SetAttention(PollingAttention attention, string? focusedDeviceKey = null);

    /// <summary>
    /// Asks for one device to be read on the next pass. A full read covers every
    /// capability and is what a page opening needs; the timer never does it on its own.
    /// </summary>
    void RequestRefresh(string deviceKey, bool full = true);
}
