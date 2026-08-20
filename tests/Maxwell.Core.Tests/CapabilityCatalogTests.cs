using Maxwell.Core.Devices;
using Maxwell.Core.Drivers.Audeze;
using Maxwell.Core.Drivers.Mock;
using Xunit;

namespace Maxwell.Core.Tests;

/// <summary>
/// Invariants every driver's capability list has to hold.
///
/// These are cheap and they matter more than they look: the shell renders whatever a
/// driver declares, so a Range without bounds or a Choice without options is not a
/// crash, it is a control that silently does nothing. The checks live in one place so
/// each new driver gets them for free.
/// </summary>
public sealed class CapabilityCatalogTests
{
    public static TheoryData<string, IReadOnlyList<Capability>> Catalogs =>
        new()
        {
            { AudezeRaceDriver.Id, MaxwellCapabilities.All },
            { $"{MockDeviceDriver.Id}/mouse", MockCatalogs.Mouse },
            { $"{MockDeviceDriver.Id}/keyboard", MockCatalogs.Keyboard },
        };

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void IdsAreUnique(string driver, IReadOnlyList<Capability> catalog)
    {
        string[] duplicates = [.. catalog
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];

        Assert.True(duplicates.Length == 0, $"{driver} declares {string.Join(", ", duplicates)} more than once.");
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void RangesAreDeclared(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Primitive == CapabilityPrimitive.Range))
        {
            Assert.True(c.Range is not null, $"{driver}/{c.Id} is a Range with no bounds.");
            Assert.True(c.Range!.Min < c.Range.Max, $"{driver}/{c.Id} has an empty range.");

            foreach (int detent in c.Range.Detents)
            {
                Assert.True(
                    detent >= c.Range.Min && detent <= c.Range.Max,
                    $"{driver}/{c.Id} has a detent at {detent}, outside {c.Range.Min}..{c.Range.Max}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void ChoicesAreDeclared(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Primitive == CapabilityPrimitive.Choice))
        {
            Assert.True(c.Choices.Count > 0, $"{driver}/{c.Id} is a Choice with no options.");

            int distinct = c.Choices.Select(o => o.Index).Distinct().Count();
            Assert.True(distinct == c.Choices.Count, $"{driver}/{c.Id} has duplicate option indices.");
        }
    }

    /// <summary>
    /// A disabled control must say why beside itself. The design system forbids
    /// explaining unavailability in a page-level banner, so the driver — the only layer
    /// that knows the reason — has to supply the sentence.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void RequirementsCarryTheirExplanation(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Requirement != RequirementKind.None))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(c.RequirementHint),
                $"{driver}/{c.Id} has requirement {c.Requirement} but no hint to show next to the control.");
        }
    }

    /// <summary>
    /// A ceiling read out of the device is useless if it points at a capability nobody
    /// declares — the range would never resolve and the slider would sit at its
    /// placeholder maximum forever.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void DeviceSourcedRangesPointAtSomethingReal(string driver, IReadOnlyList<Capability> catalog)
    {
        HashSet<string> ids = [.. catalog.Select(c => c.Id)];

        foreach (Capability c in catalog.Where(c => c.Range?.Source == RangeSource.Device))
        {
            string? source = c.Range!.MaxFromCapability;

            Assert.False(string.IsNullOrEmpty(source), $"{driver}/{c.Id} claims a device-sourced range but names no source.");
            Assert.True(ids.Contains(source!), $"{driver}/{c.Id} sources its ceiling from {source}, which is not declared.");
        }
    }

    /// <summary>
    /// An interactive primitive the user cannot write is a dead switch. The exception is
    /// a capability marked <see cref="CapabilityStatus.Unavailable"/>, which exists to
    /// say "the hardware has this and we cannot reach it yet" — that one is honest.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void InteractivePrimitivesAreWritable(string driver, IReadOnlyList<Capability> catalog)
    {
        CapabilityPrimitive[] interactive =
        [
            CapabilityPrimitive.Toggle,
            CapabilityPrimitive.Range,
            CapabilityPrimitive.Choice,
            CapabilityPrimitive.Action,
        ];

        foreach (Capability c in catalog.Where(c =>
                     interactive.Contains(c.Primitive) && c.DeclaredStatus != CapabilityStatus.Unavailable))
        {
            Assert.True(c.IsWritable, $"{driver}/{c.Id} is a {c.Primitive} the user cannot write.");
        }
    }

    [Fact]
    public void MaxwellDeclaresTheDangerousOperationsAsDangerous()
    {
        Capability reset = MaxwellCapabilities.All.Single(c => c.Id == CapabilityIds.MaintFactoryReset);
        Capability pair = MaxwellCapabilities.All.Single(c => c.Id == CapabilityIds.LinkPair);

        // Both wipe state the user cannot get back, and the public protocol notes label
        // their command range "Battery/sync" — which is how a read-only sweep once came
        // within one byte of erasing the headset.
        Assert.Equal(CapabilityRisk.Hidden, reset.Risk);
        Assert.False(reset.IsReversible);
        Assert.Equal(CapabilityRisk.Confirm, pair.Risk);
        Assert.False(pair.IsReversible);
    }

    [Fact]
    public void MaxwellMicrophoneMuteIsAnIndicatorNotASwitch()
    {
        Capability mute = MaxwellCapabilities.All.Single(c => c.Id == CapabilityIds.MicMute);

        // Audeze HQ has readMicMute and no writer at all; mute moves only when the boom
        // arm is raised. Drawing a switch would promise control that does not exist.
        Assert.Equal(CapabilityAccess.Read, mute.Access);
        Assert.Equal(CapabilityPrimitive.Readout, mute.Primitive);
    }

    [Fact]
    public void MaxwellGainIsMarkedIrreversible()
    {
        foreach (string id in new[]
                 {
                     CapabilityIds.AudioVolume, CapabilityIds.AudioGainLeft, CapabilityIds.AudioGainRight,
                 })
        {
            Capability gain = MaxwellCapabilities.All.Single(c => c.Id == id);

            // The mirror register latches on the first write and only a factory reset
            // puts it back, so the warning has to come before the write.
            Assert.False(gain.IsReversible, $"{id} must warn before the first write.");
            Assert.Equal(150, gain.Range!.Max);
        }
    }
}
