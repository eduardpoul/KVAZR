using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels.Pages;

public sealed partial class OverviewViewModel : PageViewModel
{
    public override string Title => "Обзор";

    /// <summary>The overview also has to render the dongle-only and empty states.</summary>
    public override bool RequiresControl => false;

    [ObservableProperty]
    public partial double ChatMix { get; set; }

    [ObservableProperty]
    public partial NoiseReductionMode NoiseReduction { get; set; } = NoiseReductionMode.Off;

    public IReadOnlyList<NoiseReductionMode> NoiseReductionModes { get; } =
        [NoiseReductionMode.Off, NoiseReductionMode.Low, NoiseReductionMode.High];

    public OverviewViewModel(IDeviceService device) : base(device)
    {
    }

    protected override void OnStateApplied(DeviceState state)
    {
        if (state.ChatMix >= 0) ChatMix = state.ChatMix;
        if (state.NoiseReduction is { } mode) NoiseReduction = mode;

        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargeText));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(CableText));
        OnPropertyChanged(nameof(DongleText));
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(BoomMicText));
        OnPropertyChanged(nameof(PresetText));
        OnPropertyChanged(nameof(ChatMixText));
        OnPropertyChanged(nameof(SidetoneText));
        OnPropertyChanged(nameof(ShowNotFound));
        OnPropertyChanged(nameof(ShowMonitorOnly));
        OnPropertyChanged(nameof(ShowDevice));
    }

    partial void OnChatMixChanged(double value) =>
        Write(ct => Device.SetChatMixAsync((byte)Math.Round(value), ct));

    partial void OnNoiseReductionChanged(NoiseReductionMode value) =>
        Write(ct => Device.SetNoiseReductionAsync(value, ct));

    // ---------- projections ----------

    public bool ShowNotFound => State.Connection == ConnectionState.NotFound;

    public bool ShowMonitorOnly => State.Connection == ConnectionState.MonitorOnly;

    public bool ShowDevice => State.Connection != ConnectionState.NotFound;

    public string BatteryText => State.HasBattery ? $"{State.BatteryPercent}%" : "—";

    public string ChargeText => State.IsCharging ? "Заряжается" : "";

    public string FirmwareText =>
        State.FirmwareVersion is { } v ? $"Прошивка {v}" : "Прошивка —";

    public string CableText => State.CanControl
        ? "Кабель USB-C · настройка доступна"
        : "Кабель не подключён";

    public string DongleText => State.DonglePresent
        ? "Донгл 2.4 ГГц · звук"
        : "Донгл не подключён";

    public string MicStatusText => State.MicMuted switch
    {
        true => "Замьючен",
        false => "Не замьючен",
        null => "—",
    };

    /// <summary>
    /// The boom mic reports mute as 0xFF and refuses a level read while unplugged, so
    /// the two readings together are what tell us it is detached.
    /// </summary>
    public string BoomMicText => State.BoomMicAttached ? "Подключён" : "Отсоединён";

    public string PresetText => EqPresets.NameOf(State.EqPresetIndex);

    public string ChatMixText => (int)Math.Round(ChatMix) switch
    {
        0 => "0 · только игра",
        10 => "10 · баланс",
        20 => "20 · только чат",
        var v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public string SidetoneText => State.SidetoneEnabled switch
    {
        true when State.MaxSidetoneLevel > 0 =>
            $"Свой голос в наушниках · уровень {State.SidetoneLevel} из {State.MaxSidetoneLevel}",
        true => "Включён",
        false => "Выключен",
        null => "—",
    };
}
