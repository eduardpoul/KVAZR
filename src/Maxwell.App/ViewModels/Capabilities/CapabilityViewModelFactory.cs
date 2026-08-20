using Maxwell.Core.Devices;

namespace Maxwell.App.ViewModels.Capabilities;

/// <summary>
/// Turns a capability into the view model for its primitive.
///
/// This is the only place that maps primitive to control. Every page is built from it,
/// which is what stops a device page from ever being written by hand: a driver declares
/// twelve kinds of thing, the shell knows how to draw twelve kinds of thing, and no
/// layer in between knows what a Maxwell is.
/// </summary>
public static class CapabilityViewModelFactory
{
    public static CapabilityViewModel Create(CapabilityState state, IDeviceSession? session)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Descriptor.Primitive switch
        {
            CapabilityPrimitive.Toggle => new ToggleCapabilityViewModel(state, session),
            CapabilityPrimitive.Range => new RangeCapabilityViewModel(state, session),
            CapabilityPrimitive.Choice => new ChoiceCapabilityViewModel(state, session),
            CapabilityPrimitive.Action => new ActionCapabilityViewModel(state, session),

            // Readout is also the honest fallback for primitives that have no control
            // yet — KeyMap, ButtonMap, Curve and the rest arrive with the screens that
            // need them. Showing the value read-only says less than the design intends,
            // but it never lies about what the user can do.
            _ => new ReadoutCapabilityViewModel(state, session),
        };
    }

    /// <summary>
    /// Whether this primitive has a real control yet. The showcase and the device pages
    /// use it to mark what is still a placeholder instead of quietly presenting a
    /// read-only row as the finished design.
    /// </summary>
    public static bool IsImplemented(CapabilityPrimitive primitive) => primitive
        is CapabilityPrimitive.Readout
        or CapabilityPrimitive.Toggle
        or CapabilityPrimitive.Range
        or CapabilityPrimitive.Choice
        or CapabilityPrimitive.Action;
}
