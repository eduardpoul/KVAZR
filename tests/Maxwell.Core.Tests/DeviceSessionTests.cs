using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// The session decides what the interface is allowed to claim, so these cases are
/// about honesty as much as correctness: a refused write must not leave the new value
/// on screen, a silent device must not blank a good reading, and a control whose
/// precondition is unmet must be disabled rather than dead.
/// </summary>
public sealed class DeviceSessionTests
{
    private const string Ranged = "test.ranged";
    private const string Ceiling = "test.ceiling";
    private const string NeedsCable = "test.needs_cable";
    private const string Plain = "test.plain";
    private const string Grouped = "test.grouped";
    private const string Withheld = "test.withheld";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RefusedWrite_RestoresThePreviousValueAndExplainsItself()
    {
        (DeviceSession session, Fixture fixture) = await StartAsync();
        fixture.Connection.AcceptWrites = false;

        bool accepted = await session.WriteAsync(Plain, CapabilityValue.Of(7), Ct);

        CapabilityState state = session.Snapshot.Capabilities[Plain];

        Assert.False(accepted);
        Assert.Equal(CapabilityValue.Of(3), state.Value);
        Assert.False(state.IsWriting);
        Assert.NotNull(state.WriteError);
    }

    [Fact]
    public async Task AcceptedWrite_PromotesAnUntestedCapabilityForGood()
    {
        (DeviceSession session, Fixture fixture) = await StartAsync();

        Assert.Equal(CapabilityStatus.Untested, session.Snapshot.Capabilities[Plain].Status);

        bool accepted = await session.WriteAsync(Plain, CapabilityValue.Of(7), Ct);

        Assert.True(accepted);
        Assert.Equal(CapabilityStatus.Verified, session.Snapshot.Capabilities[Plain].Status);

        // Promotion has to outlive the process, or the amber mark comes back tomorrow.
        Assert.True(fixture.Ledger.IsConfirmed(fixture.Info.Key, Plain));
    }

    [Fact]
    public async Task DeviceSourcedRange_TakesItsCeilingFromTheReading()
    {
        (DeviceSession session, _) = await StartAsync();

        await session.RefreshAllAsync(Ct);

        // Declared as 0..1 with the real ceiling living in another capability; the
        // hard-coded placeholder must not survive contact with the device.
        Assert.Equal(31, session.Snapshot.Capabilities[Ranged].Range!.Max);
    }

    [Fact]
    public async Task SilentDevice_DoesNotBlankAValueAlreadyRead()
    {
        (DeviceSession session, Fixture fixture) = await StartAsync();
        await session.RefreshAllAsync(Ct);

        Assert.Equal(CapabilityValue.Of(3), session.Snapshot.Capabilities[Plain].Value);

        fixture.Connection.Values.Remove(Plain);
        await session.RefreshAllAsync(Ct);

        Assert.Equal(CapabilityValue.Of(3), session.Snapshot.Capabilities[Plain].Value);
    }

    [Fact]
    public async Task WhenTheDeviceDoesNotAnswer_ItIsVisibleAndItsControlsAreNot()
    {
        var fixture = new Fixture { ConnectFails = true };
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        // Present, enumerated, silent. A headset switched off or out of range looks
        // exactly like this, and it is an ordinary situation rather than a fault.
        Assert.Equal(DeviceAvailability.Unreachable, session.Snapshot.Availability);
        Assert.False(session.Snapshot.CanControl);
    }

    [Fact]
    public async Task AWithheldCapability_IsDescribedAndCannotBeWritten()
    {
        (DeviceSession session, Fixture fixture) = await StartAsync();

        CapabilityState state = session.Snapshot.Capabilities[Withheld];

        // Still on the page. Dropping it would describe a device that does not have the
        // function, and it does — we are the ones declining to offer it.
        Assert.True(session.Snapshot.Has(Withheld));
        Assert.True(state.IsAvailable);
        Assert.False(state.CanWrite);

        bool accepted = await session.WriteAsync(Withheld, CapabilityValue.Of(1), Ct);

        // Refused here rather than left to the interface to grey out, because a hotkey, a
        // tray command or a future page would each have to remember on its own otherwise.
        Assert.False(accepted);
        Assert.True(fixture.Connection.AcceptWrites);
    }

