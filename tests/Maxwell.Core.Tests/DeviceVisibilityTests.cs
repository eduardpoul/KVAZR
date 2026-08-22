using Maxwell.Core.Devices;
using Maxwell.Core.Polling;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// Hiding a device is not forgetting it, and the tests exist mostly to keep those two
/// apart. A hidden device keeps its pairing, keeps everything the system knows about it,
/// and comes back whole — the only thing that changes is that no surface shows it and
/// nothing wakes it up.
/// </summary>
public sealed class DeviceVisibilityTests
{
    private static DeviceInfo Device(
        string key,
        string model,
        DeviceClass deviceClass = DeviceClass.Headset,
        string? catalogued = null,
        bool auxiliary = false) =>
        new()
        {
            Key = key,
            DriverId = "test",
            Vendor = string.Empty,
            Model = model,
            Class = deviceClass,
            ImageKey = catalogued,
            Auxiliary = auxiliary,
        };

    private static HashSet<string> Hidden(params string[] keys) => new(keys, StringComparer.Ordinal);

    [Fact]
    public void ADeviceTheUserPutAway_IsHidden()
    {
        Assert.True(DeviceVisibility.IsHidden(
            Device("a", "JBL Flip 6"), Hidden("a"), hideSilent: false));
    }

    [Fact]
    public void EverythingElse_IsShown()
    {
        Assert.False(DeviceVisibility.IsHidden(
            Device("b", "JBL Flip 6"), Hidden("a"), hideSilent: true));
    }

    [Theory]
    [InlineData("6C:29:95:1B:AE:07")]
    [InlineData("6c-29-95-1b-ae-07")]
    [InlineData("6C2995 1BAE07")]
    public void ADeviceWhoseOnlyNameIsItsAddress_CountsAsSilent(string name)
    {
        Assert.True(DeviceVisibility.IsSilent(Device("x", name, DeviceClass.Unknown)));
    }

    [Fact]
    public void ACataloguedDevice_IsNotSilent_EvenWithNoClass()
    {
        // Flydigi's charging dock classifies as nothing at all, and it stays on the
        // list because a person put it in the catalogue — that entry is the statement
        // "this thing is real and wanted", and it is the only statement that can be:
        // nothing readable at this tier separates the dock from bus noise.
        Assert.False(DeviceVisibility.IsSilent(
            Device("x", "Flydigi CD2", DeviceClass.Unknown, catalogued: "flydigi-cd2")));
    }

    [Fact]
    public void AnUncataloguedUnknown_IsSilent_WhateverItsNameSays()
    {
        // "USB Controller", "Устройство ввода USB" — named, and still nothing a user owns
        // knowingly. The name being pronounceable is not the same as the device being
        // identifiable, and the earlier rule that trusted names left these on the list.
        Assert.True(DeviceVisibility.IsSilent(Device("x", "USB Controller", DeviceClass.Unknown)));
    }

    [Fact]
    public void AnAuxiliaryDevice_HidesUnderTheSameSwitch_AndComesBackWithIt()
    {
        // The G HUB virtual keyboard: honestly a keyboard, catalogued, and still
        // nothing anybody plugged in. The catalogue's hidden flag rides the silent
        // switch rather than adding a second one — one lever, one place to look.
        DeviceInfo ghost = Device("x", "G HUB", DeviceClass.Keyboard, "ghub", auxiliary: true);

        Assert.True(DeviceVisibility.IsHidden(ghost, Hidden(), hideSilent: true));
        Assert.False(DeviceVisibility.IsHidden(ghost, Hidden(), hideSilent: false));
    }

    [Fact]
    public void AKnownClass_IsNeverSilent_HoweverItIsNamed()
    {
        Assert.False(DeviceVisibility.IsSilent(Device("x", "6C:29:95:1B:AE:07", DeviceClass.Headset)));
    }

    [Fact]
    public void SweepingAnonymousDevicesAside_IsOnlyDoneWhenAskedFor()
    {
        DeviceInfo anonymous = Device("x", "6C:29:95:1B:AE:07", DeviceClass.Unknown);

        Assert.True(DeviceVisibility.IsHidden(anonymous, Hidden(), hideSilent: true));
        Assert.False(DeviceVisibility.IsHidden(anonymous, Hidden(), hideSilent: false));
    }

    [Fact]
    public void AHiddenDevice_IsNotPolled()
    {
        // The point of hiding is that the device is left alone. Taking it off the screens
        // while still waking it every thirty seconds would be the worst of both.
        var options = new DevicePollingOptions();

        PollPlan plan = PollingPolicy.Plan(
            new PollSubject("a", IsPresent: true, CanControl: true, ConsecutiveFailures: 0, IsHidden: true),
            PollingAttention.Focused,
            "a",
            options);

        Assert.Equal(PollScope.None, plan.Scope);
    }

    [Fact]
    public void AVisibleDeviceUnderFocus_IsStillReadInFull()
    {
        var options = new DevicePollingOptions();

        PollPlan plan = PollingPolicy.Plan(
            new PollSubject("a", IsPresent: true, CanControl: true, ConsecutiveFailures: 0),
            PollingAttention.Focused,
            "a",
            options);

        Assert.Equal(PollScope.BatteryAndVolatile, plan.Scope);
    }
}
