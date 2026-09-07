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
/// **The consumer of that record has been found, and it is `FUN_18008e290`** — reached from the
/// mindist event through `FUN_18008ed60`, which builds the solver's working struct on the stack
/// and hands it over. It replaces what this class used to do, which was an accumulated normal
/// impulse with a separate Coulomb friction pass, invented here because the consumer was unread.
/// Two things about it are not what a textbook solver does, and both are transcribed below:
///
/// - **The impulse is applied in a fixed fraction, repeatedly, until the approach is gone.** Each
///   pass applies `-0.1 · mA · 2 · mB / (mA + mB) · v₀`, where `v₀` is the approach speed measured
///   ONCE before the loop — so the magnitude never changes, and the loop simply runs until
///   `dot(relative velocity, direction)` stops being negative, bounded at a hundred passes:
///   `for (; (0.0 &lt; dVar24 &amp;&amp; (iVar15 &lt; 100)); iVar15 = iVar15 + 1)`. A static partner's mass
///   term is its partner's scaled by `1.0e5`, which is what makes the world immovable here.
/// - **There is ONE impulse, and friction is a direction constraint on it.** `FUN_180090240` sets
///   the direction to the normalised relative velocity, and if that leans further from the normal
///   than the friction cone allows it is clamped onto the cone edge —
///   `dir = normal · (−cos θ) + tangent · sin θ`, with `(cos θ, sin θ)` precomputed at
///   `record+0x134` / `+0x138` from `tan θ = friction`. There is no separate tangential impulse.
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
    /// The approach speed latched before the impulse loop — <c>FUN_18008e290</c>'s <c>fVar17</c>.
    /// </summary>
    /// <remarks>
    /// **Measured once, and every pass's impulse is the same fraction of it.** The engine computes
    /// `dot(relative velocity, normal)` before the loop starts and never recomputes it; only the
    /// TEST that ends the loop reads the live velocity. Recomputing the magnitude each pass would
    /// be a Gauss-Seidel solve, which converges faster and is not what this is.
    /// </remarks>
    public float Approach { get; private set; }

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

    /// <summary>Latches the approach speed every pass's impulse is a fraction of.</summary>
    /// <remarks>
    /// **The engine measures it once and the loop never revises it** — see <see cref="Approach"/>.
    /// A contact that is not approaching by more than <see cref="Approaching"/> takes no impulse
    /// path at all: `if (fVar17 &lt;= _DAT_1800ee398)` gates the whole block, and
    /// `_DAT_1800ee398` dumps as `-1.0E-4`, in metres per second.
    /// </remarks>
    public void Begin()
    {
        Approach = Closing();
        Accumulated = 0f;
    }

    /// <summary>Applies one pass of the engine's fixed sub-impulse.</summary>
    /// <returns>Whether the contact is still approaching, so the loop must run again.</returns>
    /// <remarks>
    /// **The magnitude is `0.2 · effective mass · approach speed`, and both numbers are dumped.**
    /// `dVar12 = DAT_1800fd880 / (dVar19 + dVar18)` with `DAT_1800fd880` = `-0.1` as a double, and
    /// the impulse `dVar12 * dVar19 * (dVar18 + dVar18) * fVar17` reduces to
    /// `-0.2 · mA·mB/(mA+mB) · v₀`. Against the static world `mA = 1.0e5 · mB`, so the harmonic
    /// mean is the moving body's own effective mass and each pass removes a fifth of the approach.
    ///
    /// **The direction is chosen fresh every pass**, because the relative velocity it opposes
    /// changes as the impulses land — <c>FUN_180090240</c> runs at the bottom of the engine's loop
    /// body.
    /// </remarks>
    public bool Oppose()
    {
        float effective = EffectiveInverseMass;

        if (effective <= FloatEpsilon || Approach > Approaching)
        {
            return false;
        }

        if (Closing() >= 0f)
        {
            return false;
        }

        (float X, float Y, float Z) direction = Direction();

        // -0.2 · m · v₀, with v₀ negative when approaching, so this is positive.
        float applied = -PassFraction * Approach / effective;

        Accumulated += applied;

        Push((direction.X * applied, direction.Y * applied, direction.Z * applied));

        return true;
    }

    /// <summary>Pushes the body back out of what it is already inside.</summary>
    /// <param name="step">The timestep the depth is spread over.</param>
    /// <remarks>
    /// **This one is NOT the engine's, and it is here because the mindist scheduler is not.** IVP
    /// re-checks a pair before the two reach each other, so a body never carries a six-unit
    /// overlap and no term exists to remove one; the engine's only post-loop addition is a
    /// separation speed of `sqrt(impacts) · 0.01` metres per second plus a restitution share, which
    /// is about bounce and not about depth. A discrete test at 66 Hz does produce overlaps, so this
    /// leaks a fraction of the remaining depth back as velocity, once per slice rather than once
    /// per pass — inside the loop it would be multiplied by the pass count.
    /// </remarks>
    public void Separate(float step)
    {
        float effective = EffectiveInverseMass;

        if (effective <= FloatEpsilon || step <= 0f || Depth <= Slop)
        {
            return;
        }

        // **Capped, and the cap is the part that was measured rather than reasoned.** Without it a
        // body falling at 400 units a second penetrates six units in one step, and a push
        // proportional to that depth returns it as a hundred-unit-per-second launch: the test body
        // was thrown back to 44 units above a floor it should have been resting on. A corpse would
        // do the same thing and look like it had been shot.
        float bias = MathF.Min(Recovery * (Depth - Slop) / step, MaximumRecovery);

        float applied = bias / effective;

        Accumulated += applied;

        Push((Normal.X * applied, Normal.Y * applied, Normal.Z * applied));
    }

    /// <summary>The contact point's speed along the normal — negative while approaching.</summary>
    private float Closing()
    {
        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, Arm);

        return ((Body.Velocity.X + spin.X) * Normal.X) +
               ((Body.Velocity.Y + spin.Y) * Normal.Y) +
               ((Body.Velocity.Z + spin.Z) * Normal.Z);
    }

    /// <summary>The direction the pass impulse takes — <c>FUN_180090240</c>.</summary>
    /// <remarks>
    /// **It opposes the motion, not the surface**, and only leans back toward the normal when the
    /// friction cone will not stretch far enough to cover it. The cone's half-angle is carried as
    /// the pair `(cos θ, sin θ)` with `tan θ` the friction coefficient, which is what
    /// <c>FUN_18008ed60</c> builds: `fVar2 = 1 − s²/2 + C·s⁴` is the series for `1/sqrt(1 + s²)`
    /// and the partner is `fVar2 · s`.
    ///
    /// **The engine's `s` is `(sqrt(mindist+0x80) + 1) · material friction` and this uses the
    /// material friction alone.** Nothing traced writes `mindist+0x80`, so the factor is between
    /// one and unknown; taking it as one is the minimum of the engine's range rather than a
    /// different rule, and it is flagged rather than smoothed over.
    ///
    /// **The world's own material is still NOT read.** The map's collision text carries a per-hull
    /// table — `MapSurfaceTable.Materials` — and this uses the body's coefficient, so a corpse
    /// slides the same on ice as on wood.
    /// </remarks>
    private (float X, float Y, float Z) Direction()
    {
        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, Arm);

        (float X, float Y, float Z) moving = (
            Body.Velocity.X + spin.X, Body.Velocity.Y + spin.Y, Body.Velocity.Z + spin.Z);

        float speed = MathF.Sqrt(
            (moving.X * moving.X) + (moving.Y * moving.Y) + (moving.Z * moving.Z));

        if (speed <= FloatEpsilon)
        {
            return Normal;
        }

        // Opposing the motion, normalised — the engine's `dir = relative velocity`, normalised.
        (float X, float Y, float Z) against = (
            -moving.X / speed, -moving.Y / speed, -moving.Z / speed);

        float lean = (against.X * Normal.X) + (against.Y * Normal.Y) + (against.Z * Normal.Z);

        float friction = MathF.Max(Body.Friction, 0f);
        float cosine = 1f / MathF.Sqrt(1f + (friction * friction));

        if (lean >= cosine)
        {
            return against;
        }

        // Outside the cone: put the impulse exactly on its edge.
        (float X, float Y, float Z) tangent = (
            against.X - (Normal.X * lean),
            against.Y - (Normal.Y * lean),
            against.Z - (Normal.Z * lean));

        float across = MathF.Sqrt(
            (tangent.X * tangent.X) + (tangent.Y * tangent.Y) + (tangent.Z * tangent.Z));

        if (across <= FloatEpsilon)
        {
            return Normal;
        }

        float sine = friction * cosine;

        return ((Normal.X * cosine) + (tangent.X / across * sine),
                (Normal.Y * cosine) + (tangent.Y / across * sine),
                (Normal.Z * cosine) + (tangent.Z / across * sine));
    }

    /// <summary>Adds one impulse's linear and angular halves to the body.</summary>
    private void Push((float X, float Y, float Z) impulse)
    {
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

                    float distance = soon.Fraction * reach;

                    // **A resting point must not report an IMPACT, and this is what made the walk
                    // useless.** A corpse lying on the floor has points sitting on the surface, so
                    // their sweep crosses at a fraction of nearly zero — and the step's slice is
                    // the minimum over every point of every body, so one resting limb dragged the
                    // whole system down to a hair and the interval was then taken in one move.
                    // Measured: the freeze limit below never once fired, because the bodies going
                    // through floors were not colliding repeatedly — they were never being sliced
                    // at all.
                    //
                    // **The slop is the same distance the contact solve leaves unresolved**, so a
                    // point within it is resting rather than arriving.
                    if (distance > Slop)
                    {
                        float when = distance / MathF.Max(committedLength / step, FloatEpsilon);

                        if (when > FloatEpsilon && when < impact)
                        {
                            impact = when;
                        }
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

    /// <summary>How much of the approach one pass removes — <c>FUN_18008e290</c>.</summary>
    /// <remarks>
    /// **`DAT_1800fd880` is `-0.1` as a double**, dumped in the disassembly rather than read out of
    /// the decompiled expression, and the `(dVar18 + dVar18)` beside it doubles it. So each pass
    /// removes a fifth of the approach speed and the loop converges as `0.8ⁿ` — which is why the
    /// engine needs a bound as large as <see cref="IvpEnvironment.MaximumImpulsePasses"/>.
    /// </remarks>
    private const float PassFraction = 0.2f;

    /// <summary>The approach speed below which no impulse is applied at all.</summary>
    /// <remarks>
    /// **`_DAT_1800ee398` = `-1.0E-4`, in metres per second**, converted here to Source units
    /// because this project's bodies are in inches. A contact slower than this takes the engine's
    /// other branch, which applies friction and nothing else.
    /// </remarks>
    private const float Approaching = -1.0e-4f * IvpWorldCollision.SourceUnitsPerMetre;

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
