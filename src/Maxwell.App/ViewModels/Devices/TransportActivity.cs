using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Devices;

/// <summary>A transport chip: its short name and whether it is the channel in use.</summary>
public sealed record TransportPill(string Label, bool IsLive);

/// <summary>
/// Which of a device's present channels counts as the one actually working.
///
/// A Maxwell paired over Bluetooth while its dongle is plugged in shows both chips, and
/// painting both accent claimed both were doing the job. The channel that carries
/// control is the one this application is using; failing that, the one carrying audio;
/// and for a device where nothing claims either, every present channel stays lit —
/// there is no knowledge to grey one out by.
/// </summary>
public static class TransportActivity
{
    public static bool IsLive(DeviceInfo info, TransportInfo transport)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(transport);

        if (!transport.IsPresent) return false;

        bool anyControl = info.Transports.Any(t => t.IsPresent && t.CarriesControl);
        if (anyControl) return transport.CarriesControl;

        bool anyAudio = info.Transports.Any(t => t.IsPresent && t.CarriesAudio);
        if (anyAudio) return transport.CarriesAudio;

        return true;
    }
}
