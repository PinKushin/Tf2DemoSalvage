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
    }

    /// <summary>Every contact one body's hull makes with the world this step.</summary>
    /// <param name="body">The body to test.</param>
    /// <param name="world">The static world, or null when there is none.</param>
    /// <param name="into">Where the contacts are added.</param>
    /// <param name="step">
    /// The timestep, so a point about to cross a surface raises its contact before it does. Zero
    /// disables that and tests the current position alone.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A body's hull points are tested, not its triangles.** That is the vertex-face case, and it
    /// is the departure <see cref="IvpWorldCollision"/> states: a corpse on a floor touches through
    /// its vertices, and the edge-edge pair IVP also dispatches is absent.
    ///
    /// **An immovable body generates none**, matching the island driver, which does not integrate
    /// one either.
    /// </remarks>
    public static void Find(
        IvpRigidBody body, IvpWorldCollision? world, ICollection<IvpContact> into, float step = 0f)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(into);

        if (world is null || body.Immovable || body.Hull.Count == 0)
        {
            return;
        }

        Quaternion orientation = new(
            body.Orientation.X, body.Orientation.Y, body.Orientation.Z, body.Orientation.W);

        Vector3 centre = new((float)body.Position.X, (float)body.Position.Y, (float)body.Position.Z);

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
                // **Two steps, and the reason is the integrator's one-step lag.** `IvpIntegrator`
                // moves a body by its PREVIOUS velocity — `core[0x150] += core[0x170] * dt`, and
                // only then `core[0x170] = core[0x140]` — so this step's motion is already decided
                // before any impulse is applied, and an impulse raised now takes effect on the step
                // after. Predicting with the current velocity alone therefore raises the contact
                // exactly one step too late, which is what left a body 2,775 units under a
                // sixteen-unit floor while reporting contacts the whole way down.
                Vector3 ahead = centre + arm + new Vector3(
                    (body.PreviousVelocity.X + body.Velocity.X) * step,
                    (body.PreviousVelocity.Y + body.Velocity.Y) * step,
                    (body.PreviousVelocity.Z + body.Velocity.Z) * step);

                if (world.Entry(centre + arm, ahead) is { } soon)
                {
                    hit = (soon, 0f);
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
