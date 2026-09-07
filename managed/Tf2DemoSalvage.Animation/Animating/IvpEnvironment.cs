using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// An IVP simulation: a clock, a gravity vector, and the bodies it steps (B58, D142, D146).
/// </summary>
/// <remarks>
/// **The assembly of pieces transcribed separately.** Each stage is read out of `vphysics.dll` on
/// its own — <see cref="IvpGravity"/> from the controller at `env+0x0`, <see cref="IvpIntegrator"/>
/// from `FUN_180099a00` — and this is the order they run in:
///
/// <code>
///   FUN_18008a020    the PSI event fires
///     FUN_180082560  the pipeline
///       FUN_180090700   islands assembled
///         FUN_1800909d0 every awake core in the island integrated
///           FUN_180099a00
/// </code>
///
/// with the gravity controller accumulating into velocity before that, and the constraint group's
/// two relaxation sweeps between the two.
///
/// **The step is the demo's TICK interval, not the frame time.** `PhysicsLevelInit` sets it with
/// `physenv->SetSimulationTimestep( gpGlobals->interval_per_tick )` under Valve's own comment —
/// *"Always run client physics at this rate - helps keep ragdolls stable"* (`physics.cpp:177-180`).
/// A viewer drawing at three hundred frames a second must not step physics three hundred times a
/// second; getting that wrong does not fail, it produces a corpse that settles differently at every
/// frame rate.
///
/// **The constraint solve runs between gravity and integration**, as
/// <see cref="IvpConstraintGroup"/> — two relaxation sweeps per step, each walking the joint list
/// descending then ascending. It corrects the velocity gravity just changed, and the integrator
/// reads the result, which is why the order in <see cref="Simulate"/> is not rearrangeable.
///
/// **One term in it is still zero for want of a reading**: the rate gain, which would let a limit
/// clamp the PREDICTED deflection rather than the current one. At zero a joint resists a limit it
/// has already broken instead of stopping short of it — a real difference, and a smaller one than
/// inventing the number would be. `docs/findings/51` names where it arrives.
/// </remarks>
public sealed class IvpEnvironment
{
    private readonly List<IvpRigidBody> _bodies = [];

    /// <summary>Creates an environment.</summary>
    /// <param name="step">The simulation timestep — the demo's tick interval.</param>
    /// <param name="gravity">
    /// Acceleration, defaulting to <c>sv_gravity</c>'s 800 down Z as
    /// <c>Vector( 0, 0, -GetCurrentGravity() )</c> gives it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The step is not positive.</exception>
    public IvpEnvironment(float step, (float X, float Y, float Z)? gravity = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        Step = step;
        Gravity = gravity ?? (0f, 0f, -PhysicsEnvironment.DefaultGravity);
    }

    /// <summary>The fixed simulation step — <c>env+0x108</c>.</summary>
    public float Step { get; }

    /// <summary>Acceleration applied to every body that takes it.</summary>
    public (float X, float Y, float Z) Gravity { get; }

    /// <summary>The second acceleration, for bodies that ask for it.</summary>
    /// <remarks>
    /// **IVP holds two and picks per body**, at `controller+0x10` and `controller+0x20`. Nothing in
    /// TF2 sets it; it is carried because the engine has it.
    /// </remarks>
    public (float X, float Y, float Z)? AlternateGravity { get; set; }

    /// <summary>Absolute simulation time — <c>env+0x188</c>.</summary>
    public double Now { get; private set; }

    /// <summary>The bodies this environment steps.</summary>
    public IReadOnlyList<IvpRigidBody> Bodies => _bodies;

    /// <summary>The joints solved between gravity and integration.</summary>
    /// <remarks>
    /// **One group per environment, which is what the engine has.** `CreateConstraintGroup` is a
    /// slot on the environment (23, a thunk at `0x180012a40` loading `environment+0x8`), and a
    /// ragdoll's joints all go into one — so a corpse's limbs relax against each other in the same
    /// sweep rather than in separate passes.
    /// </remarks>
    public IvpConstraintGroup Constraints { get; } = new();

    /// <summary>Adds a body, starting its clock at the current time.</summary>
    /// <param name="body">The body.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **Its clock starts NOW rather than at zero**, and that is not tidiness. The integrator takes
    /// its position delta from `env+0x188 − core+0x1d0`, so a body joining a running environment
    /// with a zeroed stamp would integrate its first step across the environment's whole lifetime
    /// and be flung across the map. A corpse is created mid-demo every time.
    /// </remarks>
    public void Add(IvpRigidBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        body.LastStepped = Now;

        _bodies.Add(body);
    }

    /// <summary>Runs one physics step.</summary>
    /// <remarks>
    /// **Gravity, then the constraint solve, then integration** — the order the pipeline runs them
    /// in, and it matters: the solve corrects the velocity gravity just changed, and the integrator
    /// reads the result.
    ///
    /// **The clock advances FIRST**, because the integrator derives its own position delta from the
    /// difference between the environment's time and the body's — which is how a body that missed
    /// steps catches up in one longer move rather than losing the distance.
    ///
    /// **An immovable body is skipped entirely**, exactly as `FUN_1800909d0` skips a core carrying
    /// the bit.
    /// </remarks>
    public void Simulate()
    {
        IvpGravity.Apply(_bodies, Gravity, Step, AlternateGravity);

        Constraints.Solve();

        Now += Step;

        for (int index = 0; index < _bodies.Count; index++)
        {
            IvpRigidBody body = _bodies[index];

            if (body.Immovable)
            {
                continue;
            }

            IvpIntegrator.Step(body, Now - body.LastStepped, Step);

            body.LastStepped = Now;
        }
    }
}
