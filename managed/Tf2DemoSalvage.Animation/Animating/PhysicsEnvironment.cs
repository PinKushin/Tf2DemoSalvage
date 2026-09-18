using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The physics environment's clock and step, as the client sets them up (B58, B316, D142).
/// </summary>
/// <remarks>
/// **A TF2 corpse is simulated by the CLIENT, so this is the client's environment rather than the
/// server's** — and that is why the numbers here are ours to reproduce rather than to read off the
/// wire. `C_TFRagdoll::CreateTFRagdoll` calls `InitAsClientRagdoll` (`c_tf_player.cpp:920`), and
/// `DT_TFRagdoll` sends only initial conditions: an origin, a force, a velocity and a force bone
/// (`c_tf_player.cpp:519`). Every pose after the first frame is computed.
///
/// **`PhysicsLevelInit` sets exactly two things that matter here** (`physics.cpp:177-180`):
///
/// <code>
///   physenv->SetGravity( Vector(0, 0, -GetCurrentGravity() ) );
///   // 15 ms per tick
///   // NOTE: Always run client physics at this rate - helps keep ragdolls stable
///   physenv->SetSimulationTimestep( IsXbox() ? DEFAULT_XBOX_CLIENT_VPHYSICS_TICK
///                                            : gpGlobals->interval_per_tick );
/// </code>
///
/// **The step is the TICK interval, not the frame time, and Valve says why in the comment.** A
/// viewer drawing at 300 frames a second must not step physics 300 times a second; it steps at the
/// demo's own tick rate, which this project already decodes rather than assuming. Getting that wrong
/// does not fail — it produces a ragdoll that settles differently at every frame rate.
///
/// **The environment itself is <see cref="IvpRagdollWorld"/>**, on the ported driver (D172); this keeps only the gravity the
/// client hands it. Its clock and its own time manager were deleted with the old solver's (D180, B369).
/// </remarks>
public static class PhysicsEnvironment
{
    /// <summary>Valve's `sv_gravity` default, in units per second squared.</summary>
    /// <remarks>
    /// <c>ConVar sv_gravity( "sv_gravity", "800", FCVAR_NOTIFY | FCVAR_REPLICATED, "World gravity." )</c>
    /// — the environment takes it as `Vector(0, 0, -GetCurrentGravity())`, so it acts down Z in
    /// Source's own axes, before any conversion into IVP's.
    /// </remarks>
    public const float DefaultGravity = 800f;
}
