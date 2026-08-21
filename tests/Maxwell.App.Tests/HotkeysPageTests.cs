using Maxwell.App.Platform;
using Maxwell.App.ViewModels.Pages;
using Maxwell.Core.Abstractions;
using Maxwell.Core.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Maxwell.Core.Settings;
using Xunit;

namespace Maxwell.App.Tests;

/// <summary>
/// Three things can be wrong with a shortcut and the fix is different for each, so the
/// page has to tell them apart rather than showing one general failure. These pin that.
/// </summary>
public sealed class HotkeysPageTests
{
    private static HotkeysViewModel Build(
        out FakeSettings settings,
        out FakeHotkeys hotkeys,
        params (string Action, string Gesture)[] bindings)
    {
        settings = new FakeSettings();
        settings.Current.Hotkeys.Clear();

        foreach ((string action, string gesture) in bindings) settings.Current.Hotkeys[action] = gesture;

        hotkeys = new FakeHotkeys();

        using var registry = new DeviceRegistry(
            [], new InMemoryCapabilityLedger(), NullLoggerFactory.Instance);

        var coordinator = new HotkeyCoordinator(
            hotkeys, new PrimaryDevice(registry, new HiddenDevices(settings)), settings);

        return new HotkeysViewModel(settings, coordinator);
    }

    [Fact]
    public void EveryActionGetsARow_EvenTheUnbound()
    {
        using HotkeysViewModel page = Build(out _, out _);

        Assert.Equal(HotkeyActions.All.Count, page.Rows.Count);
        Assert.All(page.Rows, r => Assert.False(r.HasGesture));
    }

    [Fact]
    public void AnUnboundRowSaysSo_RatherThanShowingNothing()
    {
        // An empty button in a column of filled ones reads as a rendering fault. The row
        // has to say the absence is deliberate.
        using HotkeysViewModel page = Build(out _, out _);

        Assert.Equal("Не назначено", page.Rows[0].Label);
        Assert.False(page.Rows[0].HasProblem);
    }

    [Fact]
    public void TwoActionsOnOneCombination_AreBothMarked()
    {
        // Both, not one. Marking only the second makes it look as though the first is
        // fine and the second is the mistake, when either could be the one to change.
        using HotkeysViewModel page = Build(
            out _, out _,
            (HotkeyActions.ChatMixUp, "Ctrl+Alt+Up"),
            (HotkeyActions.ChatMixDown, "Ctrl+Alt+Up"));

        List<HotkeyRowViewModel> clashing = [.. page.Rows.Where(r => r.IsConflicting)];

        Assert.Equal(2, clashing.Count);
        Assert.All(clashing, r => Assert.Equal("Это сочетание уже назначено другому действию", r.Problem));
    }

    [Fact]
    public void ACombinationTheSystemRefused_IsShownAsRefused()
    {
        // Nothing about this is visible from inside the process except the refusal at
        // registration, so a row that did not say so would show a binding that silently
        // never fires.
        using HotkeysViewModel page = Build(
            out _, out FakeHotkeys hotkeys, (HotkeyActions.NoiseCycle, "Ctrl+Alt+N"));

        hotkeys.Refuse = true;

        HotkeyRowViewModel row = page.Rows.Single(r => r.Action.Id == HotkeyActions.NoiseCycle);

        // Through the row rather than straight into Capture: the refusal only becomes
        // knowable when the bindings are registered again, and that happens on write.
        row.RecordCommand.Execute(null);
        page.Capture("Ctrl+Alt+N");

        Assert.True(row.IsRejected);
        Assert.Equal("Сочетание занято другим приложением", row.Problem);
    }

    [Fact]
    public void CapturingWritesThrough_AndOnlyToTheListeningRow()
    {
        using HotkeysViewModel page = Build(out FakeSettings settings, out _);

        HotkeyRowViewModel target = page.Rows[2];
        target.RecordCommand.Execute(null);
        page.Capture("Ctrl+Shift+K");

        Assert.Equal("Ctrl+Shift+K", target.Gesture);
        Assert.Equal("Ctrl+Shift+K", settings.Current.Hotkeys[target.Action.Id]);
        Assert.All(page.Rows.Where(r => r != target), r => Assert.False(r.HasGesture));
    }

    [Fact]
    public void OnlyOneRowListensAtATime()
    {
        // Two listening rows would both take the same key press, and which one won would
        // come down to event order.
        using HotkeysViewModel page = Build(out _, out _);

        page.Rows[0].RecordCommand.Execute(null);
        page.Rows[1].RecordCommand.Execute(null);

        Assert.False(page.Rows[0].IsRecording);
        Assert.True(page.Rows[1].IsRecording);
    }

    [Fact]
    public void AbandoningACaptureChangesNothing()
    {
        using HotkeysViewModel page = Build(out _, out _, (HotkeyActions.PresetNext, "Ctrl+Alt+E"));

        HotkeyRowViewModel row = page.Rows.Single(r => r.Action.Id == HotkeyActions.PresetNext);
        row.RecordCommand.Execute(null);
        page.Stop();

        Assert.Equal("Ctrl+Alt+E", row.Gesture);
        Assert.False(page.IsRecording);
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
    }

    /// <summary>Refuses everything when asked to, which is what the real one does when
    /// another application already owns the combination.</summary>
    private sealed class FakeHotkeys : IHotkeyService
    {
        public bool Refuse { get; set; }

        // Never raised here: nothing in these tests presses a key, and the coordinator
        // subscribing is enough to keep the compiler from calling the event unused.
        public event EventHandler<string>? Triggered
        {
            add { }
            remove { }
        }

        public IReadOnlyList<string> Apply(IReadOnlyList<HotkeyBinding> bindings) =>
            Refuse ? [.. bindings.Select(b => b.ActionId)] : [];

        public void Dispose()
        {
        }
    }
}
