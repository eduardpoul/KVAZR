using Maxwell.Core.Devices;
using Maxwell.Core.Notifications;
using Maxwell.Core.Polling;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The schedule is a pure function of what is on screen and what each device is doing,
/// so the whole policy runs here without a timer, a device or a clock.
/// </summary>
public sealed class PollingPolicyTests
{
    private const string Headset = "audeze.race:maxwell-xbox";
    private const string Mouse = "mock:mouse";

    private static readonly DevicePollingOptions Options = new();

    [Fact]
    public void OfflineDevice_IsNotPolled()
    {
        PollPlan plan = Plan(Headset, present: false, canControl: false, PollingAttention.Glance);

        Assert.Equal(PollScope.None, plan.Scope);
    }

    [Fact]
    public void UnreachableDevice_IsNotPolled()
    {
        // A device that is not answering is re-probed by discovery, not by the poller:
        // polling it would climb the failure backoff forever while discovery is already
        // asking it the one question that matters.
        PollPlan plan = Plan(Headset, present: true, canControl: false, PollingAttention.Focused, Headset);

        Assert.Equal(PollScope.None, plan.Scope);
    }

    [Fact]
    public void FocusedDevice_AlsoReadsWhatTheHardwareCanChangeItself()
    {
        PollPlan plan = Plan(Headset, present: true, canControl: true, PollingAttention.Focused, Headset);

        Assert.Equal(PollScope.BatteryAndVolatile, plan.Scope);
    }

    [Fact]
    public void UnfocusedDevice_ReadsOnlyItsBattery()
    {
        // The mouse is present and fine, but its page is not the one open.
        PollPlan plan = Plan(Mouse, present: true, canControl: true, PollingAttention.Focused, Headset);

        Assert.Equal(PollScope.Battery, plan.Scope);
    }

    [Fact]
    public void WithNothingOnScreen_TheIntervalStretches()
    {
        PollPlan idle = Plan(Headset, present: true, canControl: true, PollingAttention.Idle);
        PollPlan glance = Plan(Headset, present: true, canControl: true, PollingAttention.Glance);

        Assert.Equal(glance.Interval * Options.IdleMultiplier, idle.Interval);
    }

    [Fact]
    public void ConsecutiveFailures_BackOffAndAreCapped()
    {
        TimeSpan first = PollingPolicy.Backoff(Options.Interval, 1, Options);
        TimeSpan second = PollingPolicy.Backoff(Options.Interval, 2, Options);
        TimeSpan far = PollingPolicy.Backoff(Options.Interval, 99, Options);

        Assert.Equal(Options.Interval * 2, first);
        Assert.Equal(Options.Interval * 4, second);
        Assert.Equal(Options.Interval * (1 << Options.MaxBackoffShift), far);
    }

    [Fact]
    public void OneSuccess_ClearsTheBackoff()
    {
        Assert.Equal(Options.Interval, PollingPolicy.Backoff(Options.Interval, 0, Options));
    }

    [Fact]
    public void DiscoveryIsFasterWhileTheListIsEmpty()
    {
        // This is the moment the user is plugging something in and watching for it.
        Assert.True(
            PollingPolicy.DiscoveryDelay(anyDevicePresent: false, Options)
            < PollingPolicy.DiscoveryDelay(anyDevicePresent: true, Options));
    }

    // ---------- the fast lane ----------

    /// <summary>
    /// The fast lane exists for one row today — a microphone's input level — and reading
    /// that row means listening to the microphone. So every one of these is really the
    /// same question: can the application end up listening to somebody without their
    /// having asked to see a level?
    /// </summary>
    [Fact]
    public void AnOpenPage_ReadsItsLiveRows()
    {
        Assert.Contains(Level, PollingPolicy.Live(Microphone(), PollingAttention.Focused, Mic));
    }

