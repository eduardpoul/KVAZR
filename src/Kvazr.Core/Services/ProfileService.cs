using Kvazr.Core.Model;
using Kvazr.Core.Settings;

namespace Kvazr.Core.Services;

/// <summary>Applies stored profiles to the headset.</summary>
public interface IProfileService
{
    /// <summary>
    /// Writes every setting the profile carries. Returns how many were accepted, so the
    /// caller can tell "applied" from "the device refused half of it".
    /// </summary>
    Task<int> ApplyAsync(DeviceProfile profile, CancellationToken cancellationToken = default);
}

public sealed class ProfileService : IProfileService
{
    private readonly IDeviceService _device;

    public ProfileService(IDeviceService device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    public async Task<int> ApplyAsync(DeviceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!_device.State.CanControl) return 0;

        int applied = 0;

        // Ordered so that the noisiest changes land first and the gain rails last;
        // each write verifies itself, so a refusal simply does not count.
        if (profile.EqPresetIndex is { } preset)
            applied += await Try(() => _device.SetEqPresetAsync((byte)preset, cancellationToken)).ConfigureAwait(false);

        if (profile.NoiseReduction is { } noise)
            applied += await Try(() => _device.SetNoiseReductionAsync(noise, cancellationToken)).ConfigureAwait(false);

        if (profile.SidetoneEnabled is { } sidetone)
            applied += await Try(() => _device.SetSidetoneEnabledAsync(sidetone, cancellationToken)).ConfigureAwait(false);

        if (profile.SidetoneLevel is { } sidetoneLevel)
            applied += await Try(() => _device.SetSidetoneLevelAsync((byte)sidetoneLevel, cancellationToken)).ConfigureAwait(false);

        if (profile.ChatMix is { } chatMix)
            applied += await Try(() => _device.SetChatMixAsync((byte)chatMix, cancellationToken)).ConfigureAwait(false);

        if (profile.VoicePromptLevel is { } voice)
            applied += await Try(() => _device.SetVoicePromptLevelAsync((byte)voice, cancellationToken)).ConfigureAwait(false);

        if (profile.EarcupBalance is { } balance)
            applied += await Try(() => _device.SetEarcupBalanceAsync((byte)balance, cancellationToken)).ConfigureAwait(false);

        if (profile.AmpGain is { } amp)
            applied += await Try(() => _device.SetGainAsync(GainChannel.Master, (byte)amp, cancellationToken)).ConfigureAwait(false);

        if (profile.LeftGain is { } left)
            applied += await Try(() => _device.SetGainAsync(GainChannel.Left, (byte)left, cancellationToken)).ConfigureAwait(false);

        if (profile.RightGain is { } right)
            applied += await Try(() => _device.SetGainAsync(GainChannel.Right, (byte)right, cancellationToken)).ConfigureAwait(false);

        return applied;
    }

    /// <summary>
    /// A value stored before the safety rails existed — or edited by hand in the JSON —
    /// must not abort the whole profile, so a rejected write is counted as not applied.
    /// </summary>
    private static async Task<int> Try(Func<Task<bool>> write)
    {
        try
        {
            return await write().ConfigureAwait(false) ? 1 : 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }
}
