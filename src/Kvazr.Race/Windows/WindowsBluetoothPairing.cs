using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kvazr.Race.Windows;

/// <summary>
/// Removes a Bluetooth pairing.
///
/// The only genuinely destructive thing this application can do to a device, and the only
/// one it cannot undo: putting the device back means holding its button until it flashes
/// and pairing it again, exactly as on the first day. Everything above this calls it
/// behind a confirmation for that reason.
///
/// Deliberately not tested against real hardware, and it cannot be: the only way to try it
/// is to unpair somebody's headset. It is written to be obviously correct instead — one
/// call, one argument, and the address parsed from the form Windows itself reported.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsBluetoothPairing
{
    /// <summary>
    /// Parses the address Windows reports on a device node. It arrives in several shapes
    /// depending on which property it came from — "6C:29:95:1B:AE:07", "6c2995 1bae07",
    /// or bare hex — so the separators are dropped rather than matched.
    /// </summary>
    public static ulong? Parse(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        Span<char> digits = stackalloc char[12];
        int count = 0;

        foreach (char c in address)
        {
            if (!Uri.IsHexDigit(c)) continue;
            if (count == 12) return null;

            digits[count++] = c;
        }

        if (count != 12) return null;

        return ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value)
            ? value
            : null;
    }

    /// <summary>
    /// Removes the pairing. True when Windows says it is gone.
    ///
    /// A device that was not paired to begin with also returns false, and the two are not
    /// worth telling apart: either way there is no pairing afterwards, which is what was
    /// asked for.
    /// </summary>
    public static bool Forget(ulong address)
    {
        ulong local = address;
        return Native.BluetoothRemoveDevice(ref local) == 0;
    }

    private static class Native
    {
        /// <summary>
        /// Takes the address by reference even though it does not change it, which is the
        /// shape of the published signature and not a mistake here.
        /// </summary>
        [DllImport("bthprops.cpl", SetLastError = true)]
        internal static extern uint BluetoothRemoveDevice(ref ulong address);
    }
}
