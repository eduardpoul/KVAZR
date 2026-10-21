#if WINDOWS
using System.Runtime.Versioning;
using Kvazr.Core.Abstractions;
using Kvazr.Race.Windows;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Kvazr.App.Platform.Windows;

/// <summary>
/// Unpairs through whichever of Windows' two Bluetooth stacks the device is actually on.
///
/// They are parallel subsystems that share an address format and almost nothing else.
/// <c>BluetoothRemoveDevice</c> is the classic one; it removes a headset and it cannot
/// see a low-energy mouse at all — measured by listing what its own enumerator returns,
/// which on this desk was two devices, neither of them the mouse Windows was holding
/// connected at that moment. So the classic call is tried and its refusal is not the
/// answer: it is the signal to knock on the other door.
///
/// The other door is the pairing object on the device itself, which is a projection of
/// the modern API and works for both kinds — but only reachable from the Windows target,
/// which is why this lives here and not beside its classic counterpart.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class WindowsPairingRemoval : IPairingRemoval
{
    public async Task<PairingRemoval> ForgetAsync(string? address, CancellationToken cancellationToken = default)
    {
        if (WindowsBluetoothPairing.Parse(address) is not { } value) return PairingRemoval.NoPairing;

        // Classic first, because it is synchronous, cheap, and the case it covers —
        // headsets, speakers, the things with a battery worth watching — is the common
        // one here.
        if (WindowsBluetoothPairing.Forget(value)) return PairingRemoval.Removed;

        cancellationToken.ThrowIfCancellationRequested();

        return await LowEnergyAsync(value).ConfigureAwait(false);
    }

    private static async Task<PairingRemoval> LowEnergyAsync(ulong address)
    {
        BluetoothLEDevice? device;

        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            // The radio can be off, or the projection can refuse in a session with no
            // access to it. Neither is worth a crash: nothing was changed either way.
            return PairingRemoval.Refused;
        }

        if (device is null) return PairingRemoval.Refused;

        using (device)
        {
            DeviceInformationPairing pairing = device.DeviceInformation.Pairing;

            // Not paired to begin with is not a failure. What was asked for is that there
            // be no pairing afterwards, and there is not.
            if (!pairing.IsPaired) return PairingRemoval.Removed;

            DeviceUnpairingResult result = await pairing.UnpairAsync();

            return result.Status is DeviceUnpairingResultStatus.Unpaired
                or DeviceUnpairingResultStatus.AlreadyUnpaired
                ? PairingRemoval.Removed
                : PairingRemoval.Refused;
        }
    }
}
#endif