    /// <summary>
    /// The switch a live row hangs from is read on the same lane as the row, and this is
    /// correctness rather than thrift.
    ///
    /// A microphone's level and the switch above it are two readings of one thing: the
    /// switch is off when the capsule is silent, and the capsule is what the level is
    /// measuring. Reading one of them sixteen times a second while the other waited for
    /// the ordinary sweep put a moving meter above a switch that said the microphone was
    /// off, a second at a time.
    /// </summary>
    [Fact]
    public void TheSwitchALiveRowHangsFrom_IsReadAsOftenAsTheRow()
    {
        Assert.Contains(Switch, PollingPolicy.Live(Microphone(), PollingAttention.Focused, Mic));
    }

    [Fact]
    public void NothingIsReadTwice_ThoughSeveralRowsMayShareAGate()
    {
        IReadOnlyList<string> live = PollingPolicy.Live(Microphone(), PollingAttention.Focused, Mic);

        Assert.Equal(live.Count, live.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AGateThatIsNotThere_IsNotInvented()
    {
        // A driver may name a switch this device does not publish — the level of a
        // microphone whose mute is not exposed. Asking for it would read a capability
        // nothing can answer, once per frame.
        Assert.DoesNotContain("mic.absent_switch", PollingPolicy.Live(Microphone(gate: "mic.absent_switch"), PollingAttention.Focused, Mic));
    }

    [Fact]
    public void TheTrayAndTheFlyout_ReadNothingLive()
    {
        // Neither surface shows a level. The flyout is batteries, the tray is one icon.
        Assert.Empty(PollingPolicy.Live(Microphone(), PollingAttention.Idle, Mic));
        Assert.Empty(PollingPolicy.Live(Microphone(), PollingAttention.Glance, Mic));
    }

    [Fact]
    public void AMicrophoneWhosePageIsNotOpen_IsNotListenedTo()
    {
        // The page open is a headset's; the microphone is merely in the list behind it.
        Assert.Empty(PollingPolicy.Live(Microphone(), PollingAttention.Focused, Headset));
    }

    [Fact]
    public void ADeviceThatIsNotAnswering_HasNothingToMeasure()
    {
        Assert.Empty(PollingPolicy.Live(Microphone(canControl: false), PollingAttention.Focused, Mic));
        Assert.Empty(PollingPolicy.Live(Microphone(present: false), PollingAttention.Focused, Mic));
    }

    [Fact]
    public void OrdinaryRows_AreNotTheFastLanesBusiness()
    {
        // Volume is read by the sweep, at the sweep's pace. If it came through here as
        // well the two would race each other into the same session sixteen times a second.
        Assert.DoesNotContain(CapabilityIds.MicSystemLevel, PollingPolicy.Live(Microphone(), PollingAttention.Focused, Mic));
    }

    private const string Mic = "system:quadcast";
    private const string Level = "mic.system_peak";
    private const string Switch = "mic.system_on";

    private static DeviceSnapshot Microphone(bool present = true, bool canControl = true, string? gate = Switch)
    {
        var info = new DeviceInfo
        {
            Key = Mic,
            DriverId = "system",
            Vendor = "HyperX",
            Model = "QuadCast S",
            Class = DeviceClass.Microphone,
        };

        return new DeviceSnapshot
        {
            Info = info,

            // Unreachable is the shape of a device Windows can see and nothing can talk
            // to — which for a microphone means there is no stream to measure either.
            Availability = !present
                ? DeviceAvailability.Offline
                : canControl ? DeviceAvailability.Active : DeviceAvailability.Unreachable,

            Capabilities = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
            {
                [Level] = new()
                {
                    Descriptor = new Capability
                    {
                        Id = Level,
                        Domain = CapabilityDomain.Microphone,
                        Primitive = CapabilityPrimitive.Readout,
                        Access = CapabilityAccess.Read,
                        Title = "Input level",
                        IsLive = true,
                        EnabledBy = gate,
                    },
                },
                [Switch] = new()
                {
                    Descriptor = new Capability
                    {
                        Id = Switch,
                        Domain = CapabilityDomain.Microphone,
                        Primitive = CapabilityPrimitive.Toggle,
                        Title = "Microphone",
                        IsVolatile = true,
                    },
                },
                [CapabilityIds.MicSystemLevel] = new()
                {
                    Descriptor = new Capability
                    {
                        Id = CapabilityIds.MicSystemLevel,
                        Domain = CapabilityDomain.Microphone,
                        Primitive = CapabilityPrimitive.Range,
                        Title = "Recording level",
                    },
                },
            },
        };
    }

    private static PollPlan Plan(
        string key,
        bool present,
        bool canControl,
        PollingAttention attention,
        string? focused = null,
        int failures = 0) =>
        PollingPolicy.Plan(new PollSubject(key, present, canControl, failures), attention, focused, Options);
}

public sealed class BatteryWatchdogTests
{
    private const string Headset = "audeze.race:maxwell-xbox";
    private const string Mouse = "mock:mouse";

    [Fact]
    public void OneDeviceCrossingAThreshold_DoesNotSilenceAnother()
    {
        var watchdog = new BatteryWatchdog();

        watchdog.Evaluate(Headset, 40, isCharging: false);
        watchdog.Evaluate(Mouse, 40, isCharging: false);

        BatteryDecision headset = watchdog.Evaluate(Headset, 29, isCharging: false);
        BatteryDecision mouse = watchdog.Evaluate(Mouse, 29, isCharging: false);

        // Shared state here would mark 30 announced on the first call and swallow the
        // second device's warning entirely.
        Assert.Equal(BatteryAlert.LowBattery, headset.Alert);
        Assert.Equal(BatteryAlert.LowBattery, mouse.Alert);
    }

    [Fact]
    public void TheSameDevice_IsNotWarnedAboutTwiceForOneCrossing()
    {
        var watchdog = new BatteryWatchdog();
        watchdog.Evaluate(Headset, 40, isCharging: false);

        Assert.Equal(BatteryAlert.LowBattery, watchdog.Evaluate(Headset, 29, isCharging: false).Alert);
        Assert.Equal(BatteryAlert.None, watchdog.Evaluate(Headset, 28, isCharging: false).Alert);
    }

    [Fact]
    public void TheUserThreshold_JoinsTheStandardOnesInDescendingOrder()
    {
        IReadOnlyList<int> thresholds = BatteryWatchdog.ThresholdsFor(20);

        Assert.Equal([30, 20, 15, 5], thresholds);
    }

    [Fact]
    public void AThresholdEqualToAStandardOne_IsNotDuplicated()
    {
        Assert.Equal([30, 15, 5], BatteryWatchdog.ThresholdsFor(15));
    }
}

/// <summary>
/// How often the device somebody is actually looking at gets read.
///
/// The settings channel says nothing unless asked, so everything the wearer changes on the
/// device itself — the ChatMix wheel, the sidetone, the preset button — reaches the screen
/// no sooner than the next read. At the interval the other devices use, that is ten
/// seconds of a panel disagreeing with the thing in someone's hands.
/// </summary>
public sealed class FocusedPollingTests
{
    private static readonly DevicePollingOptions Options = new()
    {
        Interval = TimeSpan.FromSeconds(10),
        FocusedInterval = TimeSpan.FromSeconds(1),
        IdleMultiplier = 4,
    };

    private static PollSubject Subject(string key = "device") => new(key, IsPresent: true, CanControl: true, ConsecutiveFailures: 0);

    [Fact]
    public void OpenPage_IsReadFarMoreOften()
    {
        PollPlan plan = PollingPolicy.Plan(Subject(), PollingAttention.Focused, "device", Options);

        Assert.Equal(Options.FocusedInterval, plan.Interval);
        Assert.Equal(PollScope.BatteryAndVolatile, plan.Scope);
    }

    /// <summary>The others carry on at the ordinary rate; only the watched one is hurried.</summary>
    [Fact]
    public void OtherDevices_KeepTheOrdinaryInterval()
    {
        PollPlan plan = PollingPolicy.Plan(Subject("other"), PollingAttention.Focused, "device", Options);

        Assert.Equal(Options.Interval, plan.Interval);
        Assert.Equal(PollScope.Battery, plan.Scope);
    }

    [Fact]
    public void NothingOpen_SlowsDownAsBefore()
    {
        PollPlan plan = PollingPolicy.Plan(Subject(), PollingAttention.Idle, null, Options);

        Assert.Equal(Options.Interval * Options.IdleMultiplier, plan.Interval);
    }
}
