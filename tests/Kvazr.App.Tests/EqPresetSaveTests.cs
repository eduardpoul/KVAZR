using Kvazr.App.Infrastructure;
using Kvazr.App.ViewModels.Capabilities;
using Kvazr.Core.Settings;
using Xunit;

using static Kvazr.App.Tests.Harness;

namespace Kvazr.App.Tests;

/// <summary>
/// Saving a curve under a name, and getting it back.
///
/// The headset keeps four user slots and no room for a name in any of them, so a named
/// curve is ours: it lives in the settings file and is applied by sending the ten bands,
/// exactly as a drag does. That choice is what these cases pin down — a saved curve
/// becomes the new "unchanged", so the row stops calling itself edited and a revert comes
/// back to the saved shape rather than to whichever headset preset was underneath it.
/// </summary>
public sealed class EqPresetSaveTests
{
    private static (CurveCapabilityViewModel Curve, CurvePresetLibrary Library, Store Store) Build(
        params int[] gains)
    {
        var store = new Store();
        var library = new CurvePresetLibrary(store);
        var session = new Recorder();

        var curve = (CurveCapabilityViewModel)CapabilityViewModelFactory.Create(Curve(gains), session);
        curve.Library = library;

        return (curve, library, store);
    }

    [Fact]
    public void ANamedCurveIsStoredAndStopsCountingAsAnEdit()
    {
        Bind();

        (CurveCapabilityViewModel curve, CurvePresetLibrary library, Store store) = Build(0, 0, 0);
        Pump();

        curve.MovePointCommand.Execute(new CurvePoint(1, 0));
        Assert.True(curve.IsDirty);

        curve.BeginSaveCommand.Execute(null);
        Assert.True(curve.IsNaming);

        // Prefilled, because a name box that opens empty is a name box that gets an empty
        // name typed into it.
        Assert.NotEmpty(curve.PendingName);

        curve.PendingName = "Мой рок";
        curve.ConfirmSaveCommand.Execute(null);

        Assert.False(curve.IsNaming);
        Assert.Equal(1, store.Saves);

        EqCurvePreset saved = Assert.Single(library.For(3));
        Assert.Equal("Мой рок", saved.Name);
        Assert.Equal([0, -12, 0], saved.Bands);

        // The saved curve is what "unchanged" means now, so nothing is outstanding.
        Assert.False(curve.IsDirty);
        Assert.Contains(curve.Presets, p => p is { IsSaved: true, Label: "Мой рок" });
    }

    [Fact]
    public void PickingASavedCurvePutsItOnTheDeviceAndBecomesWhatRevertGoesBackTo()
    {
        Bind();

        (CurveCapabilityViewModel curve, CurvePresetLibrary library, _) = Build(0, 0, 0);
        Pump();

        library.Save("Мой рок", [3, -6, 9]);

        int index = curve.Presets.ToList().FindIndex(p => p.IsSaved && p.Label == "Мой рок");
        Assert.True(index >= 0);

        curve.ChoosePresetCommand.Execute(index);
        Pump();

        Assert.Equal([3, -6, 9], curve.Values);
        Assert.False(curve.IsDirty);
        Assert.True(curve.CanDelete);
        Assert.Equal(index, curve.SelectedPresetIndex);

        // Edited, then reverted: back to the saved curve, not to the flat line it was
        // read from. The saved curve is the baseline from the moment it is applied.
        curve.MovePointCommand.Execute(new CurvePoint(0, 1));
        Assert.True(curve.IsDirty);

        curve.RevertCommand.Execute(null);
        Assert.Equal([3, -6, 9], curve.Values);
    }

    [Fact]
    public void DeletingAPresetGoesBackToTheHeadsetsFirstPreset()
    {
        Bind();

        var library = new CurvePresetLibrary(new Store());
        var session = new Recorder();

        var curve = (CurveCapabilityViewModel)CapabilityViewModelFactory.Create(Curve(3, 2, 0), session);
        var slots = (ChoiceCapabilityViewModel)CapabilityViewModelFactory.Create(
            Presets(2, "Audeze", "Treble Boost", "Bass Boost"), session);

        curve.Slots = slots;
        curve.Library = library;
        Pump();

        library.Save("Мой рок", [3, -6, 9]);
        curve.ChoosePresetCommand.Execute(curve.Presets.ToList().FindIndex(p => p.IsSaved));
        Pump();

        Assert.Equal([3, -6, 9], curve.Values);
        Assert.Equal("Bass Boost", slots.Selected!.Label);

        curve.DeletePresetCommand.Execute(null);
        Pump();

        Assert.Empty(library.For(3));
        Assert.False(curve.CanDelete);

        // Not Bass Boost. A saved curve is ten absolute gains and remembers nothing of
        // what was underneath it, so the preset that happened to be selected the day it
        // was saved is not a place to land the wearer.
        Assert.Equal("Audeze", slots.Selected!.Label);
    }

    [Fact]
    public void DeletingAPresetWithNoHeadsetPresetsGoesBackToTheCurveUnderneath()
    {
        Bind();

        (CurveCapabilityViewModel curve, CurvePresetLibrary library, _) = Build(2, 2, 2);
        Pump();

        library.Save("Мой рок", [3, -6, 9]);
        curve.ChoosePresetCommand.Execute(
            curve.Presets.ToList().FindIndex(p => p.IsSaved && p.Label == "Мой рок"));
        Pump();

        Assert.Equal([3, -6, 9], curve.Values);

        curve.DeletePresetCommand.Execute(null);

        Assert.Empty(library.For(3));
        Assert.False(curve.CanDelete);
        Assert.DoesNotContain(curve.Presets, p => p.IsSaved);

        // Nothing to select, so the curve the plot held before the saved one is the only
        // place to go. Leaving the deleted shape playing was the first answer here and it
        // left the row naming one thing while the drivers did another.
        Assert.Equal([2, 2, 2], curve.Values);
        Assert.False(curve.IsDirty);
    }

    [Fact]
    public void SavingOverALoadedNameReplacesItRatherThanMakingASecond()
    {
        Bind();

        (CurveCapabilityViewModel curve, CurvePresetLibrary library, _) = Build(0, 0, 0);
        Pump();

        library.Save("Мой рок", [1, 1, 1]);
        curve.ChoosePresetCommand.Execute(
            curve.Presets.ToList().FindIndex(p => p.IsSaved && p.Label == "Мой рок"));
        Pump();

        curve.MovePointCommand.Execute(new CurvePoint(2, 1));
        curve.BeginSaveCommand.Execute(null);

        // The box opens on the name that is loaded, so pressing Enter edits the preset
        // instead of quietly making "Мой пресет" beside it.
        Assert.Equal("Мой рок", curve.PendingName);

        curve.ConfirmSaveCommand.Execute(null);

        EqCurvePreset saved = Assert.Single(library.For(3));
        Assert.Equal([1, 1, 12], saved.Bands);
    }
}