    [Fact]
    public async Task AChannelTheConnectionDoesNotUse_DoesNotCountAsTheDeviceMoving()
    {
        // What the Maxwell looks like: a dongle it is controlled over, and a Bluetooth
        // channel that carries sound and a battery reading and no control at all. The
        // connection knows only about the lead it talks over.
        TransportInfo dongle = new()
        {
            Kind = TransportKind.Dongle, Label = "USB-донгл", IsPresent = true, CarriesControl = true,
        };

        TransportInfo bluetooth = new()
        {
            Kind = TransportKind.Bluetooth, Label = "Bluetooth", IsPresent = true, CarriesControl = false,
            CarriesAudio = true,
        };

        var fixture = new Fixture();
        fixture.Connection.Transports = [dongle];
        DeviceSession session = fixture.CreateSession();

        DeviceInfo found = fixture.Info with { Transports = [dongle, bluetooth] };

        await session.MarkPresentAsync(found, Ct);
        Assert.Equal(1, fixture.Driver.Connects);

        // The next sweep, finding exactly what the last one found.
        await session.MarkPresentAsync(found, Ct);

        // One connection, still. Rebuilding it here cost twenty seconds out of every
        // minute on real hardware: the channel discovery knew about was overwritten by
        // the connection's shorter list, the next sweep saw it come back, and the
        // difference read as the headset having moved from one lead to another.
        Assert.Equal(1, fixture.Driver.Connects);

        // And the channel discovery knew about is still on the device, which is the half
        // of this the person sees — the Bluetooth chip in the header.
        Assert.Contains(
            session.Snapshot.Info.Transports,
            t => t.Kind == TransportKind.Bluetooth);
    }

    [Fact]
    public async Task AnUnmetRequirement_DisablesTheControlRatherThanHidingIt()
    {
        (DeviceSession session, _) = await StartAsync();

        Assert.True(session.Snapshot.Capabilities[NeedsCable].IsAvailable);

        await session.MarkAbsentAsync(Ct);

        CapabilityState state = session.Snapshot.Capabilities[NeedsCable];

        Assert.True(session.Snapshot.Has(NeedsCable));
        Assert.False(state.IsAvailable);
        Assert.False(state.CanWrite);
    }

    [Fact]
    public async Task AnAbsentDevice_KeepsItsLastReadingRatherThanShowingZeroes()
    {
        (DeviceSession session, _) = await StartAsync();
        await session.RefreshAllAsync(Ct);

        await session.MarkAbsentAsync(Ct);

        Assert.Equal(DeviceAvailability.Offline, session.Snapshot.Availability);
        Assert.Equal(CapabilityValue.Of(3), session.Snapshot.Capabilities[Plain].Value);
        Assert.NotNull(session.Snapshot.LastSeenAt);
    }

    /// <summary>
    /// A composite goes to the device whole and comes back whole. Half a curve is not
    /// something the hardware has a command for, so the session must not be able to
    /// store one either.
    /// </summary>
    [Fact]
    public async Task AGroupedValue_SurvivesTheRoundTripIntact()
    {
        (DeviceSession session, Fixture fixture) = await StartAsync();

        CapabilityValue moved = CapabilityValue.Numbers(1600, 800);

        Assert.True(await session.WriteAsync(Grouped, moved, Ct));
        Assert.Equal(moved, session.Snapshot.Capabilities[Grouped].Value);

        await session.RefreshAllAsync(Ct);

        Assert.Equal(moved, session.Snapshot.Capabilities[Grouped].Value);
        Assert.Equal(moved, fixture.Connection.Values[Grouped]);
    }

    /// <summary>
    /// A poll that reads back the same numbers is not the user's mouse being touched.
    ///
    /// This is the case that made the vector shape carry its own equality: the generated
    /// record comparison looks at the list by reference, so every refresh produced a
    /// fresh instance that compared unequal, and every volatile composite lit its
    /// "changed on the device" ring every few seconds until the ring meant nothing.
    /// </summary>
    [Fact]
    public async Task ARepeatedGroupedReading_IsNotReportedAsAnExternalChange()
    {
        (DeviceSession session, _) = await StartAsync();

        await session.RefreshAllAsync(Ct);
        await session.RefreshAllAsync(Ct);

        Assert.False(session.Snapshot.Capabilities[Grouped].ChangedExternally);
    }

    [Fact]
    public async Task AGroupedValueChangedOnTheDevice_IsReportedAsExternal()
    {
        (DeviceSession session, Fixture fixture) = await StartAsync();

        fixture.Connection.Values[Grouped] = CapabilityValue.Numbers(3200, 3200);
        await session.RefreshAllAsync(Ct);

        Assert.True(session.Snapshot.Capabilities[Grouped].ChangedExternally);
    }

    // ---------- fixture ----------

    /// <summary>
    /// Connects and reads once, which is the state every control is in by the time a
    /// person can touch it. Writing before a read would roll back to "not read yet" —
    /// correct, but not a situation the interface can produce.
    /// </summary>
    private static async Task<(DeviceSession Session, Fixture Fixture)> StartAsync()
    {
        var fixture = new Fixture();
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);
        await session.RefreshAllAsync(Ct);

