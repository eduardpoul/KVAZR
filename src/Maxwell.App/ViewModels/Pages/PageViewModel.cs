using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.Infrastructure;
using Maxwell.Core.Model;
using Maxwell.Core.Services;

namespace Maxwell.App.ViewModels.Pages;

/// <summary>
/// Base for every navigable page. Owns the state subscription and a debouncer, so a
/// page never has to remember to marshal to the UI thread or to throttle its writes.
/// </summary>
public abstract partial class PageViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(350);

    private readonly Debouncer _debouncer = new(WriteDelay);
    private bool _disposed;

    /// <summary>Suppresses device writes while a property is being set from a device read.</summary>
    private bool _applyingFromDevice;

    protected IDeviceService Device { get; }

    [ObservableProperty]
    public partial DeviceState State { get; set; } = DeviceState.Empty;

    public abstract string Title { get; }

    /// <summary>True when the page is meaningless without a cable.</summary>
    public virtual bool RequiresControl => true;

    public bool IsAvailable => !RequiresControl || State.CanControl;

    protected PageViewModel(IDeviceService device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Device = device;
        Device.StateChanged += OnDeviceStateChanged;
        ApplyState(device.State);
    }

    private void OnDeviceStateChanged(object? sender, DeviceState state) =>
        Dispatcher.UIThread.Post(() => ApplyState(state));

    private void ApplyState(DeviceState state)
    {
        // Writing the incoming values back to the device would be an echo loop, so the
        // setters are told to stay quiet while the snapshot is being applied.
        _applyingFromDevice = true;
        try
        {
            State = state;
            OnStateApplied(state);
            OnPropertyChanged(nameof(IsAvailable));
        }
        finally
        {
            _applyingFromDevice = false;
        }
    }

    /// <summary>Pull page-specific values out of the snapshot.</summary>
    protected virtual void OnStateApplied(DeviceState state)
    {
    }

    /// <summary>
    /// Queue a device write for a user-driven change. Does nothing while a snapshot is
    /// being applied, and collapses repeats so a slider drag writes once.
    /// </summary>
    protected void Write(Func<CancellationToken, Task> action)
    {
        if (_applyingFromDevice || !State.CanControl) return;
        _debouncer.Schedule(action);
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Device.StateChanged -= OnDeviceStateChanged;
        _debouncer.Dispose();

        GC.SuppressFinalize(this);
    }
}
