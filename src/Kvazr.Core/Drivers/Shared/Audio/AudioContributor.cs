using Kvazr.Core.Devices;
using Kvazr.Core.Drivers.System;
using Kvazr.Race.Abstractions;

namespace Kvazr.Core.Drivers.Audio;

/// <summary>
/// Gives every audio device its volume, wherever it came from.
///
/// A contributor rather than a driver, because Core Audio knows one thing about a device
/// and nothing else: it cannot name it, class it, or say how it is attached. As a driver
/// it would have to invent all three, and it would then shadow — or be shadowed by — the
/// driver that actually understands the hardware. As a contributor it simply adds rows
/// to whatever the owner found.
///
/// Matching is on hardware ids rather than on the container alone, and that distinction
/// is load-bearing. The system tier keys devices by container and would match either
/// way; the Audeze driver reports only usb ids, because it found the headset through its
/// HID interface and never looked at a container. Recomputing the endpoint container's
/// full id set — container, usb, bluetooth — and overlapping it against the device's own
/// is what makes the volume rows land on both.
/// </summary>
public sealed class AudioContributor : ICapabilityContributor
{
    private readonly IAudioEndpoints _endpoints;
    private readonly ISystemDeviceEnumerator _system;

    public AudioContributor(IAudioEndpoints endpoints, ISystemDeviceEnumerator system)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(system);

        _endpoints = endpoints;
        _system = system;
    }

    public string ContributorId => "audio";

    public Task<IDeviceConnection?> AttachAsync(
        DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.HardwareIds.Count == 0) return Task.FromResult<IDeviceConnection?>(null);

        List<AudioEndpoint> mine = [.. Mine(device)];
        if (mine.Count == 0) return Task.FromResult<IDeviceConnection?>(null);

        IReadOnlyList<AudioRow> rows = AudioCapabilities.Rows(mine, device);

        return Task.FromResult<IDeviceConnection?>(AudioConnection.Open(_endpoints, rows, out _));
    }

    /// <summary>Endpoints that belong to this device, by hardware id.</summary>
    private IEnumerable<AudioEndpoint> Mine(DeviceInfo device)
    {
        Dictionary<Guid, HashSet<string>> byContainer = _system.Enumerate()
            .GroupBy(n => n.ContainerId)
            .ToDictionary(g => g.Key, g => SystemDeviceProfile.HardwareIds(g.Key, g));

        foreach (AudioEndpoint endpoint in _endpoints.Enumerate())
        {
            // Virtual cables, capture software and streaming sinks all share the
            // container that means "no container". Treating it as real would merge every
            // one of them into a single imaginary peripheral and hang its volume off
            // whichever device matched first.
            if (endpoint.IsContainerless) continue;

            if (!byContainer.TryGetValue(endpoint.ContainerId, out HashSet<string>? ids))
            {
                // An endpoint whose container the system enumerator did not report. Its
                // container id alone is still worth trying — the system tier keys devices
                // by exactly that.
                ids = [$"container:{endpoint.ContainerId:D}"];
            }

            if (ids.Overlaps(device.HardwareIds)) yield return endpoint;
        }
    }
}
