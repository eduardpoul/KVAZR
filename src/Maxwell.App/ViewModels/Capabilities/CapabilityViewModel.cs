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
/// The small word beside a row's title in Turn 14/15 — the state, named at a glance,
/// instead of a dot the reader has to decode or a sentence they have to read. Each maps
/// to one legend colour on the artboards.
/// </summary>
public enum CapabilityBadgeKind
{
    /// <summary>Amber "запись не проверена": a write the hardware has never confirmed.</summary>
    Untested,

    /// <summary>Grey "только чтение": a control-shaped value the device will not let us set.</summary>
    ReadOnly,

    /// <summary>Dashed grey "ждёт устройства": the driver is ready, the device has not answered.</summary>
    Pending,

    /// <summary>Blue "изменено на устройстве": the value moved under us, at the device itself.</summary>
    Accent,

    /// <summary>Red: the last write came back with an error.</summary>
    Error,
}

/// <summary>One inline state badge: the word and which legend colour draws it.</summary>
public sealed record CapabilityBadge(string Text, CapabilityBadgeKind Kind);

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

        TrackBusy();

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

    /// <summary>
    /// A write is running, and has been running long enough to be worth saying so.
    ///
    /// Not the raw flag. A write to the headset takes about six hundred milliseconds, and
    /// announcing each one turned every slider nudge into a badge that arrived and left
    /// before it could be read — measured on the equaliser as "не сохранено" at 479 ms,
    /// "применяется" at 892 ms and "не сохранено" again at 1509 ms, all from one drag.
    /// Nobody is waiting on that: the value is on screen already and the device agrees a
    /// moment later. What is worth a word is a write that does not come back — a headset
    /// carried out of range mid-write — so the indicator holds its tongue for
    /// <see cref="BusyDelay"/>, and having spoken stays up <see cref="BusyHold"/> so it
    /// cannot flash either. A refused write is untouched by any of this; that reports
    /// through <see cref="HasError"/>, which has never been delayed.
    /// </summary>
    public bool IsWriting => _busy;

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
    /// <summary>
    /// Not surfaced any more, and deliberately so.
    ///
    /// The fact behind it is real and stays recorded on the descriptor: the first gain
    /// write latches mirror register 0x31, which currently holds its factory 142 and only
    /// a factory reset puts back. What that register governs was never established, and
    /// it was never tested on hardware — the protocol notes file gain under "must not be
    /// tested at all". A caution nobody can act on, printed beside a control people use
    /// every day, teaches them to read past cautions; the owner of the hardware asked for
    /// it gone and it is their headset.
    ///
    /// Kept as false rather than deleted so the knowledge, the descriptor flag and this
    /// note stay together for whoever wonders later.
    /// </summary>
    public bool IsIrreversible => false;

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
        _ when IsWriting => "Применяется…",
        { Status: CapabilityStatus.Unavailable } => "Функция есть, способа вызвать пока нет",
        { Status: CapabilityStatus.Untested } when Detail == HintDetail.Full =>
            "Ещё не проверено на живом устройстве",
        { ChangedExternally: true } => "Изменено на устройстве",

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
        _ when NeedsConfirmation && !HasError && IsAvailable && !IsWriting =>
            HintKind.Warning,
        _ => Hint is null ? HintKind.None : HintKind.Info,
    };

    /// <summary>
    /// A control-shaped value that is nonetheless read-only — a slider for the ChatMix
    /// wheel, a reading the device owns and we can only watch. The plain Readout and the
    /// Action are excluded: a value with no control needs no "read only" beside it, and a
    /// button is not a value at all.
    /// </summary>
    /// <summary>
    /// The reading printed opposite the name in the tray panel, or null when the control
    /// already shows its own value. A slider needs it — a bare track says nothing about
    /// where it sits — while a dropdown's face is the value, and printing it twice on a
    /// 360-pixel row is noise.
    /// </summary>
    public virtual string? QuickValue => null;

    public bool HasQuickValue => !string.IsNullOrEmpty(QuickValue);

    public bool IsReadOnlyInteractive =>
        !Descriptor.IsWritable
        && Descriptor.Primitive is not (CapabilityPrimitive.Readout or CapabilityPrimitive.Action);

    /// <summary>
    /// The inline badge beside the title — Turn 14/15's way of naming a row's state in a
    /// word. Same one-thing-wins precedence as <see cref="Hint"/>: an error outranks a
    /// missing driver outranks an unconfirmed write outranks a read-only value. Null for
    /// an ordinary writable control at rest, and null while unavailable, where the frame
    /// already stands in for the whole control.
    /// </summary>
    public CapabilityBadge? Badge => State switch
    {
        { WriteError: not null } => new CapabilityBadge("ошибка записи", CapabilityBadgeKind.Error),
        _ when IsWriting => null,
        { Status: CapabilityStatus.Unavailable } => new CapabilityBadge("ждёт устройства", CapabilityBadgeKind.Pending),
        { IsAvailable: false } => null,
        // No badge for a value that moved on the device.
        //
        // It was meant to explain a number changing on its own, but the only control it
        // ever appeared on is the one with a wheel on the earcup — where the wearer just
        // turned that wheel and needs no telling. All it did was flicker beside a slider
        // that had already caught up.
        { Status: CapabilityStatus.Untested } => new CapabilityBadge("запись не проверена", CapabilityBadgeKind.Untested),
        _ when IsReadOnlyInteractive => new CapabilityBadge("только чтение", CapabilityBadgeKind.ReadOnly),
        _ => null,
    };

    // ---------- updates ----------

    /// <summary>Pushes a fresh reading in. Never triggers a write back to the device.</summary>
    public void Apply(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Our own write, come back confirmed.
        //
        // The echo window exists to ignore readings taken before a write landed, and it
        // was ignoring the write's own answer along with them: the session reports the
        // accepted value the moment the device takes it, which is inside the window by
        // definition. Skipped, the control kept whatever it held until some unrelated
        // capability happened to move and forced a publish — thirteen seconds after a
        // reset, in the measurement that found this. An echo carrying exactly the value
        // we sent is the one reading that cannot be stale, so it closes the window
        // instead of being thrown away by it.
        if (_pending is { } sent && !state.IsWriting && state.Value == sent)
        {
            _pending = null;
            _lastLocalWrite = DateTimeOffset.MinValue;
        }

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

        TrackBusy();
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

    /// <summary>Long enough that an ordinary round trip to the device says nothing at all.</summary>
    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(1200);

    /// <summary>Once it is up, long enough to be read rather than glimpsed.</summary>
    private static readonly TimeSpan BusyHold = TimeSpan.FromMilliseconds(700);

    private bool _busy;
    private DateTimeOffset _writingSince = DateTimeOffset.MinValue;
    private DateTimeOffset _busySince = DateTimeOffset.MinValue;
    private bool _watching;

    /// <summary>
    /// Works out whether the row should call itself busy, and books another look if the
    /// answer can still change without a new reading arriving — which it can, twice: when
    /// a slow write crosses <see cref="BusyDelay"/>, and when a finished one has served
    /// out its <see cref="BusyHold"/>.
    /// </summary>
    private void TrackBusy()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool writing = State.IsWriting;

        if (!writing) _writingSince = DateTimeOffset.MinValue;
        else if (_writingSince == DateTimeOffset.MinValue) _writingSince = now;

        bool want = writing
            ? _busy || now - _writingSince >= BusyDelay
            : _busy && now - _busySince < BusyHold;

        if (want != _busy)
        {
            _busy = want;
            if (want) _busySince = now;

            OnPropertyChanged(nameof(IsWriting));
            OnPropertyChanged(nameof(Hint));
            OnPropertyChanged(nameof(HintTone));
            OnPropertyChanged(nameof(Badge));
        }

        TimeSpan again =
            writing && !_busy ? BusyDelay - (now - _writingSince)
            : !writing && _busy ? BusyHold - (now - _busySince)
            : TimeSpan.Zero;

        if (again <= TimeSpan.Zero || _watching) return;

        _watching = true;
        DispatcherTimer.RunOnce(
            () =>
            {
                _watching = false;
                TrackBusy();
            },
            again);
    }

    /// <summary>
    /// A local write has just gone out and the device has not caught up yet.
    ///
    /// The window covers the whole round trip: the debounce that batches a drag, the
    /// write itself, and the poll after it that can still be carrying the value from
    /// before. Without it a slider being dragged jumps back to where it was and then
    /// forward again as the readings land out of order — visible as the control fighting
    /// the hand on it, and made far more visible once readings began publishing one at a
    /// time instead of in one batch at the end of a sweep.
    /// </summary>
    protected bool WriteInFlight => DateTimeOffset.UtcNow - _lastLocalWrite < WriteEcho;

    /// <summary>
    /// Long enough to cover a debounce, a write and the poll that follows it. Short
    /// enough that a value moved on the device itself still shows up promptly.
    /// </summary>
    private static readonly TimeSpan WriteEcho = TimeSpan.FromMilliseconds(1500);

    private DateTimeOffset _lastLocalWrite = DateTimeOffset.MinValue;

    /// <summary>
    /// The value last sent, kept only until the device confirms it. What makes the
    /// confirmation recognisable — everything else arriving in the echo window is a
    /// reading from before the write and has to be ignored.
    /// </summary>
    private CapabilityValue? _pending;

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
        OnPropertyChanged(nameof(Badge));
    }

    /// <summary>
    /// Queues a write. Does nothing while a reading is being applied, and collapses
    /// repeats so dragging a slider writes once rather than forty times.
    /// </summary>
    protected void Write(CapabilityValue value)
    {
        if (_applying || _settling || Session is null || !CanEdit) return;

        _pending = value;
        _lastLocalWrite = DateTimeOffset.UtcNow;
        _debouncer.Schedule(ct => Session.WriteAsync(Id, value, ct));
    }

    /// <summary>Writes immediately. For discrete choices, where a delay just feels broken.</summary>
    protected void WriteNow(CapabilityValue value)
    {
        if (_applying || _settling || Session is null || !CanEdit) return;

        _pending = value;
        _lastLocalWrite = DateTimeOffset.UtcNow;
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
