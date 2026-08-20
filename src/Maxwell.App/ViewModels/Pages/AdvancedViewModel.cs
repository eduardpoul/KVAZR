using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels.Pages;

public sealed partial class AdvancedViewModel : PageViewModel
{
    public override string Title => "Дополнительно";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool Busy { get; set; }

    /// <summary>
    /// Destructive actions arm on the first press and only fire on the second.
    /// A single misplaced click must not be able to wipe the headset.
    /// </summary>
    [ObservableProperty]
    public partial bool ResetArmed { get; set; }

    [ObservableProperty]
    public partial bool PairingArmed { get; set; }

    public AdvancedViewModel(IDeviceService device) : base(device)
    {
    }

    protected override void OnStateApplied(DeviceState state)
    {
        OnPropertyChanged(nameof(FirmwareText));

        // Losing the device disarms everything: the confirmation was for a device that
        // is no longer the one in front of us.
        if (!state.CanControl)
        {
            ResetArmed = false;
            PairingArmed = false;
        }
    }

    public string FirmwareText => State.FirmwareVersion is { } version
        ? string.Create(CultureInfo.InvariantCulture, $"Установлена {version}")
        : "Версия не прочитана";

    public string ResetButtonText => ResetArmed ? "Точно сбросить?" : "Сбросить";

    public string PairingButtonText => PairingArmed ? "Точно разорвать пару?" : "Сопрячь заново";

    partial void OnResetArmedChanged(bool value) => OnPropertyChanged(nameof(ResetButtonText));

    partial void OnPairingArmedChanged(bool value) => OnPropertyChanged(nameof(PairingButtonText));

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        Busy = true;
        Status = "Проверяю подключение…";

        try
        {
            await Device.DiscoverAsync().ConfigureAwait(true);
            await Device.RefreshAllAsync().ConfigureAwait(true);

            DeviceState state = Device.State;

            Status = state.Connection switch
            {
                ConnectionState.NotFound => "Устройство не найдено.",
                ConnectionState.MonitorOnly =>
                    "Найден только донгл. Он передаёт звук, но не отвечает на команды настройки — это его штатное поведение, а не поломка.",
                _ => string.Create(CultureInfo.InvariantCulture,
                    $"Кабель на связи. Заряд {state.BatteryPercent}%, прошивка {state.FirmwareVersion ?? "—"}, ChatMix {state.ChatMix}."),
            };
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task FactoryResetAsync()
    {
        if (!State.CanControl)
        {
            Status = "Нужен кабель — по донглу команда не пройдёт.";
            return;
        }

        if (!ResetArmed)
        {
            ResetArmed = true;
            Status = "Сброс сотрёт пресеты, баланс чашек и уровни громкости, включая исправленный баланс кастомной прошивки. Отменить нельзя. Нажмите ещё раз, чтобы подтвердить.";
            return;
        }

        ResetArmed = false;
        Busy = true;

        try
        {
            bool sent = await Device.FactoryResetAsync(confirmed: true).ConfigureAwait(true);
            Status = sent
                ? "Команда отправлена. Наушники перезагрузятся — подключите их заново."
                : "Отправить не удалось.";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task StartPairingAsync()
    {
        if (!State.CanControl)
        {
            Status = "Нужен кабель — по донглу команда не пройдёт.";
            return;
        }

        if (!PairingArmed)
        {
            PairingArmed = true;
            Status = "Текущая пара с донглом будет разорвана, и до нового сопряжения звук по воздуху работать не будет. Нажмите ещё раз, чтобы подтвердить.";
            return;
        }

        PairingArmed = false;
        Busy = true;

        try
        {
            bool sent = await Device.StartPairingAsync(confirmed: true).ConfigureAwait(true);
            Status = sent
                ? "Команда отправлена. Наушники перешли в режим сопряжения."
                : "Отправить не удалось.";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void CancelArming()
    {
        ResetArmed = false;
        PairingArmed = false;
        Status = "Отменено.";
    }
}
