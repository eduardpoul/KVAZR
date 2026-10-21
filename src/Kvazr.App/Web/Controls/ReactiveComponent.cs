using System.Collections.Specialized;
using System.ComponentModel;

using Microsoft.AspNetCore.Components;

namespace Kvazr.App.Web;

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

    /// <summary>
    /// The catalogue, short enough to sit inside markup. Every component redraws on a
    /// language change through <see cref="Follow"/>, which the base subscribes for itself
    /// so no page has to remember to.
    /// </summary>
    protected static Localization.Localizer T => Localization.Localizer.Current;

    private Localization.Localizer? _text;

    /// <summary>
    /// Redraw when the language changes. Every page calls this, because every page has
    /// text on it — and a language switch that leaves half the window in the old language
    /// until it is navigated away from and back is worse than no switch at all.
    /// </summary>
    protected void Follow(Localization.Localizer? text)
    {
        if (text is null || ReferenceEquals(_text, text)) return;

        _text = text;
        text.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Redraw();

    private int _pending;

    /// <summary>
    /// One redraw per burst, not one per property.
    ///
    /// The view models announce their fields individually and a device snapshot moves
    /// several at once — the page header alone raises a dozen, and a session publishes a
    /// fresh snapshot after every capability it reads rather than once at the end of a
    /// sweep. Queueing a render for each of them put hundreds of work items on Blazor's
    /// dispatcher during a full read, and anything the wearer did meanwhile waited behind
    /// the pile: a reverted equaliser changed the sound at once and redrew ten seconds
    /// later, which read as the application having hung.
    ///
    /// Collapsing them costs nothing — the render that does run sees all the changes,
    /// because it reads the view models as they are by then.
    /// </summary>
    private void Redraw()
    {
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;

        _ = InvokeAsync(() =>
        {
            Volatile.Write(ref _pending, 0);
            StateHasChanged();
        });
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Redraw();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    public virtual void Dispose()
    {
        foreach (INotifyPropertyChanged source in _observed) source.PropertyChanged -= OnChanged;
        if (_text is not null) _text.Changed -= OnLanguageChanged;

        foreach (INotifyCollectionChanged collection in _collections) collection.CollectionChanged -= OnCollectionChanged;

        _observed.Clear();
        _collections.Clear();

        GC.SuppressFinalize(this);
    }
}
