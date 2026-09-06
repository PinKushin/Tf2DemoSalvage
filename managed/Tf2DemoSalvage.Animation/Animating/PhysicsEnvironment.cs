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
/// **`Simulate` clamps, and outside the clamp it simulates NOTHING**
/// (`FUN_180015310` in `vphysics.dll`): the whole body is inside
/// `if ((dt &lt;= max) &amp;&amp; (min &lt; dt))`, so a step too long or too short is skipped rather than
/// sub-stepped. That is a behaviour rather than a guard, and it is transcribed as one.
/// </remarks>
public sealed class PhysicsEnvironment
{
    /// <summary>Valve's `sv_gravity` default, in units per second squared.</summary>
    /// <remarks>
    /// <c>ConVar sv_gravity( "sv_gravity", "800", FCVAR_NOTIFY | FCVAR_REPLICATED, "World gravity." )</c>
    /// — the environment takes it as `Vector(0, 0, -GetCurrentGravity())`, so it acts down Z in
    /// Source's own axes, before any conversion into IVP's.
    /// </remarks>
    public const float DefaultGravity = 800f;

    /// <summary>Creates an environment.</summary>
    /// <param name="step">
    /// The simulation timestep — the demo's <c>interval_per_tick</c>, NOT the frame time.
    /// </param>
    /// <param name="gravity">Downward acceleration, defaulting to <see cref="DefaultGravity"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The step is not positive.</exception>
    public PhysicsEnvironment(float step, float gravity = DefaultGravity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        Step = step;
        Gravity = gravity;
    }

    /// <summary>The fixed simulation step — <c>env+0x108</c>.</summary>
    public float Step { get; }

    /// <summary>Downward acceleration, in units per second squared.</summary>
    public float Gravity { get; }

    /// <summary>Absolute simulation time — <c>env+0x188</c>.</summary>
    public double Now { get; private set; }

    /// <summary>The time manager this environment drives.</summary>
    public PhysicsTimeManager Time { get; } = new();

    /// <summary>Sets the clock, which the event loop does before every event it fires.</summary>
    /// <param name="now">The absolute time.</param>
    /// <remarks>
    /// **`FUN_180082460`, called from inside the drain rather than around it.** The loop sets this to
    /// each event's own time before firing it and to the target once at the end.
    /// </remarks>
    public void SetTime(double now) => Now = now;

    /// <summary>Simulates forward by a delta, as `simulate_dtime` does.</summary>
    /// <param name="delta">Seconds to advance.</param>
    /// <returns>How many events fired.</returns>
    /// <remarks>
    /// **The target is `env+0x188 + dtime`** — `FUN_180082540` is one line and that is all of it:
    /// <c>FUN_180089f30(*(env + 8), env, *(double *)(env + 0x188) + dtime)</c>.
    /// </remarks>
    public int Simulate(double delta) => Time.DrainUntil(Now + delta, SetTime);

    /// <summary>Whether a delta is one the engine would simulate at all.</summary>
    /// <param name="delta">The proposed step.</param>
    /// <param name="minimum">The lower bound; a delta at or below it simulates nothing.</param>
    /// <param name="maximum">The upper bound; a delta above it simulates nothing either.</param>
    /// <returns>Whether anything would happen.</returns>
    /// <remarks>
    /// **Outside the clamp the engine simulates NOTHING**, which is the part worth reproducing: the
    /// entire body of `CPhysicsEnvironment::Simulate` sits inside the test, so a frame too long is
    /// skipped rather than broken into sub-steps. A viewer that sub-stepped instead would be steadier
    /// than TF2 and wrong — the corpse would settle differently after a hitch.
    ///
    /// **The bounds are read as arithmetic, not as named constants**: `FUN_180015310` compares
    /// against two doubles in the binary's constant pool. They are not transcribed here because
    /// their values have not been dumped — this method exists to make the SHAPE explicit and is
    /// deliberately not called by anything yet.
    /// </remarks>
    public static bool WouldSimulate(double delta, double minimum, double maximum) =>
        delta <= maximum && minimum < delta;
}
