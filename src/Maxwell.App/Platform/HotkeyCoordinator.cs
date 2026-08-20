using Maxwell.App.Platform.Windows;
using Maxwell.Core.Devices;
using Maxwell.Core.Settings;

namespace Maxwell.App.Platform;

/// <summary>
/// Turns global shortcuts into capability writes.
///
/// Written against capabilities rather than against one headset, which makes the same
/// five shortcuts work on anything that declares the matching capability: a shortcut
/// that steps ChatMix drives an Audeze and a Logitech identically, and does nothing at
/// all on a mouse that has no such thing.
/// </summary>
public sealed class HotkeyCoordinator : IDisposable
{
    private readonly IHotkeyService _hotkeys;
    private readonly PrimaryDevice _primary;
    private readonly ISettingsStore _settings;

    private bool _disposed;

    /// <summary>Raised when the user asks for the window, which only the shell can honour.</summary>
    public event EventHandler? WindowRequested;

    /// <summary>Actions that could not be bound, usually because another app owns the combination.</summary>
    public IReadOnlyList<string> Rejected { get; private set; } = [];

    public HotkeyCoordinator(IHotkeyService hotkeys, PrimaryDevice primary, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(settings);

        _hotkeys = hotkeys;
        _primary = primary;
        _settings = settings;

        _hotkeys.Triggered += OnTriggered;
        _settings.Changed += OnSettingsChanged;
    }

    public void Apply()
    {
        List<HotkeyBinding> bindings =
        [
            .. _settings.Current.Hotkeys
                .Where(pair => HotkeyActions.ById(pair.Key) is not null)
                .Select(pair => new HotkeyBinding(pair.Key, pair.Value)),
        ];

        Rejected = _hotkeys.Apply(bindings);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => Apply();

    private void OnTriggered(object? sender, string actionId)
    {
        if (string.Equals(actionId, HotkeyActions.WindowShow, StringComparison.Ordinal))
        {
            WindowRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        DeviceSnapshot? snapshot = _primary.Snapshot;
        IDeviceSession? session = _primary.Session;

        // Everything else needs a channel that answers. Over a dongle the headset is
        // silent, and a shortcut that quietly does nothing is better than one that
        // pretends to have worked.
        if (snapshot is null || session is null || !snapshot.CanControl) return;

        _ = HandleAsync(actionId, snapshot, session);
    }

    private static async Task HandleAsync(string actionId, DeviceSnapshot device, IDeviceSession session)
    {
        switch (actionId)
        {
            case HotkeyActions.ChatMixUp:
                await StepAsync(device, session, CapabilityIds.MixChat, +1).ConfigureAwait(false);
                break;

            case HotkeyActions.ChatMixDown:
                await StepAsync(device, session, CapabilityIds.MixChat, -1).ConfigureAwait(false);
                break;

            case HotkeyActions.NoiseCycle:
                await CycleAsync(device, session, CapabilityIds.MicNoiseReduction).ConfigureAwait(false);
                break;

            case HotkeyActions.SidetoneToggle:
                await FlipAsync(device, session, CapabilityIds.MicSidetone).ConfigureAwait(false);
                break;

            case HotkeyActions.PresetNext:
                await CycleAsync(device, session, CapabilityIds.AudioEqPreset).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    /// <summary>Moves a range by one step, clamped to the bounds the device reported.</summary>
    private static async Task StepAsync(
        DeviceSnapshot device,
        IDeviceSession session,
        string capabilityId,
        int direction)
    {
        if (device.Get(capabilityId) is not { CanWrite: true } state) return;
        if (state.Value is not NumberValue current || state.Range is not { } range) return;

        int next = Math.Clamp(current.Value + (Math.Max(1, range.Step) * direction), range.Min, range.Max);
        if (next == current.Value) return;

        await session.WriteAsync(capabilityId, CapabilityValue.Of(next)).ConfigureAwait(false);
    }

    private static async Task CycleAsync(DeviceSnapshot device, IDeviceSession session, string capabilityId)
    {
        if (device.Get(capabilityId) is not { CanWrite: true } state) return;

        IReadOnlyList<CapabilityChoice> choices = state.Descriptor.Choices;
        if (choices.Count == 0) return;

        int current = state.Value.AsChoiceIndex();
        int position = 0;

        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].Index == current) position = i;
        }

        int next = choices[(position + 1) % choices.Count].Index;

        await session.WriteAsync(capabilityId, CapabilityValue.Choice(next)).ConfigureAwait(false);
    }

    private static async Task FlipAsync(DeviceSnapshot device, IDeviceSession session, string capabilityId)
    {
        if (device.Get(capabilityId) is not { CanWrite: true } state) return;
        if (state.Value is not FlagValue current) return;

        await session.WriteAsync(capabilityId, CapabilityValue.Of(!current.Value)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hotkeys.Triggered -= OnTriggered;
        _settings.Changed -= OnSettingsChanged;
    }
}
