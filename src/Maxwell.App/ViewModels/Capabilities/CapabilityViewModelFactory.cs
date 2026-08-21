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
    public static CapabilityViewModel Create(
        CapabilityState state,
        IDeviceSession? session,
        HintDetail detail = HintDetail.Normal,
        IConfirmationPrompt? confirm = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Descriptor.Primitive switch
        {
            CapabilityPrimitive.Toggle => new ToggleCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.Range => new RangeCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.Choice => new ChoiceCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.Action =>
                new ActionCapabilityViewModel(state, session, confirm) { Detail = detail },

            CapabilityPrimitive.RangeGroup => new RangeGroupCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.Curve => new CurveCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.KeyMap => new KeyMapCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.ButtonMap => new ButtonMapCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.Lighting => new LightingCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.ProfileSet => new ProfileSetCapabilityViewModel(state, session) { Detail = detail },
            CapabilityPrimitive.HostSlots => new HostSlotsCapabilityViewModel(state, session) { Detail = detail },

            // Readout is both a primitive in its own right and the fallback, and the
            // fallback arm is now unreachable — every member of the enum is named above.
            // It stays because the enum is the taxonomy's, not this file's: a thirteenth
            // primitive should render as a read-only value on the day it is added rather
            // than throw in front of the user.
            _ => new ReadoutCapabilityViewModel(state, session) { Detail = detail },
        };
    }
}
