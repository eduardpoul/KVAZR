using Maxwell.App.ViewModels.Capabilities;
using Maxwell.Core.Devices;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// A dropdown whose list is itself a setting.
///
/// Most options are a fixed menu — three noise-reduction strengths, four report rates —
/// and a control built once is right forever. A mouse's stored resolutions are not: they
/// are the options of its resolution row and are also a row of their own, editable. Built
/// once, the row went on offering a step the mouse no longer had.
/// </summary>
public sealed class ChoiceOptionsTests
{
    private static CapabilityState Offering(IReadOnlyList<int> options, int picked) => new()
    {
        Descriptor = new Capability
        {
            Id = CapabilityIds.PointerDpi,
            Domain = CapabilityDomain.Pointing,
            Primitive = CapabilityPrimitive.Choice,
            Title = "Resolution",
            Access = CapabilityAccess.ReadWrite,
            Choices = [new(1, "placeholder")],
            ChoicesFromCapability = CapabilityIds.PointerDpiSteps,
        },

        // Resolved by the session from the ladder's reading, which is what the control
        // has to follow.
        Choices = [.. options.Select(dpi => new CapabilityChoice(dpi, $"{dpi} DPI"))],
        Value = CapabilityValue.Choice(picked),
    };

    private static ChoiceCapabilityViewModel Build(CapabilityState state) =>
        (ChoiceCapabilityViewModel)CapabilityViewModelFactory.Create(state, null);

    [Fact]
    public void TheListIsTheResolvedOne_NotTheDeclaredPlaceholder()
    {
        ChoiceCapabilityViewModel vm = Build(Offering([800, 1200, 1600], picked: 800));

        Assert.Equal([800, 1200, 1600], vm.Options.Select(o => o.Index));
        Assert.Equal(800, vm.Selected?.Index);
    }

    [Fact]
    public void WhenTheListMoves_TheControlMovesWithIt()
    {
        ChoiceCapabilityViewModel vm = Build(Offering([800, 1200, 3200], picked: 800));

        vm.Apply(Offering([800, 1200, 5000], picked: 800));

        Assert.Equal([800, 1200, 5000], vm.Options.Select(o => o.Index));
        Assert.Equal(800, vm.Selected?.Index);
    }

    /// <summary>
    /// The collection is rebuilt in place rather than replaced, because the control is
    /// bound to that instance and a new one would leave it drawing the old list.
    /// </summary>
    [Fact]
    public void TheCollectionIsTheSameOne_Rebuilt()
    {
        ChoiceCapabilityViewModel vm = Build(Offering([800, 1200], picked: 800));
        object was = vm.Options;

        vm.Apply(Offering([400, 800], picked: 800));

        Assert.Same(was, vm.Options);
        Assert.Equal([400, 800], vm.Options.Select(o => o.Index));
    }

    /// <summary>
    /// A reading that names a step the list no longer has. Nothing selected is the honest
    /// answer — better than pointing at a neighbouring value the device is not running.
    /// </summary>
    [Fact]
    public void AValueThatIsNotOnTheListAnyMore_SelectsNothing()
    {
        ChoiceCapabilityViewModel vm = Build(Offering([800, 1200], picked: 800));

        vm.Apply(Offering([1200, 1600], picked: 800));

        Assert.Null(vm.Selected);
    }

    [Fact]
    public void AnUnresolvedList_LeavesTheDeclaredOneAlone()
    {
        CapabilityState waiting = Offering([800], picked: 800) with { Choices = [] };

        ChoiceCapabilityViewModel vm = Build(waiting);

        Assert.Equal([1], vm.Options.Select(o => o.Index));
    }
}
