using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.Branding;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>One row of the state table: what it is called and what it means.</summary>
internal sealed record IdentityState(KvazrMarkState State, string Name, string Hint);

/// <summary>One row of the motion table.</summary>
internal sealed record IdentityMotion(KvazrMotion Motion, string Name, string Hint);

/// <summary>
/// The identity sheet, in the running application.
///
/// It exists for the same reason the tray icon can be dumped to a PNG: a mark, a
/// rotation and a pulsing placeholder cannot be reviewed by reading the code that
/// draws them, and reviewing them in a design canvas proves only that the canvas is
/// right. Sitting them in the shell means they are reviewed as the user meets them —
/// on this theme, at this DPI, next to this typeface.
/// </summary>
internal sealed partial class IdentityViewModel : ObservableObject
{
    public string Title => Maxwell.App.Localization.Localizer.Current["page.identity", "Идентика"];

    public IReadOnlyList<IdentityState> States { get; } =
    [
        new(KvazrMarkState.AllConnected, Maxwell.App.Localization.Localizer.Current.Say("Всё подключено"), Maxwell.App.Localization.Localizer.Current.Say("Планета в акцентном цвете, вращения нет")),
        new(KvazrMarkState.Searching, Maxwell.App.Localization.Localizer.Current.Say("Идёт поиск"), Maxwell.App.Localization.Localizer.Current.Say("Связка вращается, планета приглушена")),
        new(KvazrMarkState.FirmwareUpdating, Maxwell.App.Localization.Localizer.Current.Say("Обновление прошивки"), Maxwell.App.Localization.Localizer.Current.Say("Планета фиолетовая, орбита замыкается по проценту")),
        new(KvazrMarkState.NoDevices, Maxwell.App.Localization.Localizer.Current.Say("Нет устройств"), Maxwell.App.Localization.Localizer.Current.Say("Весь знак уходит в серый")),
        new(KvazrMarkState.LowBattery, Maxwell.App.Localization.Localizer.Current.Say("Низкий заряд"), Maxwell.App.Localization.Localizer.Current.Say("Планета янтарная, ядро без изменений")),
        new(KvazrMarkState.LinkError, Maxwell.App.Localization.Localizer.Current.Say("Ошибка связи"), Maxwell.App.Localization.Localizer.Current.Say("Планета красная, вращение остановлено")),
    ];

    public IReadOnlyList<IdentityMotion> Motions { get; } =
    [
        new(KvazrMotion.Loading, Maxwell.App.Localization.Localizer.Current.Say("Загрузка"), Maxwell.App.Localization.Localizer.Current.Say("2,8 с, линейно, бесконечно")),
        new(KvazrMotion.Fast, Maxwell.App.Localization.Localizer.Current.Say("Быстрая"), Maxwell.App.Localization.Localizer.Current.Say("1,4 с · кнопки, короткие ответы")),
        new(KvazrMotion.Breathing, Maxwell.App.Localization.Localizer.Current.Say("Дыхание"), Maxwell.App.Localization.Localizer.Current.Say("6 с · фон сплеша, простой")),
        new(KvazrMotion.Pulse, Maxwell.App.Localization.Localizer.Current.Say("Пульс"), Maxwell.App.Localization.Localizer.Current.Say("Устройство отвечает, но ещё не готово")),
        new(KvazrMotion.Landing, Maxwell.App.Localization.Localizer.Current.Say("Посадка"), Maxwell.App.Localization.Localizer.Current.Say("Два оборота с торможением, конец загрузки")),
        new(KvazrMotion.Fault, Maxwell.App.Localization.Localizer.Current.Say("Сбой"), Maxwell.App.Localization.Localizer.Current.Say("Вращение стоит, планета красная, короткий сдвиг")),
    ];

    /// <summary>
    /// The determinate ring, at the same 65 % the identity draws it. A fixed number
    /// rather than an animation: the point of this control is that it is honest about
    /// a known total, and a sample that swept would be showing the one thing it is not.
    /// </summary>
    public double FirmwareProgress => 0.65;

    public string FirmwareCaption => Maxwell.App.Localization.Localizer.Current.Say("65% · не отключайте устройство");
}
