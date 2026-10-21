using Kvazr.Core.Devices;
using Kvazr.Core.Drivers.Audeze;
using Kvazr.Core.Drivers.Mock;
using Xunit;

namespace Kvazr.Core.Tests;

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

    /// <summary>
    /// Members index the reading vector positionally, so a driver that skipped a number
    /// or repeated one would shift every member after it against the values it reported
    /// — a mouse whose Y axis silently showed the X reading, and nothing to notice.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void MembersAreNumberedFromZeroWithoutGaps(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Members.Count > 0))
        {
            for (int i = 0; i < c.Members.Count; i++)
            {
                Assert.True(
                    c.Members[i].Index == i,
                    $"{driver}/{c.Id} member {i} is numbered {c.Members[i].Index}.");
            }
        }
    }

    /// <summary>
    /// A composite with no parts renders as an empty frame. That is a legitimate state —
    /// Maxwell's equaliser is declared exactly so, because the firmware plainly holds one
    /// and the command that reaches it has not been found — but only when the capability
    /// says out loud that it cannot be reached. Anywhere else it is a driver that forgot
    /// to describe itself, and the user gets a labelled blank.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void CompositesDescribeTheirParts(string driver, IReadOnlyList<Capability> catalog)
    {
        CapabilityPrimitive[] composite =
        [
            CapabilityPrimitive.RangeGroup,
            CapabilityPrimitive.Curve,
            CapabilityPrimitive.KeyMap,
            CapabilityPrimitive.ButtonMap,
            CapabilityPrimitive.Lighting,
            CapabilityPrimitive.ProfileSet,
            CapabilityPrimitive.HostSlots,
        ];

        foreach (Capability c in catalog.Where(c =>
                     composite.Contains(c.Primitive) && c.DeclaredStatus != CapabilityStatus.Unavailable))
        {
            Assert.True(c.Members.Count > 0, $"{driver}/{c.Id} is a {c.Primitive} with no members to draw.");
        }
    }

    /// <summary>
    /// A range group's tracks need bounds from somewhere. Without them every channel
    /// would fall back to a placeholder 0–100 and a DPI pair would read as a percentage.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void GroupedRangesHaveBoundsPerMemberOrShared(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Primitive == CapabilityPrimitive.RangeGroup))
        {
            foreach (CapabilityMember member in c.Members)
            {
                CapabilityRange? bounds = member.Range ?? c.Range;

                Assert.True(bounds is not null, $"{driver}/{c.Id}/{member.Label} has no bounds.");
                Assert.True(bounds!.Min < bounds.Max, $"{driver}/{c.Id}/{member.Label} has an empty range.");
            }
        }
    }

    /// <summary>
    /// A button map's members are the buttons and its choices are what they can be given.
    /// A map without the vocabulary is a row of dropdowns that open on nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void ButtonMapsOfferSomethingToAssign(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Primitive == CapabilityPrimitive.ButtonMap))
        {
            Assert.True(c.Choices.Count > 0, $"{driver}/{c.Id} maps buttons to an empty list of actions.");
        }
    }

    /// <summary>
    /// Layers and columns are facts about the hardware, and a layout drawn without them
    /// puts its row breaks wherever the panel happened to be wide.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void KeyMapsDeclareTheirGeometry(string driver, IReadOnlyList<Capability> catalog)
    {
        foreach (Capability c in catalog.Where(c => c.Primitive == CapabilityPrimitive.KeyMap))
        {
            Assert.True(c.LayerCount >= 1, $"{driver}/{c.Id} declares {c.LayerCount} layers.");
            Assert.True(c.GridColumns >= 1, $"{driver}/{c.Id} declares no column count.");

            Assert.True(
                c.Members.Count % c.GridColumns == 0,
                $"{driver}/{c.Id} has {c.Members.Count} keys, which is not whole rows of {c.GridColumns}.");
        }
    }

    /// <summary>
    /// A slot the user can switch to has to be one the device could be on. Marking every
    /// slot empty would leave a list that invites filling and never selects anything.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void SlotListsHaveSomewhereToSwitchTo(string driver, IReadOnlyList<Capability> catalog)
    {
        CapabilityPrimitive[] slots = [CapabilityPrimitive.ProfileSet, CapabilityPrimitive.HostSlots];

        foreach (Capability c in catalog.Where(c => slots.Contains(c.Primitive) && c.Members.Count > 0))
        {
            Assert.True(
                c.Members.Any(m => !m.IsEmpty),
                $"{driver}/{c.Id} declares every slot empty.");
        }
    }

    [Fact]
    public void MaxwellDeclaresTheDangerousOperationsAsDangerous()
    {
        Capability reset = MaxwellCapabilities.All.Single(c => c.Id == CapabilityIds.MaintFactoryReset);

        // It wipes state the user cannot get back, and the public protocol notes label its
        // command range "Battery/sync" — which is how a read-only sweep once came within
        // one byte of erasing the headset.
        Assert.Equal(CapabilityRisk.Hidden, reset.Risk);
        Assert.False(reset.IsReversible);
    }

    /// <summary>
    /// Re-pairing is not offered. It is one byte away from the factory reset in the same
    /// command range, it undoes a link the user did not set up by hand and cannot redo
    /// without the dongle in reach, and nothing about a headset that is already paired
    /// calls for it. A row nobody needs is not worth the chance of hitting it.
    /// </summary>
    [Fact]
    public void MaxwellDoesNotOfferRePairing() =>
        Assert.DoesNotContain(MaxwellCapabilities.All, c => c.Id == CapabilityIds.LinkPair);

    /// <summary>
    /// The microphone mute and level are not offered at all, and this is here so nobody
    /// puts them back from the protocol notes without repeating the measurement.
    ///
    /// The headset does not report either one. Sub-command 0x22 answers 0xFF on both
    /// leads, boom in or out, USB audio dead or live; 0x35 never answers; a sweep of
    /// 0x00..0xFF in both read namespaces finds nothing that moves with the microphone;
    /// and ninety seconds of listening on all three HID interfaces caught fourteen
    /// reports from the volume wheel and none from the boom. Audeze HQ shows nothing
    /// either, checked against the same movement by the owner of the headset.
    /// </summary>
    /// <summary>
    /// Everything the headset can change without us is marked volatile, because an open
    /// page reads the battery and the volatile set and nothing else. Noise reduction was
    /// not marked, and the owner changed it on the headset and watched the page go on
    /// claiming the old setting for a minute.
    /// </summary>
    [Fact]
    public void WhatTheHeadsetChangesOnItsOwnIsPolledWhileThePageIsOpen()
    {
        foreach (string id in new[] { CapabilityIds.MixChat, CapabilityIds.MicNoiseReduction })
        {
            Assert.True(
                MaxwellCapabilities.All.Single(c => c.Id == id).IsVolatile,
                $"{id} can move on the headset, so it has to be polled");
        }
    }

    /// <summary>
    /// The sidetone ceiling is read to bound the level slider and is not a setting. It
    /// printed "31" under a slider already reading "9 / 31" — the same number twice, the
    /// second one looking like something to set.
    /// </summary>
    [Fact]
    public void TheSidetoneCeilingIsReadButNotShown()
    {
        Assert.True(MaxwellCapabilities.All.Single(c => c.Id == CapabilityIds.MicSidetoneMax).IsSupporting);

        // And the level still takes its maximum from it, which is the only reason it is
        // read at all.
        Capability level = MaxwellCapabilities.All.Single(c => c.Id == CapabilityIds.MicSidetoneLevel);
        Assert.Equal(CapabilityIds.MicSidetoneMax, level.Range!.MaxFromCapability);
    }

    [Fact]
    public void MaxwellOffersNoMicrophoneMuteOrLevel()
    {
        Assert.DoesNotContain(MaxwellCapabilities.All, c => c.Id == CapabilityIds.MicMute);
        Assert.DoesNotContain(MaxwellCapabilities.All, c => c.Id == CapabilityIds.MicLevel);

        // The rest of the microphone section stays: these were measured working, there
        // and back, on hardware.
        foreach (string id in new[]
                 {
                     CapabilityIds.MicNoiseReduction, CapabilityIds.MicSidetone,
                     CapabilityIds.MicSidetoneLevel,
                 })
        {
            Assert.Contains(MaxwellCapabilities.All, c => c.Id == id);
        }
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
