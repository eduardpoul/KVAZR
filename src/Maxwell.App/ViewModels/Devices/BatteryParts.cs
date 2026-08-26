using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>One half of a device that has more than one battery.</summary>
/// <param name="Id">The capability it came from, which is how the case is told from an earphone.</param>
/// <param name="Label">"Left", "Right", "Case" — the device's own word, translated.</param>
public sealed record BatteryPart(string Id, string Label, int Percent, bool Charging)
{
    /// <summary>
    /// The name cut to one letter, for the tray panel, whose row is one line high and
    /// already carrying a name, a status and a picture.
    ///
    /// Taken from the label rather than kept as a second list, so it follows the language
    /// the rest of the panel is in.
    /// </summary>
    public string Initial => Label.Length > 0 ? Label[..1] : string.Empty;

    /// <summary>The number as it is drawn, which is the only place a percent sign belongs.</summary>
    public string Reading => $"{Percent}%";
}

/// <summary>
/// The halves of a device whose battery is not one battery.
///
/// A pair of earphones has three, and the single number the hero draws is the emptiest of
/// them — true, useful, and not what somebody looking at earphones wants to know. They
/// want which one is about to run out, and whether the case can still refill it. So the
/// halves are drawn as one line beside the device, "Left 100%  Right 100%  Case 10%", the
/// way every application that shows them at all has settled on drawing it.
///
/// Empty for everything else, which is nearly every device: a headset with one cell has
/// nothing to lay out and gets the number it already had.
/// </summary>
internal static class BatteryParts
{
    /// <summary>The halves, in the order they are drawn, or empty for an ordinary battery.</summary>
    private static readonly (string Level, string Charging)[] Halves =
    [
        (CapabilityIds.PowerBatteryLeft, CapabilityIds.PowerChargingLeft),
        (CapabilityIds.PowerBatteryRight, CapabilityIds.PowerChargingRight),
        (CapabilityIds.PowerBatteryCase, CapabilityIds.PowerChargingCase),
    ];

    public static IReadOnlyList<BatteryPart> Of(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var parts = new List<BatteryPart>();

        foreach ((string level, string charging) in Halves)
        {
            if (snapshot.Get(level) is not { } state) continue;

            // A half that has not been heard from is left out rather than shown as a
            // question mark: an earphone out of its case simply says nothing, and a row
            // of dashes beside two real numbers reads as a fault.
            if (state.Value is not NumberValue percent) continue;

            parts.Add(new BatteryPart(
                level,
                Maxwell.App.Localization.Localizer.Current.Say(state.Descriptor.Title),
                percent.Value,
                snapshot.Get(charging)?.Value.AsFlag() ?? false));
        }

        return parts;
    }
}
