namespace Maxwell.App.Branding;

/// <summary>
/// The seven motions the identity defines for the mark, and nothing else. A duration
/// invented at a call site is how a set of loaders stops looking like one family, so
/// the durations live here and the call sites name a motion.
/// </summary>
public enum KvazrMotion
{
    /// <summary>No movement. The resting mark.</summary>
    Still,

    /// <summary>2.8 s per turn, linear, forever. The default for anything that waits.</summary>
    Loading,

    /// <summary>1.4 s per turn. Buttons and short answers, where 2.8 s reads as stuck.</summary>
    Fast,

    /// <summary>6 s per turn with the core fading. Splash background, idling.</summary>
    Breathing,

    /// <summary>The planet swells and settles: the device answered but is not ready.</summary>
    Pulse,

    /// <summary>Two turns braking to a stop. Played once, at the end of loading.</summary>
    Landing,

    /// <summary>Rotation stops, the planet goes red, the assembly jolts sideways.</summary>
    Fault,
}

/// <summary>Durations, verbatim from section 05 of the identity.</summary>
internal static class KvazrTimings
{
    public static TimeSpan Loading { get; } = TimeSpan.FromSeconds(2.8);

    public static TimeSpan Fast { get; } = TimeSpan.FromSeconds(1.4);

    public static TimeSpan Breathing { get; } = TimeSpan.FromSeconds(6);

    public static TimeSpan Pulse { get; } = TimeSpan.FromSeconds(1.3);

    public static TimeSpan Landing { get; } = TimeSpan.FromSeconds(3.4);

    public static TimeSpan Fault { get; } = TimeSpan.FromSeconds(2.4);

    /// <summary>
    /// A skeleton's opacity cycle. Shared with <see cref="KvazrMotion.Fast"/> by
    /// coincidence of number rather than of meaning, so it is stated separately.
    /// </summary>
    public static TimeSpan SkeletonPulse { get; } = TimeSpan.FromSeconds(1.4);

    /// <summary>
    /// How far apart adjacent skeleton blocks are shifted. The identity allows
    /// 0.1–0.15 s; the middle of that range is what makes a row read as one object
    /// rather than as a set of independently blinking bars.
    /// </summary>
    public static TimeSpan SkeletonPhaseStep { get; } = TimeSpan.FromSeconds(0.12);

    public static TimeSpan Motion(KvazrMotion motion) => motion switch
    {
        KvazrMotion.Loading => Loading,
        KvazrMotion.Fast => Fast,
        KvazrMotion.Breathing => Breathing,
        KvazrMotion.Pulse => Pulse,
        KvazrMotion.Landing => Landing,
        KvazrMotion.Fault => Fault,
        _ => TimeSpan.Zero,
    };
}
