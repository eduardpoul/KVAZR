using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.Core.Model;
using Maxwell.Core.Services;
using Maxwell.Race.Protocol;

namespace Maxwell.App.ViewModels.Pages;

public sealed partial class SoundViewModel : PageViewModel
{
    /// <summary>Balance travels roughly six steps either side of the 0x80 centre.</summary>
    private const int BalanceCentre = 128;
    private const int BalanceTravel = 6;

    public override string Title => "Звук";

    public IReadOnlyList<EqPreset> Presets => EqPresets.All;

    [ObservableProperty]
    public partial EqPreset? SelectedPreset { get; set; }

    [ObservableProperty]
    public partial double AmpGain { get; set; }

    [ObservableProperty]
    public partial double LeftGain { get; set; }

    [ObservableProperty]
    public partial double RightGain { get; set; }

    [ObservableProperty]
    public partial double Balance { get; set; } = BalanceCentre;

    public double GainMaximum => RaceCommands.MaxGain;

    public double BalanceMinimum => BalanceCentre - BalanceTravel;

    public double BalanceMaximum => BalanceCentre + BalanceTravel;

    public SoundViewModel(IDeviceService device) : base(device)
    {
    }

    protected override void OnStateApplied(DeviceState state)
    {
        if (state.AmpGain >= 0) AmpGain = state.AmpGain;
        if (state.LeftGain >= 0) LeftGain = state.LeftGain;
        if (state.RightGain >= 0) RightGain = state.RightGain;
        if (state.EarcupBalance >= 0) Balance = state.EarcupBalance;
        SelectedPreset = EqPresets.ByIndex(state.EqPresetIndex);

        OnPropertyChanged(nameof(AmpGainText));
        OnPropertyChanged(nameof(LeftGainText));
        OnPropertyChanged(nameof(RightGainText));
        OnPropertyChanged(nameof(BalanceText));
        OnPropertyChanged(nameof(PresetName));
    }

    partial void OnSelectedPresetChanged(EqPreset? value)
    {
        if (value is null) return;
        Write(ct => Device.SetEqPresetAsync(value.Index, ct));
    }

    partial void OnAmpGainChanged(double value)
    {
        OnPropertyChanged(nameof(AmpGainText));
        WriteGain(GainChannel.Master, value, v => AmpGain = v);
    }

    partial void OnLeftGainChanged(double value)
    {
        OnPropertyChanged(nameof(LeftGainText));
        WriteGain(GainChannel.Left, value, v => LeftGain = v);
    }

    partial void OnRightGainChanged(double value)
    {
        OnPropertyChanged(nameof(RightGainText));
        WriteGain(GainChannel.Right, value, v => RightGain = v);
    }

    partial void OnBalanceChanged(double value)
    {
        OnPropertyChanged(nameof(BalanceText));
        Write(ct => Device.SetEarcupBalanceAsync((byte)Math.Round(value), ct));
    }

    /// <summary>
    /// A slider can land on 136 or 142, which the firmware silently discards. Rather
    /// than let the control show a value the headset never accepted, the request is
    /// nudged to the nearest usable step and the slider follows.
    /// </summary>
    private void WriteGain(GainChannel channel, double raw, Action<double> correct)
    {
        int value = (int)Math.Round(raw);
        value = Math.Clamp(value, 0, RaceCommands.MaxGain);

        if (RaceCommands.IsReservedGain(value))
        {
            value = value > 0 ? value - 1 : value + 1;
            correct(value);
            return;
        }

        Write(ct => Device.SetGainAsync(channel, (byte)value, ct));
    }

    // ---------- projections ----------

    public string PresetName => SelectedPreset?.Name ?? "—";

    public string AmpGainText => Format(AmpGain);

    public string LeftGainText => Format(LeftGain);

    public string RightGainText => Format(RightGain);

    /// <summary>Shown relative to centre, because "+2 вправо" reads better than "130".</summary>
    public string BalanceText
    {
        get
        {
            int offset = (int)Math.Round(Balance) - BalanceCentre;
            return offset switch
            {
                0 => "по центру",
                < 0 => $"{offset} · левее",
                _ => $"+{offset} · правее",
            };
        }
    }

    private static string Format(double value) =>
        ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
}
