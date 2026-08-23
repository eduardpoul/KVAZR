using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// A device is owned by one driver and described by several sources. These cases are
/// about the seam between those two facts: the owner must keep the device, the
/// contributors must reach the page, and neither may take the other down with it.
///
/// The arrangement exists because the alternative was silently lossy. Shadowing by tier
/// is right for finders and wrong for anything that knows one thing about a device it
/// did not find — under shadowing alone, Core Audio either loses its volume rows to the
/// vendor driver or takes the whole headset away from it.
/// </summary>
public sealed class CapabilityContributorTests
{
    private const string Owned = "test.owned";
    private const string Added = "test.added";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ContributedCapabilities_AppearBesideTheOwnDriverRatherThanInsteadOfThem()
    {
        Fixture fixture = new(new FakeContributor("extra", Added));
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);
        await session.RefreshAllAsync(Ct);

        Assert.True(session.Snapshot.Has(Owned));
        Assert.True(session.Snapshot.Has(Added));
        Assert.Equal(CapabilityValue.Of(3), session.Snapshot.Capabilities[Owned].Value);
        Assert.Equal(CapabilityValue.Of(50), session.Snapshot.Capabilities[Added].Value);
    }

    [Fact]
    public async Task AWrite_GoesToWhicheverSourceDeclaredTheCapability()
    {
        var contributor = new FakeContributor("extra", Added);
        Fixture fixture = new(contributor);
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        Assert.True(await session.WriteAsync(Added, CapabilityValue.Of(70), Ct));

        // The point of the routing table: sending everything to the owning connection
        // would hand a contributor's row to a driver that has never heard of it, and the
        // refusal would be reported against the wrong device.
        Assert.Equal(CapabilityValue.Of(70), contributor.Connection.Values[Added]);
        Assert.DoesNotContain(Added, fixture.Connection.Values.Keys);
    }

    [Fact]
    public async Task WhenBothDeclareTheSameCapability_TheOwningDriverKeepsIt()
    {
        Fixture fixture = new(new FakeContributor("extra", Owned, value: 99));
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);
        await session.RefreshAllAsync(Ct);

        // One id, two claimants: their values would fight on every poll, so the owner
        // wins and the contributor's copy is dropped rather than merged.
        Assert.Equal(CapabilityValue.Of(3), session.Snapshot.Capabilities[Owned].Value);
        Assert.Single(session.Snapshot.Capabilities);
    }

    [Fact]
    public async Task AContributorThatDoesNotRecogniseTheDevice_ChangesNothing()
    {
        Fixture fixture = new(new FakeContributor("extra", Added) { Recognises = false });
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        Assert.False(session.Snapshot.Has(Added));
        Assert.True(session.Snapshot.Has(Owned));
        Assert.Equal(DeviceAvailability.Active, session.Snapshot.Availability);
    }

    [Fact]
    public async Task AContributorThatCannotOpen_IsLoggedAndTheDeviceCarriesOn()
    {
        Fixture fixture = new(new FakeContributor("extra", Added) { AttachThrows = true });
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        Assert.False(session.Snapshot.Has(Added));
        Assert.Equal(DeviceAvailability.Active, session.Snapshot.Availability);
    }

    [Fact]
    public async Task AFailingContributor_DoesNotEndTheSweepForTheOtherSources()
    {
        var contributor = new FakeContributor("extra", Added);
        Fixture fixture = new(contributor);
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);
        await session.RefreshAllAsync(Ct);

        contributor.Connection.ReadThrows = true;
        fixture.Connection.Values[Owned] = CapabilityValue.Of(4);

        await session.RefreshAllAsync(Ct);

        // With one channel per device a read failure meant the device was gone, so
        // abandoning the sweep said the same thing. It does not any more: the owning
        // driver is still answering and its reading must land.
        Assert.Equal(CapabilityValue.Of(4), session.Snapshot.Capabilities[Owned].Value);

        // The dropped source keeps its last reading rather than vanishing — a momentary
        // handle failure is not hardware losing a feature.
        Assert.Equal(CapabilityValue.Of(50), session.Snapshot.Capabilities[Added].Value);
        Assert.True(contributor.Connection.Disposed);
    }

    [Fact]
    public async Task ADroppedContributor_ReattachesOnTheNextSweep()
    {
        var contributor = new FakeContributor("extra", Added);
        Fixture fixture = new(contributor);
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        contributor.Connection.ReadThrows = true;
        await session.RefreshAllAsync(Ct);

        contributor.Reopen();
        await session.MarkPresentAsync(fixture.Info, Ct);
        await session.RefreshAllAsync(Ct);

        Assert.Equal(CapabilityValue.Of(50), session.Snapshot.Capabilities[Added].Value);
        Assert.Equal(2, contributor.Attachments);
    }

    [Fact]
    public async Task ADeviceWhoseOnlyReaderIsAContributor_IsAnsweringRatherThanUnreachable()
    {
        Fixture fixture = new(new FakeContributor("extra", Added)) { ConnectFails = true };
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        // The keyboard nobody has a protocol for, answering through the standard HID
        // battery usage. Calling it unreachable because its owning driver has nothing to
        // ask would be a lie about working hardware.
        Assert.Equal(DeviceAvailability.Active, session.Snapshot.Availability);
        Assert.True(session.Snapshot.Has(Added));
        Assert.False(session.Snapshot.Has(Owned));
    }

    [Fact]
    public async Task WhenTheDeviceGoesAway_EveryChannelIsClosed()
    {
        var contributor = new FakeContributor("extra", Added);
        Fixture fixture = new(contributor);
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);
        await session.MarkAbsentAsync(Ct);

        Assert.True(fixture.Connection.Disposed);
        Assert.True(contributor.Connection.Disposed);
    }


    [Fact]
    public async Task AnOwnerThatSupersedesAContributor_KeepsThatGroundToItself()
    {
        Fixture fixture = new(new FakeContributor("extra", Added)) { Superseded = "extra" };
        DeviceSession session = fixture.CreateSession();

        await session.MarkPresentAsync(fixture.Info, Ct);

        // A vendor protocol that answers for the device's own sound does not want the
        // Windows slider for the same hardware beside it: both numbers are real, they are
        // not the same number, and the page stops being readable. Tiering cannot express
        // this — by the time contributors run, the owner has already won the device.
        Assert.False(session.Snapshot.Has(Added));
        Assert.True(session.Snapshot.Has(Owned));
    }
    private sealed class Fixture
    {
        private readonly ICapabilityContributor[] _contributors;

        public Fixture(params ICapabilityContributor[] contributors) => _contributors = contributors;

        public InMemoryCapabilityLedger Ledger { get; } = new();

        public FakeConnection Connection { get; } = new(Owned, 3);

        public bool ConnectFails { get; init; }

        /// <summary>A contributor whose ground the owning driver says it already covers.</summary>
        public string? Superseded { get; init; }

        public DeviceInfo Info => new()
        {
            Key = "fake:device",
            DriverId = "fake",
            Vendor = "Fake",
            Model = "Device",
            Supersedes = Superseded is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal) { Superseded },
        };

        public DeviceSession CreateSession() =>
            new(Info, new FakeDriver(this), Ledger, NullLogger<DeviceSession>.Instance, _contributors);

        private sealed class FakeDriver : IDeviceDriver
        {
            private readonly Fixture _fixture;

            public FakeDriver(Fixture fixture) => _fixture = fixture;

            public string DriverId => "fake";

            public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<DeviceInfo>>([_fixture.Info]);

            public Task<IDeviceConnection> ConnectAsync(
                DeviceInfo device,
                CancellationToken cancellationToken = default) =>
                _fixture.ConnectFails
                    ? throw new IOException("no control channel")
                    : Task.FromResult<IDeviceConnection>(_fixture.Connection);
        }
    }

    private sealed class FakeContributor : ICapabilityContributor
    {
        private readonly string _capabilityId;
        private readonly int _value;

        public FakeContributor(string id, string capabilityId, int value = 50)
        {
            ContributorId = id;
            _capabilityId = capabilityId;
            _value = value;
            Connection = new FakeConnection(capabilityId, value);
        }

        public string ContributorId { get; }

        public FakeConnection Connection { get; private set; }

        public bool Recognises { get; init; } = true;

        public bool AttachThrows { get; init; }

        /// <summary>How many times this contributor has handed over a channel.</summary>
        public int Attachments { get; private set; }

        public void Reopen() => Connection = new FakeConnection(_capabilityId, _value);

        public Task<IDeviceConnection?> AttachAsync(
            DeviceInfo device,
            CancellationToken cancellationToken = default)
        {
            if (AttachThrows) throw new IOException("endpoint held by someone else");
            if (!Recognises) return Task.FromResult<IDeviceConnection?>(null);

            Attachments++;
            return Task.FromResult<IDeviceConnection?>(Connection);
        }
    }

    private sealed class FakeConnection : IDeviceConnection
    {
        public FakeConnection(string capabilityId, int value)
        {
            Values = new Dictionary<string, CapabilityValue>(StringComparer.Ordinal)
            {
                [capabilityId] = CapabilityValue.Of(value),
            };

            Capabilities =
            [
                new()
                {
                    Id = capabilityId,
                    Domain = CapabilityDomain.Audio,
                    Primitive = CapabilityPrimitive.Range,
                    Title = capabilityId,
                    Range = new CapabilityRange { Min = 0, Max = 100 },
                },
            ];
        }

        public Dictionary<string, CapabilityValue> Values { get; }

        public IReadOnlyList<Capability> Capabilities { get; }

        public IReadOnlyList<TransportInfo> Transports { get; } =
        [
            new() { Kind = TransportKind.UsbCable, Label = "USB", IsPresent = true, CarriesControl = true },
        ];

        public bool ReadThrows { get; set; }

        public bool Disposed { get; private set; }

        public Task<CapabilityValue> ReadAsync(string capabilityId, CancellationToken cancellationToken = default) =>
            ReadThrows
                ? throw new IOException("handle is finished")
                : Task.FromResult(Values.GetValueOrDefault(capabilityId, CapabilityValue.Absent));

        public Task<bool> WriteAsync(
            string capabilityId,
            CapabilityValue value,
            CancellationToken cancellationToken = default)
        {
            Values[capabilityId] = value;
            return Task.FromResult(true);
        }

        public void Dispose() => Disposed = true;
    }
}
