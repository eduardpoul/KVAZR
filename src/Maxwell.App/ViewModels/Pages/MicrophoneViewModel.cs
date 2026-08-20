using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels.Pages;

public sealed partial class MicrophoneViewModel : PageViewModel
{
    public override string Title => "Микрофон";

    [ObservableProperty]
    public partial NoiseReductionMode NoiseReduction { get; set; } = NoiseReductionMode.Off;

    [ObservableProperty]
    public partial bool SidetoneEnabled { get; set; }

    [ObservableProperty]
    public partial double SidetoneLevel { get; set; }

    [ObservableProperty]
    public partial double MicLevel { get; set; }

    public IReadOnlyList<NoiseReductionMode> NoiseReductionModes { get; } =
        [NoiseReductionMode.Off, NoiseReductionMode.Low, NoiseReductionMode.High];

    /// <summary>Read from the headset rather than hardcoded — other firmware may differ.</summary>
    public double SidetoneMaximum => State.MaxSidetoneLevel > 0 ? State.MaxSidetoneLevel : 31;

    public MicrophoneViewModel(IDeviceService device) : base(device)
    {
    }

    protected override void OnStateApplied(DeviceState state)
    {
        if (state.NoiseReduction is { } mode) NoiseReduction = mode;
        if (state.SidetoneEnabled is { } enabled) SidetoneEnabled = enabled;
        if (state.SidetoneLevel >= 0) SidetoneLevel = state.SidetoneLevel;
        if (state.MicLevel >= 0) MicLevel = state.MicLevel;

        OnPropertyChanged(nameof(SidetoneMaximum));
        OnPropertyChanged(nameof(SidetoneLevelText));
        OnPropertyChanged(nameof(MicLevelText));
        OnPropertyChanged(nameof(MicMuteText));
        OnPropertyChanged(nameof(BoomMicText));
        OnPropertyChanged(nameof(BoomMicAttached));
        OnPropertyChanged(nameof(BoomMicDetached));
    }

    partial void OnNoiseReductionChanged(NoiseReductionMode value) =>
        Write(ct => Device.SetNoiseReductionAsync(value, ct));

    partial void OnSidetoneEnabledChanged(bool value) =>
        Write(ct => Device.SetSidetoneEnabledAsync(value, ct));

    partial void OnSidetoneLevelChanged(double value)
    {
        OnPropertyChanged(nameof(SidetoneLevelText));
        Write(ct => Device.SetSidetoneLevelAsync((byte)Math.Round(value), ct));
    }

    partial void OnMicLevelChanged(double value)
    {
        OnPropertyChanged(nameof(MicLevelText));
        Write(ct => Device.SetMicLevelAsync((byte)Math.Round(value), ct));
    }

    // ---------- projections ----------

    public bool BoomMicAttached => State.BoomMicAttached;

    public bool BoomMicDetached => State.CanControl && !State.BoomMicAttached;

    public string BoomMicText => State.BoomMicAttached ? "Подключён" : "Отсоединён";

    /// <summary>Mute is hardware-only on this headset — there is no write command at all.</summary>
    public string MicMuteText => State.MicMuted switch
    {
        true => "Замьючен",
        false => "Не замьючен",
        null => "—",
    };

    public string SidetoneLevelText =>
        ((int)Math.Round(SidetoneLevel)).ToString(CultureInfo.InvariantCulture);

    public string MicLevelText => State.BoomMicAttached
        ? ((int)Math.Round(MicLevel)).ToString(CultureInfo.InvariantCulture)
        : "—";
}
