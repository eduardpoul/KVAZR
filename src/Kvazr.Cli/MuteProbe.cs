using System.Runtime.Versioning;

using Kvazr.Race.Abstractions;
using Kvazr.Race.Windows;

namespace Kvazr.Cli;

/// <summary>
/// Whether a microphone's mute can actually be written, and whether it stays written.
///
/// The switch on the page reports success and the row goes back to "on" a moment later,
/// which has three possible causes and they are not distinguishable from the interface:
/// the endpoint refuses the write, or it takes it and something else undoes it, or it
/// takes it and the reading comes from somewhere else entirely. So this writes, reads
/// straight back, waits, and reads again.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MuteProbe
{
    /// <param name="leave">
    /// Leave the switch where it was written instead of putting it back, so the
    /// interface can be looked at against a microphone that really is off.
    /// </param>
    public static async Task<int> RunAsync(bool leave = false)
    {
        var endpoints = new WindowsAudioEndpoints();

        List<AudioEndpoint> capture =
        [
            .. endpoints.Enumerate()
                .Where(e => e.Flow == AudioFlow.Capture && !e.IsContainerless)
                .OrderBy(e => e.Name, StringComparer.Ordinal),
        ];

        if (capture.Count == 0)
        {
            Console.WriteLine("Микрофонов не найдено.");
            return 1;
        }

        foreach (AudioEndpoint endpoint in capture)
        {
            Console.WriteLine(endpoint.Name);

            using IAudioEndpointChannel channel = endpoints.Open(endpoint.Id);

            bool? before = channel.Mute;
            Console.WriteLine($"  было           mute {Say(before)}   громкость {channel.Volume?.ToString() ?? "—"}%");

            bool wanted = before is not true;

            bool accepted = channel.SetMute(wanted);
            Console.WriteLine($"  пишем {Say(wanted),-4}     принято {(accepted ? "да" : "НЕТ")}");

            Console.WriteLine($"  сразу после    mute {Say(channel.Mute)}");

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Console.WriteLine($"  через 2 с      mute {Say(channel.Mute)}");

            // Put it back the way it was found. A probe that leaves somebody's microphone
            // muted is worse than no probe — unless it was asked to.
            if (!leave && before is { } original)
            {
                channel.SetMute(original);
                Console.WriteLine($"  вернули        mute {Say(channel.Mute)}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static string Say(bool? value) => value switch
    {
        true => "да",
        false => "нет",
        _ => "—",
    };
}
