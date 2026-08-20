using Maxwell.App.Platform.Windows;
using Maxwell.Core.Model;
using Maxwell.Core.Services;
using Maxwell.Core.Settings;

namespace Maxwell.App.Platform;

/// <summary>
/// Turns global shortcuts into device commands. Sits between the raw hotkey service and
/// <see cref="IDeviceService"/> so neither has to know about the other, and so the
/// bindings can be re-applied whenever settings change.
/// </summary>
public sealed class HotkeyCoordinator : IDisposable
{
    private readonly IHotkeyService _hotkeys;
    private readonly IDeviceService _device;
    private readonly ISettingsStore _settings;

    private bool _disposed;

    /// <summary>Raised when the user asks for the window, which only the shell can honour.</summary>
    public event EventHandler? WindowRequested;

    /// <summary>Actions that could not be bound, usually because another app owns the combination.</summary>
    public IReadOnlyList<string> Rejected { get; private set; } = [];

    public HotkeyCoordinator(IHotkeyService hotkeys, IDeviceService device, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);

        _hotkeys = hotkeys;
        _device = device;
        _settings = settings;

        _hotkeys.Triggered += OnTriggered;
        _settings.Changed += OnSettingsChanged;
    }

    public void Apply()
    {
        List<HotkeyBinding> bindings = _settings.Current.Hotkeys
            .Where(pair => HotkeyActions.ById(pair.Key) is not null)
            .Select(pair => new HotkeyBinding(pair.Key, pair.Value))
            .ToList();

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

        // Everything else needs a cable; over the dongle the headset answers nothing.
        if (!_device.State.CanControl) return;

        _ = HandleAsync(actionId);
    }

    private async Task HandleAsync(string actionId)
    {
        DeviceState state = _device.State;

        switch (actionId)
        {
            case HotkeyActions.ChatMixUp when state.ChatMix >= 0:
                await _device.SetChatMixAsync(
                    (byte)Math.Min(state.ChatMix + 1, 20)).ConfigureAwait(false);
                break;

            case HotkeyActions.ChatMixDown when state.ChatMix >= 0:
                await _device.SetChatMixAsync(
                    (byte)Math.Max(state.ChatMix - 1, 0)).ConfigureAwait(false);
                break;

            case HotkeyActions.NoiseCycle when state.NoiseReduction is { } mode:
                await _device.SetNoiseReductionAsync(Next(mode)).ConfigureAwait(false);
                break;

            case HotkeyActions.SidetoneToggle when state.SidetoneEnabled is { } enabled:
                await _device.SetSidetoneEnabledAsync(!enabled).ConfigureAwait(false);
                break;

            case HotkeyActions.PresetNext when state.EqPresetIndex >= 0:
                await _device.SetEqPresetAsync(
                    (byte)((state.EqPresetIndex + 1) % EqPresets.All.Count)).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static NoiseReductionMode Next(NoiseReductionMode mode) => mode switch
    {
        NoiseReductionMode.Off => NoiseReductionMode.Low,
        NoiseReductionMode.Low => NoiseReductionMode.High,
        _ => NoiseReductionMode.Off,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hotkeys.Triggered -= OnTriggered;
        _settings.Changed -= OnSettingsChanged;
    }
}
