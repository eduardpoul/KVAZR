using System.Collections.ObjectModel;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;

using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// Artboard 7b. What this is, what version of it, what firmware the attached devices are
/// running, and who else's work is inside it.
///
/// The last of those is not decoration. The build ships photographs of devices made by
/// companies with no connection to this project, and the artboard's own disclaimer line
/// at the foot of the page is the reason it can do that comfortably. Attribution belongs
/// on a screen a person can reach rather than in a file only the repository sees.
/// </summary>
public sealed class AboutViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceRegistry _registry;
    private bool _disposed;

    public AboutViewModel(IDeviceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
        _registry.DevicesChanged += OnDevicesChanged;
        Refresh();
    }

    public string Title => Maxwell.App.Localization.Localizer.Current["page.about", "О программе"];

    public string Name => "KVAZR";

    /// <summary>
    /// Version and runtime, from the assembly rather than from a constant. A version
    /// number that has to be remembered separately is one that will eventually disagree
    /// with the build it is printed on.
    /// </summary>
    public string Version
    {
        get
        {
            Assembly assembly = typeof(AboutViewModel).Assembly;

            string version = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "—";

            // The build metadata suffix is a commit hash and means nothing on this screen.
            int plus = version.IndexOf('+', StringComparison.Ordinal);
            if (plus > 0) version = version[..plus];

            return Maxwell.App.Localization.Localizer.Current.Say(
                "Версия {0} · .NET {1} · Avalonia", version, Environment.Version.Major);
        }
    }

    /// <summary>Firmware of everything attached, as the artboard lists it.</summary>
    public ObservableCollection<FirmwareRow> Firmware { get; } = [];

    public bool HasFirmware => Firmware.Count > 0;

    /// <summary>
    /// The line the artboard puts at the foot of the page, and the reason the device
    /// photographs in this build are not a problem: it says plainly that naming somebody
    /// else's product is not a claim to be connected with them.
    /// </summary>
    public string Disclaimer =>
        Maxwell.App.Localization.Localizer.Current.Say("KVAZR не связан с Audeze, Logitech, NuPhy, HyperX и другими производителями. ") +
        Maxwell.App.Localization.Localizer.Current.Say("Названия устройств и торговые марки принадлежат их владельцам.");

    private void OnDevicesChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        Firmware.Clear();

        foreach (DeviceSnapshot device in _registry.Devices.Where(d => d.IsPresent))
        {
            if (device.Get(CapabilityIds.DeviceFirmware)?.Value is not TextValue { Value.Length: > 0 } version)
            {
                continue;
            }

            Firmware.Add(new FirmwareRow(
                Maxwell.App.Localization.Localizer.Current.Say(device.Info.DisplayName),
                version.Value,
                device.UpdatedAt is { } at
                    ? Maxwell.App.Localization.Localizer.Current.Say("проверено {0}", at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))
                    : string.Empty));
        }

        OnPropertyChanged(nameof(HasFirmware));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registry.DevicesChanged -= OnDevicesChanged;
    }
}

/// <summary>One device and the firmware it is running.</summary>
/// <param name="Device">Display name.</param>
/// <param name="Version">Exactly what the device reported, not a tidied form of it.</param>
/// <param name="Checked">When it was last read, or empty when it has not been.</param>
public sealed record FirmwareRow(string Device, string Version, string Checked)
{
    public bool HasChecked => Checked.Length > 0;
}
