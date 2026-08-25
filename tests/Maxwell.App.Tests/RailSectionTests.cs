using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.ViewModels.Devices;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// The list of sections beside a device page, and the one thing it has to keep up with.
///
/// A section is greyed and marked when nothing in it can be reached, so that nobody walks
/// into a page of dead controls. That is drawn from the rows, and the rows announce their
/// own availability — but nothing here was listening, so the rail was drawn once when the
/// page was built and then kept whatever it had said. Switching the headset on filled its
/// page in while the rail beside it still called it unreachable, and the only cure was to
/// leave the page and come back.
/// </summary>
public sealed class RailSectionTests
{
    private const string Hint = "The headset is not answering — switch it on";

    private static CapabilityState Row(bool available) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.AudioVolume,
            Domain = CapabilityDomain.Audio,
            Primitive = CapabilityPrimitive.Range,
            Title = "Master volume",
            Range = new CapabilityRange { Min = 0, Max = 150 },
            Requirement = RequirementKind.ControlChannel,
            RequirementHint = Hint,
        },
        Value = available ? CapabilityValue.Of(100) : CapabilityValue.Absent,
        IsAvailable = available,
    };

    private static (CapabilitySectionViewModel Section, CapabilityViewModel Row) Build(bool available)
    {
        CapabilityViewModel row = CapabilityViewModelFactory.Create(Row(available), null);

        return (new CapabilitySectionViewModel(CapabilityDomain.Audio, [row]), row);
    }

    [Fact]
    public void WhenTheDeviceWakes_TheRailSaysSo()
    {
        (CapabilitySectionViewModel section, CapabilityViewModel row) = Build(available: false);

        List<string?> raised = [];
        section.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(section.IsReachable);

        row.Apply(Row(available: true));

        Assert.True(section.IsReachable);
        Assert.Contains(nameof(CapabilitySectionViewModel.IsReachable), raised);
    }

    [Fact]
    public void WhenTheDeviceGoesQuiet_TheRailSaysThatToo()
    {
        (CapabilitySectionViewModel section, CapabilityViewModel row) = Build(available: true);

        List<string?> raised = [];
        section.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.Apply(Row(available: false));

        Assert.False(section.IsReachable);
        Assert.Equal(Hint, section.Reason);
        Assert.Contains(nameof(CapabilitySectionViewModel.Reason), raised);
    }

    [Fact]
    public void AReachableSection_HasNothingToSayAboutItself()
    {
        (CapabilitySectionViewModel section, _) = Build(available: true);

        // Null rather than empty, because the rail hangs a tooltip on it: an empty string
        // is a tooltip, and an empty tooltip is a grey box that follows the pointer.
        Assert.Null(section.Aside);
        Assert.Empty(section.Reason);
    }

    [Fact]
    public void ADisposedSection_StopsListening()
    {
        (CapabilitySectionViewModel section, CapabilityViewModel row) = Build(available: false);

        bool raised = false;
        section.PropertyChanged += (_, _) => raised = true;

        // A page is rebuilt by disposing its old sections while the device carries on
        // reporting. A section still subscribed at that point answers for a list it has
        // already emptied, and every rebuild leaves another one behind it.
        section.Dispose();
        row.Apply(Row(available: true));

        Assert.False(raised);
        Assert.Empty(section.Items);
    }
}
