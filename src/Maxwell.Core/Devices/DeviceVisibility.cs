namespace Maxwell.Core.Devices;

/// <summary>
/// Whether a device the machine can see should be shown to the person using it.
///
/// Hiding is not forgetting, and the difference is the whole reason this exists. A
/// hidden device keeps its pairing in Windows, keeps whatever the operating system knows
/// about it, and comes back the moment it is unhidden — nothing is unpaired and nothing
/// is deleted. What changes is that it stops appearing on the dashboard, in the tray, in
/// the flyout and in notifications, and stops being polled, because a device somebody
/// has deliberately put away should not be costing its own battery to report a level
/// nobody is reading.
///
/// Pure, so the rules can be checked without a device, a registry or a settings file.
/// </summary>
public static class DeviceVisibility
{
    /// <summary>
    /// True when a device should be kept out of every surface.
    ///
    /// The second rule is the one worth explaining. A machine with a few Bluetooth
    /// oddments paired — a car stereo, a neighbour's speaker, something that answers
    /// nothing — shows them all on first launch, and a list where three of eight rows
    /// are anonymous addresses teaches the user to distrust the list. Sweeping those
    /// aside by default is only defensible because it is reversible in one click and the
    /// setting says plainly what it does.
    /// </summary>
    public static bool IsHidden(DeviceInfo device, IReadOnlySet<string> hidden, bool hideSilent)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(hidden);

        if (hidden.Contains(device.Key)) return true;

        return hideSilent && IsSilent(device);
    }

    /// <summary>
    /// A device that told us neither what it is called nor what it is. Its own name is
    /// its address, which is the system driver's way of saying it has nothing.
    /// </summary>
    public static bool IsSilent(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.Class == DeviceClass.Unknown && LooksLikeAnAddress(device.Model);
    }

    /// <summary>
    /// Six pairs of hex digits, however the system chose to punctuate them. Windows
    /// formats Bluetooth addresses differently depending on which property they came
    /// from, so the separators are counted out rather than matched.
    /// </summary>
    private static bool LooksLikeAnAddress(string name)
    {
        int digits = 0;

        foreach (char c in name)
        {
            if (Uri.IsHexDigit(c)) digits++;
            else if (c is not (':' or '-' or ' ')) return false;
        }

        return digits == 12;
    }
}
