using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels.Pages;

public sealed partial class SystemViewModel : PageViewModel
{
    public override string Title => "Система";

    [ObservableProperty]
    public partial double VoicePromptLevel { get; set; }

    public SystemViewModel(IDeviceService device) : base(device)
    {
    }

    protected override void OnStateApplied(DeviceState state)
    {
        if (state.VoicePromptLevel >= 0) VoicePromptLevel = state.VoicePromptLevel;

        OnPropertyChanged(nameof(VoicePromptText));
        OnPropertyChanged(nameof(AutoPowerOffText));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(PowerText));
        OnPropertyChanged(nameof(ConnectionText));
    }

    partial void OnVoicePromptLevelChanged(double value)
    {
        OnPropertyChanged(nameof(VoicePromptText));
        Write(ct => Device.SetVoicePromptLevelAsync((byte)Math.Round(value), ct));
    }

    // ---------- projections ----------

    public string VoicePromptText =>
        ((int)Math.Round(VoicePromptLevel)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The headset returns an index, not minutes, and the mapping from index to
    /// duration has not been worked out yet. Showing "7" is honest; inventing
    /// "30 минут" would not be, so the raw value is labelled as provisional.
    /// </summary>
    public string AutoPowerOffText => State.AutoPowerOff >= 0
        ? $"режим {State.AutoPowerOff} · соответствие минутам не установлено"
        : "—";

    public string FirmwareText => State.FirmwareVersion ?? "—";

    /// <summary>
    /// Only a cable proves the headset is awake. A dongle enumerates whether or not
    /// anything is paired to it, so claiming "on" from its presence alone would be a
    /// guess dressed up as a reading.
    /// </summary>
    public string PowerText => State.CanControl ? "Включено" : "—";

    public string ConnectionText => (State.CanControl, State.DonglePresent) switch
    {
        (true, true) => "Кабель и донгл",
        (true, false) => "Кабель USB-C",
        (false, true) => "Донгл 2.4 ГГц",
        _ => "Не подключено",
    };
}
