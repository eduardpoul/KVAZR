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

    protected CapabilityViewModel(CapabilityState state, IDeviceSession? session)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        Session = session;
    }

    protected IDeviceSession? Session { get; }

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
        { Status: CapabilityStatus.Untested } => "Ещё не проверено на живом устройстве",
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
        if (_applying || Session is null || !CanEdit) return;

        _debouncer.Schedule(ct => Session.WriteAsync(Id, value, ct));
    }

    /// <summary>Writes immediately. For discrete choices, where a delay just feels broken.</summary>
    protected void WriteNow(CapabilityValue value)
    {
        if (_applying || Session is null || !CanEdit) return;

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
