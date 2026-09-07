using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One contact between a body and the static world, and the impulse that resolves it (B58).
/// </summary>
/// <remarks>
/// **The engine builds a RECORD with its effective mass precomputed, and applies nothing there.**
/// `FUN_18008d0c0` allocates ~0x110 bytes per contact from a per-environment pool, builds an
/// orthonormal contact frame by explicit cross products, and writes the per-axis effective inverse
/// mass:
///
/// <code>
/// *(float *)((longlong)pdVar7 + 0x94) =
///      rx*rx * core[+0x44] + ry*ry * core[+0x40] + rz*rz * core[+0x48] + core[+0x4c];
/// </code>
///
/// Three rotational terms and one linear. That expression is reproduced exactly below, including
/// the fact that it carries no cross product — IVP's lever arm enters through the frame the
/// contact is expressed in, not through an `r × n` here.
///
/// **The consumer of that record is the one piece `docs/findings/51` still lists as missing**, so
/// the application below is this project's, not a transcription: an accumulated normal impulse,
/// clamped non-negative, in the same sequential-impulse loop the ragdoll constraints already use —
/// which is what the constraint solve WAS found to be. Stated plainly rather than implied, because
/// the parts either side of it are transcribed and this part is not.
///
/// **The static world contributes nothing**, which is not a shortcut: the contact builder zeroes
/// the whole mass and inertia term for a body carrying `core+0x0 &amp; 2`, and that bit is how static
/// map geometry is represented.
/// </remarks>
public sealed class IvpContact
{
    /// <summary>The body being pushed.</summary>
    public required IvpRigidBody Body { get; init; }

    /// <summary>Where the contact is, relative to the body's centre, in Source units.</summary>
    public required (float X, float Y, float Z) Arm { get; init; }

    /// <summary>The direction that takes the body out of the world.</summary>
    public required (float X, float Y, float Z) Normal { get; init; }

    /// <summary>How far inside it is.</summary>
    public required float Depth { get; init; }

    /// <summary>The impulse applied so far, accumulated across iterations.</summary>
    public float Accumulated { get; private set; }

    /// <summary>
    /// The contact's effective inverse mass — <c>FUN_18008d0c0</c>, <c>record+0x94</c>.
    /// </summary>
    /// <remarks>
    /// **The pairing of `rx` with `core+0x44` and `ry` with `core+0x40` is the engine's, not a
    /// transposition error in the reading** — it is the axis order IVP holds its inverse inertia
    /// in. It is unobservable for this project's ragdolls, whose inertia is isotropic by a stated
    /// departure, and it is written this way so that it stays right when it stops being.
    /// </remarks>
    public float EffectiveInverseMass =>
        (Arm.X * Arm.X * Body.InverseInertia.Y) +
        (Arm.Y * Arm.Y * Body.InverseInertia.X) +
        (Arm.Z * Arm.Z * Body.InverseInertia.Z) +
        Body.InverseMass;

    /// <summary>Applies one iteration's worth of impulse.</summary>
    /// <param name="step">The timestep, for the penetration bias.</param>
    /// <remarks>
    /// **The accumulated impulse is clamped, not the increment**, which is what makes a stack of
    /// contacts settle instead of jittering: an iteration is free to pull back an over-correction
    /// from an earlier one, so long as the total push has never been negative. A contact cannot
    /// pull a body in.
    ///
    /// **The bias returns only a fraction of the penetration per step.** Removing it all at once
    /// converts depth into velocity and a corpse resting on a slope climbs it; Valve's own solver
    /// leaks error the same way, and the fraction here is the constraint group's relaxation weight
    /// rather than a second number invented for contacts.
    /// </remarks>
    public void Solve(float step)
    {
        float effective = EffectiveInverseMass;

        if (effective <= FloatEpsilon || step <= 0f)
        {
            return;
        }

        // The velocity of the contact point, which is the body's plus the rotation about its arm.
        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, Arm);

        float closing =
            ((Body.Velocity.X + spin.X) * Normal.X) +
            ((Body.Velocity.Y + spin.Y) * Normal.Y) +
            ((Body.Velocity.Z + spin.Z) * Normal.Z);

        // **Capped, and the cap is the part that was measured rather than reasoned.** Without it a
        // body falling at 400 units a second penetrates six units in one step, and a bias
        // proportional to that depth returns it as a hundred-unit-per-second launch: the test body
        // was thrown back to 44 units above a floor it should have been resting on. A corpse would
        // do the same thing and look like it had been shot.
        float bias = Depth > Slop
            ? MathF.Min(Recovery * (Depth - Slop) / step, MaximumRecovery)
            : 0f;

        float wanted = (-closing + bias) / effective;