        return (session, fixture);
    }

    private sealed class Fixture
    {
        public InMemoryCapabilityLedger Ledger { get; } = new();

        public FakeConnection Connection { get; } = new();

        public bool ConnectFails { get; init; }

        public DeviceInfo Info { get; } = new()
        {
            Key = "fake:device",
            DriverId = "fake",
            Vendor = "Fake",
            Model = "Device",
        };

        public FakeDriver Driver { get; private set; } = null!;

        public DeviceSession CreateSession()
        {
            Driver = new FakeDriver(this);
            return new DeviceSession(Info, Driver, Ledger, NullLogger<DeviceSession>.Instance);
        }
    }

    private sealed class FakeDriver : IDeviceDriver
    {
        private readonly Fixture _fixture;

        public FakeDriver(Fixture fixture) => _fixture = fixture;

        public string DriverId => "fake";

        public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([_fixture.Info]);

        public int Connects { get; private set; }

        public Task<IDeviceConnection> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
        {
            if (_fixture.ConnectFails) throw new IOException("no control channel");

            Connects++;
            return Task.FromResult<IDeviceConnection>(_fixture.Connection);
        }
    }

    private sealed class FakeConnection : IDeviceConnection
    {
        public bool AcceptWrites { get; set; } = true;

        public Dictionary<string, CapabilityValue> Values { get; } = new(StringComparer.Ordinal)
        {
            [Plain] = CapabilityValue.Of(3),
            [Ceiling] = CapabilityValue.Of(31),
            [Ranged] = CapabilityValue.Of(9),
            [NeedsCable] = CapabilityValue.Of(1),
            [Grouped] = CapabilityValue.Numbers(1600, 1200),
        };

        public IReadOnlyList<Capability> Capabilities { get; } =
        [
            new()
            {
                Id = Plain,
                Domain = CapabilityDomain.Audio,
                Primitive = CapabilityPrimitive.Range,
                Title = "Plain",
                DeclaredStatus = CapabilityStatus.Untested,
                Range = new CapabilityRange { Min = 0, Max = 10 },
            },
            new()
            {
                Id = Ceiling,
                Domain = CapabilityDomain.Audio,
                Primitive = CapabilityPrimitive.Readout,
                Title = "Ceiling",
                Access = CapabilityAccess.Read,
            },
            new()
            {
                Id = Ranged,
                Domain = CapabilityDomain.Audio,
                Primitive = CapabilityPrimitive.Range,
                Title = "Ranged",
                Range = new CapabilityRange
                {
                    Min = 0,
                    Max = 1,
                    Source = RangeSource.Device,
                    MaxFromCapability = Ceiling,
                },
            },
            new()
            {
                Id = NeedsCable,
                Domain = CapabilityDomain.Audio,
                Primitive = CapabilityPrimitive.Toggle,
                Title = "Needs cable",
                Requirement = RequirementKind.ControlChannel,
                RequirementHint = "Настройка доступна по кабелю",
            },
            new()
            {
                Id = Withheld,
                Domain = CapabilityDomain.Power,
                Primitive = CapabilityPrimitive.Action,
                Access = CapabilityAccess.Write,
                Title = "Withheld",
                Risk = CapabilityRisk.Hidden,
                IsWithheld = true,
            },
            new()
            {
                Id = Grouped,
                Domain = CapabilityDomain.Pointing,
                Primitive = CapabilityPrimitive.RangeGroup,
                Title = "Grouped",

                // Volatile on purpose: the external-change flag is only computed for
                // capabilities the device can move on its own, and it is the flag these
                // cases are about.
                IsVolatile = true,
                Range = new CapabilityRange { Min = 200, Max = 8000 },
                Members = [new(0, "X"), new(1, "Y")],
            },
        ];

        public IReadOnlyList<TransportInfo> Transports { get; set; } =
        [
            new() { Kind = TransportKind.UsbCable, Label = "USB", IsPresent = true, CarriesControl = true },
        ];

        public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default)
        {
            CapabilityValue value = Values.GetValueOrDefault(capabilityId, CapabilityValue.Absent);

            // Rebuilt rather than handed back, because a real driver decodes bytes into a
            // new value on every read. Returning the stored instance would let a
            // reference comparison pass and hide the precise bug these cases exist for.
            return Task.FromResult(value is VectorValue v ? CapabilityValue.Vector([.. v.Items]) : value);
        }

        public Task<bool> WriteAsync(
            string capabilityId,
            CapabilityValue value,
            CancellationToken cancellationToken = default)
        {
            if (AcceptWrites) Values[capabilityId] = value;
            return Task.FromResult(AcceptWrites);
        }

        public void Dispose()
        {
        }
    }
}
