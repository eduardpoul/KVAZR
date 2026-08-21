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
    public string Title => "Идентика";

    public IReadOnlyList<IdentityState> States { get; } =
    [
        new(KvazrMarkState.AllConnected, "Всё подключено", "Планета в акцентном цвете, вращения нет"),
        new(KvazrMarkState.Searching, "Идёт поиск", "Связка вращается, планета приглушена"),
        new(KvazrMarkState.FirmwareUpdating, "Обновление прошивки", "Планета фиолетовая, орбита замыкается по проценту"),
        new(KvazrMarkState.NoDevices, "Нет устройств", "Весь знак уходит в серый"),
        new(KvazrMarkState.LowBattery, "Низкий заряд", "Планета янтарная, ядро без изменений"),
        new(KvazrMarkState.LinkError, "Ошибка связи", "Планета красная, вращение остановлено"),
    ];

    public IReadOnlyList<IdentityMotion> Motions { get; } =
    [
        new(KvazrMotion.Loading, "Загрузка", "2,8 с, линейно, бесконечно"),
        new(KvazrMotion.Fast, "Быстрая", "1,4 с · кнопки, короткие ответы"),
        new(KvazrMotion.Breathing, "Дыхание", "6 с · фон сплеша, простой"),
        new(KvazrMotion.Pulse, "Пульс", "Устройство отвечает, но ещё не готово"),
        new(KvazrMotion.Landing, "Посадка", "Два оборота с торможением, конец загрузки"),
        new(KvazrMotion.Fault, "Сбой", "Вращение стоит, планета красная, короткий сдвиг"),
    ];

    /// <summary>
    /// The determinate ring, at the same 65 % the identity draws it. A fixed number
    /// rather than an animation: the point of this control is that it is honest about
    /// a known total, and a sample that swept would be showing the one thing it is not.
    /// </summary>
    public double FirmwareProgress => 0.65;

    public string FirmwareCaption => "65% · не отключайте устройство";
}
