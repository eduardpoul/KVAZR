using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Maxwell.App.Infrastructure;
using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>How much the hint line is allowed to say.</summary>
public enum HintDetail
{
    /// <summary>Only a refused write. The tray panel has room for nothing else.</summary>
    Terse,

    /// <summary>Everything except "not verified yet", which the amber dot already says.</summary>
    Normal,

    /// <summary>Every state in words. For the showcase, whose job is to demonstrate them.</summary>
    Full,
}

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
    /// <summary>
    /// The device has this feature and we have no way to ask for it yet — the command is
    /// not known, or the editor is not written. Different from unavailable, which means
    /// the command exists and the current connection cannot carry it.
    /// </summary>
    public bool IsImplemented => State.Status != CapabilityStatus.Unavailable;

    /// <summary>
    /// Whether this capability's control belongs in the row's right-hand column or across
    /// the whole width below its name.
    ///
    /// A switch, a slider and a dropdown are all the same shape and lining them up in one
    /// column is what makes a page of them read as a list. A host list, a button map, a
    /// key map, a response curve and a set of colour zones are not that shape: each is a
    /// small table, and squeezing one into 260 pixels does not make it tidy, it makes it
    /// unreadable and then clips what is left off the edge of the card. Artboards 9b and
    /// 9c give every one of them the full width.
    /// </summary>
    public bool IsWide => Descriptor.Primitive
        is CapabilityPrimitive.HostSlots
        or CapabilityPrimitive.ProfileSet
        or CapabilityPrimitive.ButtonMap
        or CapabilityPrimitive.KeyMap
        or CapabilityPrimitive.Curve
        or CapabilityPrimitive.Lighting;

    /// <summary>
    /// A number with its unit. No space before a percent sign and a space before every
    /// other unit — that is the typographic rule in both languages this ships in, and
    /// "64 %" in a column of readings is the kind of thing that makes a page look
    /// machine-generated.
    /// </summary>
    public static string WithUnit(double value, string? unit) => WithUnit($"{value:0}", unit);

    public static string WithUnit(string value, string? unit) => unit switch
    {
        null or "" => value,
        "%" => value + "%",
        _ => $"{value} {unit}",
    };

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
    /// How much the hint may say. A device page drops "not verified yet", because most
    /// of a Maxwell's capabilities are untested and nine identical sentences down one
    /// page stop being read by the third. The tray panel drops everything but a refusal,
    /// because it is 360 pixels wide and exists to be glanced at.
    /// </summary>
    public HintDetail Detail { get; init; } = HintDetail.Normal;

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

    /// <summary>
    /// Warned about before the write, never after — and only where a write exists.
    ///
    /// Both of these are claims about writing, so neither applies to a value that is only
    /// ever read. Ungated, the battery percentage carried "the first write cannot be
    /// undone" next to it, which is not a caution but a puzzle. Artboard 2a says the same
    /// thing in its own words: against the indicator row it prints "не применяется".
    /// </summary>
    /// <summary>
    /// The warning belongs to the act of writing, so it appears when a write is actually
    /// possible. A headset asleep on its dongle was carrying a warning triangle and
    /// "первая запись необратима" beside a slider that could do nothing at all — a
    /// caution about an action nobody could take.
    /// </summary>
    public bool IsIrreversible => !Descriptor.IsReversible && Descriptor.IsWritable && CanEdit;

    public bool NeedsConfirmation => Descriptor.Risk != CapabilityRisk.Safe && Descriptor.IsWritable;

    /// <summary>
    /// The one line shown in place of the control when the capability cannot be reached —
    /// the device is asleep, the boom is off, this channel does not carry settings.
    ///
    /// In place of, not beside: an unreachable slider was a dash, an amber dot and a
    /// right-aligned explanation stacked on three lines, and a page of those reads as a
    /// rendering fault. The reason is the only thing worth saying here.
    /// </summary>
    public string Unreachable => Descriptor.RequirementHint ?? "недоступно сейчас";

    /// <summary>True when the control may be operated at all.</summary>
    public bool CanEdit => State.CanWrite && Session is not null;

    /// <summary>
    /// The single line under the control. One line, not several, and the most urgent
    /// thing wins — a stack of badges turns into wallpaper the user stops reading.
    /// </summary>
    public string? Hint => State switch
    {
        { WriteError: { } error } => error,
        _ when Detail == HintDetail.Terse => null,

        // Said in the frame itself, in place of the control. Repeating it here put the
        // same sentence on the row twice.
        { IsAvailable: false } => null,
        { IsWriting: true } => "Применяется…",
        { Status: CapabilityStatus.Unavailable } => "Функция есть, способа вызвать пока нет",
        { Status: CapabilityStatus.Untested } when Detail == HintDetail.Full =>
            "Ещё не проверено на живом устройстве",
        { ChangedExternally: true } => "Изменено на устройстве",
        _ when IsIrreversible => Descriptor.Note ?? "Первая запись необратима",

        // Risk explained in words beside the control rather than left to the button.
        // Only the Action control draws a mark of its own, so without this every other
        // primitive's dangerous state was indistinguishable from its ordinary one — the
        // user would meet the consequence for the first time in the confirmation dialog,
        // which is later than the design system allows.
        _ when NeedsConfirmation => Descriptor.Note ?? Descriptor.Risk switch
        {
            CapabilityRisk.Hidden => "Скрыто в «Дополнительно», спросим подтверждение",
            _ => "Спросим подтверждение с описанием последствий",
        },

        _ => Descriptor.Note,
    };

    public HintKind HintTone => State switch
    {
        { WriteError: not null } => HintKind.Error,
        _ when (IsIrreversible || NeedsConfirmation) && !HasError && IsAvailable && !IsWriting =>
            HintKind.Warning,
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
        OnPropertyChanged(nameof(IsIrreversible));
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
