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
    public async Task WithoutAControlChannel_TheDeviceIsVisibleAndItsControlsAreNot()
    {
        var fixture = new Fixture { ConnectFails = true };
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        // Present, readable in principle, driveable in no respect. This is an ordinary
        // mode of use for a headset reachable only over its dongle, not a fault.
        Assert.Equal(DeviceAvailability.MonitorOnly, session.Snapshot.Availability);
        Assert.False(session.Snapshot.CanControl);
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

        public DeviceSession CreateSession() =>
            new(Info, new FakeDriver(this), Ledger, NullLogger<DeviceSession>.Instance);
    }

    private sealed class FakeDriver : IDeviceDriver
    {
        private readonly Fixture _fixture;

        public FakeDriver(Fixture fixture) => _fixture = fixture;

        public string DriverId => "fake";

        public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([_fixture.Info]);

        public Task<IDeviceConnection> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default) =>
            _fixture.ConnectFails
                ? throw new IOException("no control channel")
                : Task.FromResult<IDeviceConnection>(_fixture.Connection);
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
        ];

        public IReadOnlyList<TransportInfo> Transports { get; } =
        [
            new() { Kind = TransportKind.UsbCable, Label = "USB", IsPresent = true, CarriesControl = true },
        ];

        public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.GetValueOrDefault(capabilityId, CapabilityValue.Absent));

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
