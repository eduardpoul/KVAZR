namespace Maxwell.Core.Settings;

/// <summary>
/// An equaliser curve the wearer drew and gave a name to.
///
/// Kept by this application rather than by the headset, which is a trade made with the
/// owner of the hardware and worth writing down. The headset has four user slots and no
/// room for a name in any of them; a file here has neither limit, and writing a slot is
/// one of the few commands in the protocol notes that has never been tried on live
/// hardware. The cost is that these curves are ours: the headset knows nothing about
/// them, so the official application will not show them and they are applied by this one
/// sending the ten bands, exactly as a drag does.
///
/// The band count is stored with the curve because it is what says whether a saved curve
/// belongs to the device now in front of us. A ten-band curve means nothing to a five-band
/// equaliser, and offering it would be offering to break the sound.
/// </summary>
public sealed class EqCurvePreset
{
    public string Name { get; set; } = string.Empty;

    /// <summary>One gain per band, in the device's own band order.</summary>
    public List<int> Bands { get; set; } = [];
}
