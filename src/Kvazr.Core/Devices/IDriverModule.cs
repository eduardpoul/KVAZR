using Microsoft.Extensions.DependencyInjection;

namespace Kvazr.Core.Devices;

/// <summary>
/// A pluggable unit of device support: one class protocol, or one deeply-integrated
/// device family. This is the seam the whole driver architecture turns on.
///
/// A module lives in its own assembly — Kvazr.Drivers.Gamepad, Kvazr.Drivers.Audeze
/// — and registers whatever drivers it owns into the container. The application composes
/// the application by listing the modules it wants; the core never names a driver, and a
/// driver never names another. Adding a new device family is adding an assembly and one
/// line at the composition root, and nothing else moves.
///
/// The contract is deliberately tiny — one method — because a module's real surface is
/// the <see cref="IDeviceDriver"/> it registers, which the registry already knows how to
/// shadow by tier and merge by hardware id. The module is only the wire that carries the
/// driver in.
/// </summary>
public interface IDriverModule
{
    /// <summary>
    /// Stable name for logs and diagnostics, e.g. "gamepad" or "audeze". Not an id the
    /// devices carry — that is <see cref="IDeviceDriver.DriverId"/> — just the module's
    /// own name in a startup line.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Registers this module's drivers and their dependencies. Called once at startup,
    /// before the registry is built. A module that finds the platform unsupportive —
    /// its transport is Windows-only and this is not Windows — registers nothing and
    /// returns quietly, so the same composition root works everywhere.
    /// </summary>
    void Register(IServiceCollection services);
}
