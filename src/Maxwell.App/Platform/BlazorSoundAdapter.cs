#if WINDOWS
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.Versioning;

using Avalonia.Threading;

using Maxwell.App.ViewModels.Capabilities;
using Maxwell.App.ViewModels.Devices;
using Maxwell.Blazor;

namespace Maxwell.App.Platform;

/// <summary>
/// The sound section's view models, flattened for the web-rendered experiment.
///
/// The rows wrap the same objects the native page binds to, and writes go back through
/// the same setters — so whatever the experiment shows, it is showing the same data and
/// risking nothing the native page does not. All it hides is Avalonia.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BlazorSoundSection : ISoundSectionSource
{
    public BlazorSoundSection(CapabilitySectionViewModel section, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(section);

        Heading = section.Heading;
        IsDark = isDark;
        Rows = [.. section.Items.Select(item => new BlazorSoundRow(item, Raise))];
    }

    public string Heading { get; }

    public bool IsDark { get; }

    public IReadOnlyList<ISoundRow> Rows { get; }

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();
}

[SupportedOSPlatform("windows")]
internal sealed class BlazorSoundRow : ISoundRow
{
    private readonly CapabilityViewModel _vm;

    internal BlazorSoundRow(CapabilityViewModel vm, Action changed)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, _) => changed();
    }

    public string Title => _vm.Title;

    public string? Subtitle => _vm.Subtitle;

    public string Kind => _vm switch
    {
        _ when !_vm.IsAvailable => "blocked",
        RangeCapabilityViewModel => "range",
        ChoiceCapabilityViewModel => "choice",
        _ => "readout",
    };

    public string Display => _vm switch
    {
        RangeCapabilityViewModel r => r.Display,
        ReadoutCapabilityViewModel r => r.Display,
        _ => string.Empty,
    };

    public double Minimum => (_vm as RangeCapabilityViewModel)?.Minimum ?? 0;

    public double Maximum => (_vm as RangeCapabilityViewModel)?.Maximum ?? 100;

    public double Value => (_vm as RangeCapabilityViewModel)?.Value ?? 0;

    public bool HasValue => (_vm as RangeCapabilityViewModel)?.HasValue ?? false;

    public bool CanEdit => _vm.CanEdit;

    public IReadOnlyList<string> Options =>
        _vm is ChoiceCapabilityViewModel c ? [.. c.Options.Select(o => o.Label)] : [];

    public int SelectedIndex =>
        _vm is ChoiceCapabilityViewModel { Selected: { } chosen } c ? c.Options.IndexOf(chosen) : -1;

    public string? Hint => _vm.Hint;

    public bool IsUntested => _vm.IsUntested;

    public string Unreachable => _vm.Unreachable;

    /// <summary>
    /// Blazor raises this on its own thread; the view models live on Avalonia's. Posted
    /// rather than invoked so the web side never blocks on a confirmation dialog.
    /// </summary>
    public void SetValue(double value) => Dispatcher.UIThread.Post(() =>
    {
        if (_vm is RangeCapabilityViewModel range) range.Value = value;
    });

    public void Choose(int index) => Dispatcher.UIThread.Post(() =>
    {
        if (_vm is ChoiceCapabilityViewModel choice && index >= 0 && index < choice.Options.Count)
        {
            choice.Selected = choice.Options[index];
        }
    });
}
#endif
