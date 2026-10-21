using Kvazr.App.Platform;
using Kvazr.Core.Devices;
using Xunit;

namespace Kvazr.App.Tests;

/// <summary>
/// Taking a device off the list when it is not here any more.
///
/// Three things it is not, and each of them is a different button somewhere else:
/// it is not hiding, which is about a device that is here and stays put away until it is
/// asked for back; it is not forgetting, which unpairs the device in Windows and cannot
/// be undone from this application; and it is not permanent, because nothing on this
/// machine can know whether the gamepad that was unplugged is in a drawer or in a bin.
/// </summary>
public sealed class RemovedDevicesTests
{
    private sealed class Registry : IDeviceRegistry
    {
        public Registry(params DeviceSnapshot[] devices) => Devices = devices;

        public IReadOnlyList<DeviceSnapshot> Devices { get; }

        public event EventHandler? DevicesChanged;

        public IDeviceSession? Session(string deviceKey) => null;

        public Task DiscoverAsync(CancellationToken cancellationToken = default)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private const string Key = "system:gamepad";

    private static DeviceSnapshot Gamepad(bool present) => new()
    {
        Info = new DeviceInfo
        {
            Key = Key,
            DriverId = "system",
            Vendor = "Test",
            Model = "Gamepad",
            Class = DeviceClass.Gamepad,
        },
        Availability = present ? DeviceAvailability.Active : DeviceAvailability.Offline,
    };

    [Fact]
    public async Task ADeviceThatWasRemoved_IsOffTheList()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        Assert.True(hidden.IsVisible(Gamepad(present: false)));

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);

        Assert.False(hidden.IsVisible(Gamepad(present: false)));
    }

    /// <summary>
    /// And back the moment it is plugged in. A list that ignores hardware somebody is
    /// holding is the one fault a device manager cannot afford, so the entry is not
    /// consulted at all once the device is present — before any housekeeping runs.
    /// </summary>
    [Fact]
    public async Task PluggedInAgain_ItComesBack()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);

        Assert.True(hidden.IsVisible(Gamepad(present: true)));
    }

    [Fact]
    public async Task OnceItIsBack_TheEntryIsDropped()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);
        Assert.Single(store.Current.RemovedDevices);

        await hidden.PruneAsync(new Registry(Gamepad(present: true)));

        Assert.Empty(store.Current.RemovedDevices);
    }

    [Fact]
    public async Task WhileItIsStillAway_TheEntryStays()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);
        await hidden.PruneAsync(new Registry(Gamepad(present: false)));

        Assert.Single(store.Current.RemovedDevices);
    }

    /// <summary>
    /// What was remembered goes with the row. That is the whole difference between this
    /// and hiding: hiding keeps a device's colour ready for when it comes out of the
    /// drawer, removing says the thing is gone and its preferences are a note about
    /// somebody else's hardware.
    /// </summary>
    [Fact]
    public async Task RemovingADevice_DropsWhatWasRememberedAboutIt()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        new SettingsDeviceMemory(store).Remember(Key, "colour", "00FF00");
        Assert.True(store.Current.DeviceMemory.ContainsKey(Key));

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);

        Assert.False(store.Current.DeviceMemory.ContainsKey(Key));
    }

    /// <summary>
    /// A removed device is not also a hidden one. Counting it as hidden would put it in
    /// the line offering hidden devices back — an offer to return something that is not
    /// there, which does nothing when taken.
    /// </summary>
    [Fact]
    public async Task ARemovedDevice_IsNotCountedAsHidden()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        await hidden.HideAsync(Key, DateTimeOffset.Now);
        Assert.Equal(1, hidden.Count);

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);

        Assert.Equal(0, hidden.Count);
    }

    [Fact]
    public async Task BringingEverythingBack_BringsRemovedOnesToo()
    {
        var store = new Harness.Store();
        var hidden = new HiddenDevices(store);

        await hidden.RemoveAsync(Key, DateTimeOffset.Now);
        await hidden.ShowAllAsync();

        Assert.True(hidden.IsVisible(Gamepad(present: false)));
    }
}
