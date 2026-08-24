namespace Maxwell.App.Platform;

/// <summary>One bindable global action.</summary>
public sealed record HotkeyAction(string Id, string Title, string Description);

/// <summary>
/// The actions a global shortcut can trigger. Ids are stable and stored in settings;
/// the titles are display-only and safe to reword.
/// </summary>
public static class HotkeyActions
{
    public const string ChatMixUp = "chatmix.up";
    public const string ChatMixDown = "chatmix.down";
    public const string NoiseCycle = "noise.cycle";
    public const string SidetoneToggle = "sidetone.toggle";
    public const string PresetNext = "preset.next";
    public const string WindowShow = "window.show";

    public static readonly IReadOnlyList<HotkeyAction> All =
    [
        new(ChatMixUp, Maxwell.App.Localization.Localizer.Current.Say("Game / Chat Mix — towards the chat"), Maxwell.App.Localization.Localizer.Current.Say("Step 1 of 20")),
        new(ChatMixDown, Maxwell.App.Localization.Localizer.Current.Say("Game / Chat Mix — towards the game"), Maxwell.App.Localization.Localizer.Current.Say("Step 1 of 20")),
        new(NoiseCycle, Maxwell.App.Localization.Localizer.Current.Say("Microphone noise reduction"), Maxwell.App.Localization.Localizer.Current.Say("Round: off → low → high")),
        new(SidetoneToggle, Maxwell.App.Localization.Localizer.Current.Say("Sidetone"), Maxwell.App.Localization.Localizer.Current.Say("Turn on or off")),
        new(PresetNext, Maxwell.App.Localization.Localizer.Current.Say("Next equaliser preset"), Maxwell.App.Localization.Localizer.Current.Say("Round the ten slots")),
        new(WindowShow, Maxwell.App.Localization.Localizer.Current.Say("Show the window"), Maxwell.App.Localization.Localizer.Current.Say("From the notification area")),
    ];

    public static HotkeyAction? ById(string id) =>
        All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}
