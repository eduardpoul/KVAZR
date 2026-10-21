namespace Kvazr.Core.Abstractions;

/// <summary>What happened when a pairing was asked to go away.</summary>
public enum PairingRemoval
{
    /// <summary>
    /// There was no pairing to remove. A device on a dongle is attached by having its
    /// receiver plugged in and by nothing else, so "forget" can only mean taking its row
    /// away — which is a different thing from failing to unpair it.
    /// </summary>
    NoPairing,

    /// <summary>Gone. The device will have to be paired again as on the first day.</summary>
    Removed,

    /// <summary>
    /// The system would not. Nothing has changed, and saying otherwise — by taking the
    /// row away, as this used to — answers a request to unpair by pretending to have
    /// done it.
    /// </summary>
    Refused,
}

/// <summary>
/// Removes a Bluetooth pairing, whichever of the two Bluetooth stacks the device is on.
///
/// An interface rather than a static call because it is genuinely two implementations on
/// one platform. Windows keeps classic Bluetooth and Bluetooth Low Energy in parallel
/// subsystems that share almost nothing, and the call that unpairs a headset does not see
/// a low-energy mouse at all — measured on this desk, by listing what it does see: two
/// devices, neither of them the mouse Windows was holding connected at the time.
/// </summary>
public interface IPairingRemoval
{
    /// <summary>
    /// Removes the pairing for an address in whatever shape a device node reported it —
    /// "6C:29:95:1B:AE:07", "6c2995 1bae07", bare hex — or says there was nothing to
    /// remove when there is no address at all.
    /// </summary>
    Task<PairingRemoval> ForgetAsync(string? address, CancellationToken cancellationToken = default);
}

/// <summary>
/// Removes nothing, for the platforms where this application cannot. Honest about it:
/// an address it was given and could not act on is a refusal, not a success.
/// </summary>
public sealed class UnsupportedPairingRemoval : IPairingRemoval
{
    public static readonly UnsupportedPairingRemoval Instance = new();

    public Task<PairingRemoval> ForgetAsync(string? address, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(address) ? PairingRemoval.NoPairing : PairingRemoval.Refused);
}
