using Kvazr.Core.Devices;

namespace Kvazr.App.Web;

/// <summary>
/// Which coloured Fluent icon stands for a device that has no photograph.
///
/// Class first, then whatever the name gives away for free: an Xbox-layout pad and a
/// Switch pad are both Gamepad to the enumerator, and the name is the only place the
/// difference exists. Guessing from names is fine here precisely because the fallback
/// is the plain icon for the class — a wrong guess is one flavour of the right picture.
/// </summary>
public static class DeviceIcon
{
    public static string KeyFor(DeviceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        string name = $"{info.Vendor} {info.Model}".ToUpperInvariant();

        return info.Class switch
        {
            DeviceClass.Headset => "headset",
            DeviceClass.Earbuds when Has(name, "CASE", Kvazr.App.Localization.Localizer.Current.Say("CASE")) => "earbuds-case",
            DeviceClass.Earbuds => "earbuds",
            DeviceClass.Speaker when Has(name, "SOUNDBAR") => "soundbar",
            DeviceClass.Speaker => "speaker",
            DeviceClass.Microphone when Has(name, "QUADCAST", "YETI", "NT-", "STUDIO") => "mic-studio",
            DeviceClass.Microphone => "mic",
            DeviceClass.Mouse when Has(name, "VERTICAL", "MX VERTICAL") => "mouse-ergo",
            DeviceClass.Mouse when Has(name, "GAMING", "SUPERLIGHT", "VIPER", "DEATHADDER", "G PRO") => "mouse-gaming",
            DeviceClass.Mouse => "mouse",
            DeviceClass.Keyboard when Has(name, "NUMPAD", Kvazr.App.Localization.Localizer.Current.Say("DIGITAL")) => "numpad",
            DeviceClass.Keyboard when Has(name, "60%", "MINI") => "keyboard-60",
            DeviceClass.Keyboard when Has(name, "65", "75", "AIR75", "AIR60", "COMPACT") => "keyboard-compact",
            DeviceClass.Keyboard when Has(name, "TKL", "87") => "keyboard-tkl",
            DeviceClass.Keyboard => "keyboard-full",
            DeviceClass.Gamepad when Has(name, "DUALSENSE", "DUALSHOCK", "PLAYSTATION") => "gamepad-ps",
            DeviceClass.Gamepad when Has(name, "SWITCH", "JOY-CON") => "gamepad-switch",
            DeviceClass.Gamepad when Has(name, "XBOX", "FLYDIGI", "APEX") => "gamepad-xbox",
            DeviceClass.Gamepad => "gamepad-generic",
            DeviceClass.Receiver when Has(name, "DONGLE", Kvazr.App.Localization.Localizer.Current.Say("DONGLE")) => "dongle",
            DeviceClass.Receiver => "receiver",
            _ when Has(name, "DOCK", "CHARGER", Kvazr.App.Localization.Localizer.Current.Say("DOCK")) => "dock",
            _ when Has(name, "WEBCAM", "CAMERA", Kvazr.App.Localization.Localizer.Current.Say("CAMERA")) => "webcam",
            _ when Has(name, "WATCH", Kvazr.App.Localization.Localizer.Current.Say("WATCH")) => "watch",
            _ => "unknown",
        };
    }

    private static bool Has(string name, params string[] probes) =>
        probes.Any(p => name.Contains(p, StringComparison.Ordinal));
}