        // Clamp the TOTAL rather than this increment — see the remarks.
        float was = Accumulated;

        Accumulated = MathF.Max(0f, was + wanted);

        float applied = Accumulated - was;

        Body.Velocity = (
            Body.Velocity.X + (Normal.X * applied * Body.InverseMass),
            Body.Velocity.Y + (Normal.Y * applied * Body.InverseMass),
            Body.Velocity.Z + (Normal.Z * applied * Body.InverseMass));

        (float X, float Y, float Z) torque = Cross(
            Arm, (Normal.X * applied, Normal.Y * applied, Normal.Z * applied));

        Body.AngularVelocity = (
            Body.AngularVelocity.X + (torque.X * Body.InverseInertia.X),
            Body.AngularVelocity.Y + (torque.Y * Body.InverseInertia.Y),
            Body.AngularVelocity.Z + (torque.Z * Body.InverseInertia.Z));

        Rub(effective);
    }

    /// <summary>Opposes sliding, up to what the normal impulse allows.</summary>
    /// <remarks>
    /// **Coulomb, and the coefficient is the game's own shipped number.** A surface's friction is
    /// `surfacephysicsparams_t::friction` out of `scripts/surfaceproperties*.txt`
    /// (`vphysics_interface.h:882`), which the engine looks up per solid —
    /// `physprops-&gt;GetSurfaceIndex( solid.surfaceprop )`, `ragdoll_shared.cpp:194` — and hands to
    /// `CreatePolyObject` beside the hull.
    ///
    /// **Without this a corpse never stops.** Two of the eight measured on `koth_harvest_final` had
    /// slid hundreds of units off the map, one of them to 23,832 units below it, because a body
    /// resting on any slope with no tangential force keeps accelerating down it.
    ///
    /// **The world's own material is NOT read yet, and that is a stated gap.** The map's collision
    /// text carries a per-hull material table — `MapSurfaceTable.Materials` — and this uses only the
    /// body's coefficient, so a corpse slides the same on ice as on wood. The table is read and
    /// unused rather than absent, which is the difference between a gap and a guess.
    /// </remarks>
    private void Rub(float effective)
    {
        if (Accumulated <= 0f || Body.Friction <= 0f)
        {
            return;
        }

        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, Arm);

        (float X, float Y, float Z) moving = (
            Body.Velocity.X + spin.X, Body.Velocity.Y + spin.Y, Body.Velocity.Z + spin.Z);

        // The part of that motion along the surface, which is what friction opposes.
        float into = (moving.X * Normal.X) + (moving.Y * Normal.Y) + (moving.Z * Normal.Z);

        (float X, float Y, float Z) sliding = (
            moving.X - (Normal.X * into),
            moving.Y - (Normal.Y * into),
            moving.Z - (Normal.Z * into));

        float speed = MathF.Sqrt(
            (sliding.X * sliding.X) + (sliding.Y * sliding.Y) + (sliding.Z * sliding.Z));

        if (speed <= FloatEpsilon)
        {
            return;
        }

        // **Clamped by the normal impulse, which is what makes it Coulomb rather than a drag.** A
        // body pressed hard into a surface resists sliding more; one barely touching does not, and
        // one in the air is not slowed at all.
        float wanted = MathF.Min(speed / effective, Body.Friction * Accumulated);

        float scale = wanted / speed;

        (float X, float Y, float Z) impulse = (
            -sliding.X * scale, -sliding.Y * scale, -sliding.Z * scale);

        Body.Velocity = (
            Body.Velocity.X + (impulse.X * Body.InverseMass),
            Body.Velocity.Y + (impulse.Y * Body.InverseMass),
            Body.Velocity.Z + (impulse.Z * Body.InverseMass));

        (float X, float Y, float Z) twist = Cross(Arm, impulse);

        Body.AngularVelocity = (
            Body.AngularVelocity.X + (twist.X * Body.InverseInertia.X),
            Body.AngularVelocity.Y + (twist.Y * Body.InverseInertia.Y),
            Body.AngularVelocity.Z + (twist.Z * Body.InverseInertia.Z));
    }

    /// <summary>Every contact one body's hull makes with the world this step.</summary>
    /// <param name="body">The body to test.</param>
    /// <param name="world">The static world, or null when there is none.</param>
    /// <param name="into">Where the contacts are added.</param>
    /// <param name="step">
    /// The timestep, so a point about to cross a surface raises its contact before it does. Zero
    /// disables that and tests the current position alone.
    /// </param>
    /// <param name="lookAhead">
    /// How far ahead to predict, in seconds — the environment's <c>lookAheadTimeObjectsVsWorld</c>,
    /// which Valve defaults to a full second.
    /// </param>
    /// <param name="checks">
    /// The step's running collision-check count — <c>maxCollisionChecksPerTimestep</c> counts pair
    /// tests, not sub-steps, and every sweep here is one.
    /// </param>
    /// <returns>When within the step this body first meets the world, or the whole step.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A body's hull points are tested, not its triangles.** That is the vertex-face case, and it
    /// is the departure <see cref="IvpWorldCollision"/> states: a corpse on a floor touches through
    /// its vertices, and the edge-edge pair IVP also dispatches is absent.
    ///
    /// **An immovable body generates none**, matching the island driver, which does not integrate
    /// one either.
    /// </remarks>
    public static float Find(
        IvpRigidBody body,
        IvpWorldCollision? world,
        ICollection<IvpContact> into,
        float step,
        float lookAhead,
        ref int checks)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(into);

        float impact = step;

        if (world is null || body.Immovable || body.Hull.Count == 0)
        {
            return impact;
        }

        Quaternion orientation = new(
            body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W);

        Vector3 centre = new((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);

        // **This step's committed motion, hoisted out of the loop.** It is the same for every hull
        // point of a body, and the time of impact is measured along it.
        Vector3 committed = new(
            body.PreviousVelocity.X * step,
            body.PreviousVelocity.Y * step,
            body.PreviousVelocity.Z * step);

        float committedLength = committed.Length();

        for (int index = 0; index < body.Hull.Count; index++)
        {
            (float x, float y, float z) = body.Hull[index];

            Vector3 arm = Vector3.Transform(new Vector3(x, y, z), orientation);

            (Vector3 Normal, float Depth)? hit = world.Penetration(centre + arm);

            if (hit is null && step > 0f)
            {
                // **The SPECULATIVE contact, and it is what stops a corpse tunnelling.** A body at
                // terminal velocity moves twelve units in one tick, so a discrete test against the
                // current position finds nothing this step and finds the point deep inside a brush
                // the next — and once a point is past a brush's midplane the shallowest face is the
                // BOTTOM one, so the push that should have stopped it drives it through instead.
                // Measured: entity 2080 reporting 62 contacts while sixty-nine units under a floor.
                //
                // **IVP has no such failure because it never lets a pair get that close without
                // looking.** Its mindist system reschedules a pair check against the pair's own
                // closing distance (`docs/findings/51`, the contact-pair re-check scheduler), so
                // the contact exists before the surfaces touch. This is that: where the point WILL
                // be after this step is tested, and a contact is raised now with zero depth — no
                // penetration to recover, only a closing velocity to cancel, which is exactly what
                // a contact that has not happened yet should do.
                // **The engine predicts a collision by TIME, not by a step** —
                // `lookAheadTimeObjectsVsWorld = 1.0f`, *"predict collisions this far (seconds)
                // into the future"* (`performance.h:37`). A second, where this looked ahead one
                // step: at the speed clamp that is sixty units against two thousand.
                //
                // **The integrator's one-step lag is still in it.** A body moves by its PREVIOUS
                // velocity, so the travel predicted here starts from the motion already decided and
                // continues at the current one — which is why both appear.
                Vector3 now = centre + arm;

                Vector3 predicted = new(
                    body.Velocity.X * lookAhead,
                    body.Velocity.Y * lookAhead,
                    body.Velocity.Z * lookAhead);

                // **One sweep answers both questions.** It used to be swept twice per hull point
                // per sub-step — once to raise the contact and once for the time of impact — which
                // is the same segment against the same world for the same answer.
                checks++;

                if (world.Sweep(now, now + committed + predicted) is { } soon)
                {
                    float reach = (committed + predicted).Length();

                    float when = reach > FloatEpsilon
                        ? soon.Fraction * reach / MathF.Max(committedLength / step, FloatEpsilon)
                        : step;

                    if (when > FloatEpsilon && when < impact)
                    {
                        impact = when;
                    }

                    // **The lookahead decides WHEN A PAIR IS LOOKED AT, not when it is pushed.**
                    // `docs/findings/51` reads the scheduler as recomputing a pair's distance and
                    // either escalating into the refine or RE-QUEUEING itself for a later check —
                    // so a surface a second away is watched, not resisted. Turning the whole
                    // prediction into an impulse stops a falling body dead in mid-air, measured at
                    // sixty-six units above a floor it should have landed on.
                    // **The window is TWO steps, and that is the integrator's lag rather than a
                    // margin.** A body moves by its previous velocity, so this step's travel is
                    // already committed and an impulse raised now first bites on the step after —
                    // a contact accepted only for the committed step arrives too late to stop
                    // anything, measured as a body 2,775 units under a sixteen-unit floor.
                    float reachable = committedLength +
                        (new Vector3(body.Velocity.X, body.Velocity.Y, body.Velocity.Z).Length() * step);

                    if (soon.Fraction * (committed + predicted).Length() <= reachable)
                    {
                        hit = (soon.Normal, 0f);
                    }
                }
            }

            if (hit is not { } found)
            {
                continue;
            }

            into.Add(new IvpContact
            {
                Body = body,
                Arm = (arm.X, arm.Y, arm.Z),
                Normal = (found.Normal.X, found.Normal.Y, found.Normal.Z),
                Depth = found.Depth,
            });
        }

        return impact;
    }

    /// <summary>When this body's hull first meets the world within an interval, or the interval.</summary>
    /// <param name="body">The body to sweep.</param>
    /// <param name="world">The static world, or null when there is none.</param>
    /// <param name="remaining">How much of the step is left.</param>
    /// <param name="checks">
    /// The step's running collision-check count, which every sweep here adds to —
    /// <c>maxCollisionChecksPerTimestep</c> counts pair tests, not sub-steps.
    /// </param>
    /// <returns>The time of the first impact, or <paramref name="remaining"/> when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **This is what lets the step be subdivided at the impact rather than past it.**
    /// `FUN_180099380` gates a pair on the time REMAINING in the PSI — `env+0x190` less
    /// `env+0x188` — which only means anything if the current time advances inside the step.
    ///
    /// **Swept along the body's own motion**, which is its PREVIOUS velocity: that is what the
    /// integrator will move it by, so it is what decides where it can reach.
    ///
    /// **A point already touching returns no impact.** It has nothing left to cross, and a zero
    /// time here would stall the walk; a resting body is held by the contact found beside this,
    /// not by an event.
    /// </remarks>
    public static float TimeOfImpact(
        IvpRigidBody body, IvpWorldCollision? world, float remaining, ref int checks)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (world is null || body.Immovable || body.Hull.Count == 0 || remaining <= 0f)
        {
            return remaining;
        }

        Quaternion orientation = new(
            body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W);

        Vector3 centre = new((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);

        Vector3 travel = new(
            body.PreviousVelocity.X * remaining,
            body.PreviousVelocity.Y * remaining,
            body.PreviousVelocity.Z * remaining);

        float soonest = remaining;

        for (int index = 0; index < body.Hull.Count; index++)
        {
            // **Every sweep is one collision CHECK against the step's budget**, which is what
            // `maxCollisionChecksPerTimestep` counts — pair tests, not sub-steps. Counting
            // sub-steps instead let one tick run 250 of them, each sweeping every point of every
            // body, and a corpse caught up over six hundred ticks took minutes.
            checks++;

            (float x, float y, float z) = body.Hull[index];

            Vector3 at = centre + Vector3.Transform(new Vector3(x, y, z), orientation);

            if (world.Sweep(at, at + travel) is not { } hit)
            {
                continue;
            }

            float when = hit.Fraction * remaining;

            if (when > FloatEpsilon && when < soonest)
            {
                soonest = when;
            }
        }

        return soonest;
    }

    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) first, (float X, float Y, float Z) second) =>
        ((first.Y * second.Z) - (first.Z * second.Y),
         (first.Z * second.X) - (first.X * second.Z),
         (first.X * second.Y) - (first.Y * second.X));

    /// <summary><c>FLT_EPSILON</c>, the floor the engine's own guards use.</summary>
    private const float FloatEpsilon = 1.1920929e-07f;

    /// <summary>Penetration left unresolved, so resting contacts stop re-triggering.</summary>
    /// <remarks>
    /// **Source units, and a quarter of an inch.** Driving depth to exactly zero makes a resting
    /// body alternate between touching and not, which reads on screen as a corpse vibrating on the
    /// floor.
    /// </remarks>
    private const float Slop = 0.25f;

    /// <summary>How much of the remaining penetration one step removes.</summary>
    /// <remarks>
    /// **0.4, which is the constraint group's own relaxation weight** rather than a second number
    /// chosen here — `docs/findings/51` measured that weight live in the ragdoll solve, and a
    /// contact resolved in the same loop leaking error at the same rate is one constant, not two.
    /// </remarks>
    private const float Recovery = 0.4f;

    /// <summary>The fastest a contact may push a body out, in Source units per second.</summary>
    /// <remarks>
    /// **Not the engine's, and it exists because this project has no swept test.** IVP re-checks a
    /// pair before the bodies reach each other and never sees a six-unit overlap at all; a
    /// discrete test at 66 Hz does, on the first frame of every corpse that falls any distance.
    /// Sixty units a second is about a unit per step — enough to clear a real overlap within a few
    /// frames and far too slow to look like a bounce.
    /// </remarks>
    private const float MaximumRecovery = 60f;
}
