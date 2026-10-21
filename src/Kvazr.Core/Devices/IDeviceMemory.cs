namespace Kvazr.Core.Devices;

/// <summary>
/// Somewhere for a driver to keep what a device cannot.
///
/// Almost everything on a page is read from the hardware, and that is the right way round:
/// the device is the truth and this application is a window onto it. A few things are not
/// like that. A QuadCast's lighting has no home in the microphone at all — a frame holds
/// for a second and then the firmware takes the lights back, and no command stores one —
/// so a colour somebody chose exists nowhere unless this side writes it down.
///
/// Deliberately a string against a slot rather than anything shaped. What a driver keeps
/// is the driver's business, and a store that knew about lighting would need to learn
/// about the next thing too. Keyed by device so two of the same microphone do not share
/// one colour.
/// </summary>
public interface IDeviceMemory
{
    /// <summary>What was written here last, or null if nothing ever was.</summary>
    string? Recall(string deviceKey, string slot);

    /// <summary>Writes it down and persists it. Cheap enough to call on every change.</summary>
    void Remember(string deviceKey, string slot, string value);
}
