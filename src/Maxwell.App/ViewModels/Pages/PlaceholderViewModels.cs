using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// Pages whose content depends on work that is not done yet. They render an honest
/// "not built yet" panel rather than a convincing but dead mock-up, so nobody mistakes
/// a placeholder for a broken feature.
/// </summary>
public abstract partial class PlaceholderViewModel : PageViewModel
{
    protected PlaceholderViewModel(IDeviceService device) : base(device)
    {
    }

    public abstract string Explanation { get; }
}

public sealed class AdvancedViewModel : PlaceholderViewModel
{
    public override string Title => "Дополнительно";

    public override string Explanation =>
        "Обновление прошивки, сопряжение и сброс к заводским настройкам. " +
        "Раздел появится последним: это единственные операции, которые способны " +
        "стереть настройки наушников, и подключать их до остальных экранов незачем.";

    public AdvancedViewModel(IDeviceService device) : base(device)
    {
    }
}

public sealed class ProfilesViewModel : PlaceholderViewModel
{
    public override string Title => "Профили";

    public override bool RequiresControl => false;

    public override string Explanation =>
        "Наборы настроек с именами и автопереключение по запущенной игре. " +
        "Хранится на стороне приложения, устройство нужно только в момент применения.";

    public ProfilesViewModel(IDeviceService device) : base(device)
    {
    }
}
