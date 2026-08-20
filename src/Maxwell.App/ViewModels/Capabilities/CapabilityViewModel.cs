using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.Infrastructure;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>How the hint line under a control should read.</summary>
public enum HintKind
{
    None,
    Info,
    Warning,
    Error,
}

/// <summary>
/// One capability, ready to bind.
///
/// The eight control states from the design system live here rather than in the
/// templates, because they are the same eight for every primitive and a template that
/// re-derives them is a template that will get one of them wrong. A control only asks
/// "am I dimmed, is there a dot, what does the hint say".
/// </summary>
public abstract partial class CapabilityViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(350);

    private readonly Debouncer _debouncer = new(WriteDelay);
    private bool _disposed;

    /// <summary>Suppresses writes while a device reading is being applied.</summary>
    private bool _applying;

    /// <summary>
    /// Suppresses writes until the control has finished reacting to a new reading.
    ///
    /// This is not belt-and-braces. A two-way bound control that is handed new bounds
    /// coerces its own value to fit and pushes the correction straight back through the
    /// binding, and that echo is indistinguishable from a person moving the slider. Left
    /// unguarded it does not merely display the wrong number — it writes it to the
    /// hardware, which is exactly how a headset's earcup balance got quietly moved off
    /// centre during development.
    /// </summary>
    private bool _settling;

    protected CapabilityViewModel(CapabilityState state, IDeviceSession? session)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        Session = session;

        // The first attach is the dangerous one: the control has its defaults, the
        // binding hands it bounds and a value in whatever order it likes, and anything
        // it pushes back before it has settled is not a person's decision.
        Settle();
    }

    protected IDeviceSession? Session { get; }

    /// <summary>
    /// Spell out every state in words. The showcase does, because demonstrating them is
    /// its whole purpose; a device page does not, because most of a Maxwell's
    /// capabilities are untested and nine identical sentences down one page stop being
    /// read by the third. There the amber dot and its tooltip carry it.
    /// </summary>
    public bool Verbose { get; init; }

    [ObservableProperty]
    public partial CapabilityState State { get; private set; }

    public string Id => State.Id;

    public Capability Descriptor => State.Descriptor;

    public string Title => Descriptor.Title;

    public string? Subtitle => Descriptor.Subtitle;

    public CapabilityDomain Domain => Descriptor.Domain;

    // ---------- the eight states ----------

    public bool IsAvailable => State.IsAvailable;

    /// <summary>The amber dot. Gone for good once the device has accepted a write.</summary>
    public bool IsUntested => State.Status == CapabilityStatus.Untested;

    public bool IsWriting => State.IsWriting;

    public bool HasError => State.WriteError is not null;

    public bool ChangedExternally => State.ChangedExternally;

    /// <summary>Warned about before the write, never after.</summary>
    public bool IsIrreversible => !Descriptor.IsReversible;

    public bool NeedsConfirmation => Descriptor.Risk != CapabilityRisk.Safe;

    /// <summary>True when the control may be operated at all.</summary>
    public bool CanEdit => State.CanWrite && Session is not null;

    /// <summary>
    /// The single line under the control. One line, not several, and the most urgent
    /// thing wins — a stack of badges turns into wallpaper the user stops reading.
    /// </summary>
    public string? Hint => State switch
    {
        { WriteError: { } error } => error,
        { IsAvailable: false } => Descriptor.RequirementHint,
        { IsWriting: true } => "Применяется…",
        { Status: CapabilityStatus.Unavailable } => "Функция есть, способа вызвать пока нет",
        { Status: CapabilityStatus.Untested } when Verbose => "Ещё не проверено на живом устройстве",
        { ChangedExternally: true } => "Изменено на устройстве",
        _ when IsIrreversible => Descriptor.Note ?? "Первая запись необратима",
        _ => Descriptor.Note,
    };

    public HintKind HintTone => State switch
    {
        { WriteError: not null } => HintKind.Error,
        _ when IsIrreversible && !HasError && IsAvailable && !IsWriting => HintKind.Warning,
        _ => Hint is null ? HintKind.None : HintKind.Info,
    };

    // ---------- updates ----------

    /// <summary>Pushes a fresh reading in. Never triggers a write back to the device.</summary>
    public void Apply(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _applying = true;
        _settling = true;

        try
        {
            State = state;
            OnStateApplied(state);
            RaiseStateProperties();
        }
        finally
        {
            _applying = false;
        }

        Settle();
    }

    /// <summary>
    /// Holds writes until the control has laid out. Background priority runs after
    /// layout, by which time any coercion the control wanted to do has happened and
    /// been pushed back through the binding.
    /// </summary>
    private void Settle()
    {
        _settling = true;

        Dispatcher.UIThread.Post(
            () =>
            {
                OnSettled();
                _settling = false;
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// The control has finished reacting. Re-assert anything it may have coerced away.
    /// </summary>
    protected virtual void OnSettled()
    {
    }

    /// <summary>Pull the primitive's own value out of the reading.</summary>
    protected virtual void OnStateApplied(CapabilityState state)
    {
    }

    protected void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsUntested));
        OnPropertyChanged(nameof(IsWriting));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ChangedExternally));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HintTone));
    }

    /// <summary>
    /// Queues a write. Does nothing while a reading is being applied, and collapses
    /// repeats so dragging a slider writes once rather than forty times.
    /// </summary>
    protected void Write(CapabilityValue value)
    {
        if (_applying || _settling || Session is null || !CanEdit) return;

        _debouncer.Schedule(ct => Session.WriteAsync(Id, value, ct));
    }

    /// <summary>Writes immediately. For discrete choices, where a delay just feels broken.</summary>
    protected void WriteNow(CapabilityValue value)
    {
        if (_applying || _settling || Session is null || !CanEdit) return;

        _ = Session.WriteAsync(Id, value, CancellationToken.None);
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debouncer.Dispose();
        GC.SuppressFinalize(this);
    }
}
