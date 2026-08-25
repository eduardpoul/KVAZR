using System.Reflection;
using Avalonia.Threading;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.Tests;

/// <summary>
/// The pieces a capability view model needs before it will do anything: a session to
/// write into, a reading to start from, and a dispatcher this thread owns.
/// </summary>
internal static class Harness
{
    /// <summary>A session that accepts everything and remembers nothing.</summary>
    internal sealed class Recorder : IDeviceSession
    {
        public string DeviceKey => "test:device";

        public DeviceSnapshot Snapshot { get; } = new()
        {
            Info = new DeviceInfo
            {
                Key = "test:device",
                DriverId = "test",
                Vendor = "Test",
                Model = "Device",
                Class = DeviceClass.Headset,
            },
            Availability = DeviceAvailability.Active,
        };

        public event EventHandler<DeviceSnapshot>? SnapshotChanged;

        public Task RefreshAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RefreshAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ProbeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> WriteAsync(string id, CapabilityValue value, CancellationToken cancellationToken = default)
        {
            SnapshotChanged?.Invoke(this, Snapshot);
            return Task.FromResult(true);
        }
    }

    /// <summary>Settings that never reach a disk. Counts the saves, so a test can tell
    /// "written" from "changed in memory and forgotten".</summary>
    internal sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();

        public int Saves { get; private set; }

        public event EventHandler<AppSettings>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
    }

    internal static CapabilityState Curve(params int[] gains) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.AudioEqGraphic,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Curve,
            Access = CapabilityAccess.ReadWrite,
            Title = "Эквалайзер",
            Range = new CapabilityRange { Min = -12, Max = 12, Unit = "дБ" },
            Members = [.. Enumerable.Range(0, gains.Length).Select(i => new CapabilityMember(i, $"{i}"))],
        },
        Value = CapabilityValue.Vector([.. gains.Select(CapabilityValue.Of)]),
    };

    /// <summary>The headset's own preset list, as the equaliser's picker reads it.</summary>
    internal static CapabilityState Presets(int selected, params string[] names) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.AudioEqPreset,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Choice,
            Access = CapabilityAccess.ReadWrite,
            Title = "Пресет эквалайзера",
            Choices = [.. names.Select((name, i) => new CapabilityChoice(i, name))],
        },
        Value = CapabilityValue.Choice(selected),
    };

    /// <summary>A plain on/off, such as the sidetone switch.</summary>
    internal static CapabilityState Toggle(bool on) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.MicSidetone,
            Domain = CapabilityDomain.Microphone,
            Primitive = CapabilityPrimitive.Toggle,
            Access = CapabilityAccess.ReadWrite,
            Title = "Сайдтон",
        },
        Value = CapabilityValue.Of(on),
    };

    internal static CapabilityState Range(int value) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.AudioVolume,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Access = CapabilityAccess.ReadWrite,
            Title = "Общая громкость",
            Range = new CapabilityRange { Min = 0, Max = 150 },
        },
        Value = CapabilityValue.Of(value),
    };

    /// <summary>
    /// Lets the posted settle run. The view models hold writes until it has, so without
    /// this a test drags a band and nothing is ever sent.
    /// </summary>
    internal static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// Hands the dispatcher to this thread, and must run before anything posts to it —
    /// the reset drops whatever is queued, so calling it between a post and its pump
    /// silently swallows the settle the test is waiting for.
    /// </summary>
    internal static void Bind()
    {
        MethodInfo reset =
            typeof(Dispatcher).GetMethod(
                "ResetBeforeUnitTests",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null)
            ?? throw new InvalidOperationException("Avalonia no longer offers ResetBeforeUnitTests()");

        reset.Invoke(null, null);
    }
}
