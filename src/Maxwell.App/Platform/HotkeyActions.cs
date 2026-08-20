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
        new(ChatMixUp, "Game / Chat Mix — в сторону чата", "Шаг 1 из 20"),
        new(ChatMixDown, "Game / Chat Mix — в сторону игры", "Шаг 1 из 20"),
        new(NoiseCycle, "Шумоподавление микрофона", "По кругу: выкл → низкое → высокое"),
        new(SidetoneToggle, "Сайдтон", "Включить или выключить"),
        new(PresetNext, "Следующий пресет эквалайзера", "По кругу из 10 слотов"),
        new(WindowShow, "Показать окно", "Из области уведомлений"),
    ];

    public static HotkeyAction? ById(string id) =>
        All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}
