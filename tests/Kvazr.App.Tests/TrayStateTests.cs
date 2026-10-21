using Kvazr.App.Branding;
using Kvazr.App.Tray;
using Xunit;

namespace Kvazr.App.Tests;

/// <summary>
/// One glyph, several devices. These pin down the priority order, which is the whole
/// of the decision — the tray icon exists to make the user open the window at the right
/// moment, so the most urgent thing has to win.
/// </summary>
public class TrayStateTests
{
    private const int Threshold = 15;

    private static TrayDeviceReading Healthy(int percent = 80) => new(true, true, percent, false);

    [Fact]
    public void Nothing_found_yet_is_searching_not_empty() =>
        Assert.Equal(
            KvazrMarkState.Searching,
            TrayState.For([], discovering: true, firmwareInstalling: false, Threshold));

    [Fact]
    public void Looked_and_found_nothing_is_empty() =>
        Assert.Equal(
            KvazrMarkState.NoDevices,
            TrayState.For([], discovering: false, firmwareInstalling: false, Threshold));

    /// <summary>
    /// A device known from a previous session but not here now is not a device. The
    /// list keeps it, dimmed; the tray must not claim everything is connected.
    /// </summary>
    [Fact]
    public void An_offline_device_does_not_count_as_present() =>
        Assert.Equal(
            KvazrMarkState.NoDevices,
            TrayState.For(
                [new TrayDeviceReading(IsPresent: false, Answers: false, BatteryPercent: 62, IsCharging: false)],
                discovering: false,
                firmwareInstalling: false,
                Threshold));

    [Fact]
    public void Everything_answering_is_all_connected() =>
        Assert.Equal(
            KvazrMarkState.AllConnected,
            TrayState.For([Healthy(), Healthy(41)], discovering: false, firmwareInstalling: false, Threshold));

    [Fact]
    public void A_present_device_that_does_not_answer_is_a_link_error() =>
        Assert.Equal(
            KvazrMarkState.LinkError,
            TrayState.For(
                [Healthy(), new TrayDeviceReading(IsPresent: true, Answers: false, BatteryPercent: null, IsCharging: false)],
                discovering: false,
                firmwareInstalling: false,
                Threshold));

    [Fact]
    public void Below_the_threshold_is_low_battery() =>
        Assert.Equal(
            KvazrMarkState.LowBattery,
            TrayState.For([Healthy(), Healthy(9)], discovering: false, firmwareInstalling: false, Threshold));

    [Fact]
    public void The_threshold_itself_is_not_low() =>
        Assert.Equal(
            KvazrMarkState.AllConnected,
            TrayState.For([Healthy(Threshold)], discovering: false, firmwareInstalling: false, Threshold));

    /// <summary>
    /// A headset on the cradle at 8 % is being dealt with. An amber planet there is
    /// nagging about a solved problem, and the user learns to ignore the colour.
    /// </summary>
    [Fact]
    public void Charging_is_not_low_battery() =>
        Assert.Equal(
            KvazrMarkState.AllConnected,
            TrayState.For(
                [new TrayDeviceReading(IsPresent: true, Answers: true, BatteryPercent: 8, IsCharging: true)],
                discovering: false,
                firmwareInstalling: false,
                Threshold));

    /// <summary>A device with no battery at all — the QuadCast — must not read as flat.</summary>
    [Fact]
    public void A_device_with_no_battery_is_not_low() =>
        Assert.Equal(
            KvazrMarkState.AllConnected,
            TrayState.For(
                [new TrayDeviceReading(IsPresent: true, Answers: true, BatteryPercent: null, IsCharging: false)],
                discovering: false,
                firmwareInstalling: false,
                Threshold));

    /// <summary>
    /// A silent device's battery reading is stale by definition, so the silence is the
    /// thing to report.
    /// </summary>
    [Fact]
    public void Link_error_outranks_low_battery() =>
        Assert.Equal(
            KvazrMarkState.LinkError,
            TrayState.For(
                [
                    Healthy(3),
                    new TrayDeviceReading(IsPresent: true, Answers: false, BatteryPercent: null, IsCharging: false),
                ],
                discovering: false,
                firmwareInstalling: false,
                Threshold));

    /// <summary>
    /// Above everything else: a write in progress is the only state where the user
    /// pulling a cable does real damage.
    /// </summary>
    [Fact]
    public void Firmware_outranks_everything() =>
        Assert.Equal(
            KvazrMarkState.FirmwareUpdating,
            TrayState.For(
                [new TrayDeviceReading(IsPresent: true, Answers: false, BatteryPercent: 2, IsCharging: false)],
                discovering: true,
                firmwareInstalling: true,
                Threshold));

    /// <summary>
    /// Devices present and answering while the sweep is still running: the mark turns,
    /// because more may yet appear and a resting mark would be claiming otherwise.
    /// </summary>
    [Fact]
    public void Still_sweeping_keeps_the_mark_turning() =>
        Assert.Equal(
            KvazrMarkState.Searching,
            TrayState.For([Healthy()], discovering: true, firmwareInstalling: false, Threshold));
}
