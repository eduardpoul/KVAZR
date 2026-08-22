using System.Collections.Specialized;
using System.ComponentModel;

using Microsoft.AspNetCore.Components;

namespace Maxwell.App.Web;

/// <summary>
/// A component that redraws when the view models it watches change.
///
/// The view models raise their notifications on Avalonia's UI thread; Blazor renders on
/// its own dispatcher. InvokeAsync is the bridge, and tracking is centralised here so a
/// page cannot forget to unsubscribe — that mistake shows up as a page redrawing for a
/// device that was closed an hour ago.
/// </summary>
public abstract class ReactiveComponent : ComponentBase, IDisposable
{
    private readonly List<INotifyPropertyChanged> _observed = [];
    private readonly List<INotifyCollectionChanged> _collections = [];

    /// <summary>
    /// Redraw when this object changes. One overload taking object, because an
    /// ObservableCollection implements both notification interfaces and two overloads
    /// made every such call ambiguous. Idempotent per object.
    /// </summary>
    protected void Track(object? source)
    {
        if (source is INotifyPropertyChanged observable && !_observed.Contains(observable))
        {
            _observed.Add(observable);
            observable.PropertyChanged += OnChanged;
        }

        if (source is INotifyCollectionChanged collection && !_collections.Contains(collection))
        {
            _collections.Add(collection);
            collection.CollectionChanged += OnCollectionChanged;
        }
    }

    /// <summary>Runs an action on Avalonia's UI thread — the only thread the view
    /// models may be written from.</summary>
    protected static void OnUi(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        foreach (INotifyPropertyChanged source in _observed) source.PropertyChanged -= OnChanged;
        foreach (INotifyCollectionChanged collection in _collections) collection.CollectionChanged -= OnCollectionChanged;

        _observed.Clear();
        _collections.Clear();

        GC.SuppressFinalize(this);
    }
}
