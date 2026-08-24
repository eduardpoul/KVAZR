using Maxwell.Core.Devices;

namespace Maxwell.App.Localization;

/// <summary>
/// What a lead is called, in the language in force.
///
/// Keyed by the driver's own label rather than by the kind, which was the first attempt
/// and threw away the half that matters: a Logitech receiver and a Maxwell dongle are both
/// TransportKind.Dongle, and naming them both "USB dongle" loses the word "Lightspeed"
/// that tells the two apart on a desk carrying both.
///
/// Keying by text is the thing this catalogue avoids everywhere else, and the reason it is
/// right here is that a transport has no id to key by — and the failure mode is mild: a
/// driver that rewords a label loses its translation and shows the new words, which is
/// exactly what an untranslated label does anyway.
/// </summary>
public static class TransportNames
{
    public static string Of(TransportInfo transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return Named(transport.Label);
    }

    public static string Brief(TransportInfo transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return Named(transport.ShortLabel);
    }

    private static string Named(string label) =>
        Localizer.Current[$"transport.{label}", label];
}
