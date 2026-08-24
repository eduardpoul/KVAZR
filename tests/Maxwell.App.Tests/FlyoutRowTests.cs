using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// What an opened tray row offers, and why that is not simply "the first three".
///
/// The panel is 360 px wide and its height is the real ceiling: a slider costs two lines,
/// its caption and the track under it, while a dropdown or a segmented switch costs one.
/// Counting controls instead of lines stopped a Maxwell at ChatMix, volume and the
/// equaliser preset, and left the noise reduction — one line, and the thing you reach for
/// when someone says they can hear your keyboard — off a panel that had room for it.
/// </summary>
public sealed class FlyoutRowTests
{
    private sealed class Registry : IDeviceRegistry
    {
        public IReadOnlyList<DeviceSnapshot> Devices => [];

        public event EventHandler? DevicesChanged;

        public IDeviceSession? Session(string deviceKey) => null;

        public Task DiscoverAsync(CancellationToken cancellationToken = default)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private static CapabilityState Range(string id, string title) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Access = CapabilityAccess.ReadWrite,
            Title = title,
            Range = new CapabilityRange { Min = 0, Max = 20 },
        },
        Value = CapabilityValue.Of(10),
    };

    private static CapabilityState Choice(string id, string title, params string[] options) => new()
    {
        Descriptor = new Capability
        {
            Id = id,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Choice,
            Access = CapabilityAccess.ReadWrite,
            Title = title,
            Choices = [.. options.Select((name, i) => new CapabilityChoice(i, name))],
        },
        Value = CapabilityValue.Choice(0),
    };

    private static DeviceSnapshot Maxwell() => new()
    {
        Info = new DeviceInfo
        {
            Key = "audeze.race:maxwell",
            DriverId = "audeze.race",
            Vendor = "Audeze",
            Model = "Maxwell",
            Class = DeviceClass.Headset,
        },
        Availability = DeviceAvailability.Active,
        Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
        {
            [CapabilityIds.MixChat] = Range(CapabilityIds.MixChat, "ChatMix"),
            [CapabilityIds.AudioVolume] = Range(CapabilityIds.AudioVolume, "Общая громкость"),
            [CapabilityIds.AudioEqPreset] = Choice(CapabilityIds.AudioEqPreset, "Пресет эквалайзера", "Audeze", "Bass"),
            [CapabilityIds.MicNoiseReduction] =
                Choice(CapabilityIds.MicNoiseReduction, "Шумоподавление", "Выключено", "Низкое", "Высокое"),
            [CapabilityIds.MicSidetone] = Choice(CapabilityIds.MicSidetone, "Сайдтон", "Выкл", "Вкл"),
        },
    };

    [Fact]
    public void TwoSlidersAndTwoShortControlsFitThePanel()
    {
        using var row = new FlyoutRowViewModel(Maxwell(), new Registry());
        row.ToggleCommand.Execute(null);

        Assert.Equal(
            [
                CapabilityIds.MixChat,
                CapabilityIds.AudioVolume,
                CapabilityIds.AudioEqPreset,
                CapabilityIds.MicNoiseReduction,
            ],
            row.Quick.Select(q => q.Id));

        // Sidetone would be a seventh line. It is last in the order for a reason — a
        // sidetone level is set once and left — so it is the one that goes.
        Assert.DoesNotContain(row.Quick, q => q.Id == CapabilityIds.MicSidetone);
    }
}
