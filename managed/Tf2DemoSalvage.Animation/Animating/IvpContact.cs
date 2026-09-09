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

    /// <summary>Which hull point raised it.</summary>
    public required int Point { get; init; }

    /// <summary>Which WORLD face it is against — the retained half of a closest-feature pair.</summary>
    /// <remarks>
    /// **A contact solved across steps has to recognise itself, and a normal cannot do it.** This
    /// was keyed on the contact normal and before that on the hull point, and both lose their
    /// state exactly when a body settles: the shallowest face of a ledge changes as the body sinks
    /// into it, and which vertex is first in the list changes as it rocks. A ledge and a plane
    /// within it do not change while the body rests on that face.
    ///
    /// **`IvpWorldCollision.Touching` produces it**, and it is the identity half of what IVP's
    /// mindist keeps. The other half — which feature of the MOVING body is closest — this project
    /// still does not have, because it tests every hull vertex against the world every step.
    /// </remarks>
    public required int Feature { get; init; }

    /// <summary>The id a contact that has not happened yet is filed under.</summary>
    private const int Speculative = -1;


    /// <summary>The point this feature's contact acts at, held steady between steps.</summary>
    /// <param name="fresh">The manifold's centroid as measured this step, in world directions.</param>
    /// <returns>The retained point if there is one, else <paramref name="fresh"/>.</returns>
    /// <remarks>
    /// **This is the BODY half of a closest-feature pair, and without it the world half is not
    /// worth much.** A mindist names a feature on each side and keeps both; naming only the world
    /// face leaves the retained impulse applying at whatever point this step's contact set happens
    /// to average to, and that set reshuffles — a vertex that was inside last step is outside this
    /// one, the centroid jumps, and a force that is supposed to be holding a body steady moves
    /// under it every tick.
    ///
    /// **Stored in the body's own space**, so it turns with the body rather than having to be
    /// re-measured, which is what makes it the same point and not merely a similar one.
    ///
    /// **It follows the fresh measurement slowly rather than being frozen**, because a body really
    /// does roll onto a different part of itself and a point locked for ever would be a different
    /// bug — the same one that made locking to a hull vertex measure worse than not locking at all.
    /// The weight is the constraint group's relaxation, which is the rate everything else in this
    /// solver carries state forward at.
    /// </remarks>
    public (float X, float Y, float Z) Steady((float X, float Y, float Z) fresh)
    {
        if (Feature == Speculative)
        {
            return fresh;
        }

        // The inverse rotation, which for a unit quaternion is its conjugate.
        (float X, float Y, float Z, float W) back = (
            -Body.Orientation.X, -Body.Orientation.Y, -Body.Orientation.Z, Body.Orientation.W);

        (float X, float Y, float Z) local = IvpQuaternion.Rotate(back, fresh);

        int slot = Remembered();

        if (slot < 0)
        {
            Body.Sliding.Add((Normal, 0f, 0f, 0f, local, Point));

            return fresh;
        }

        (float X, float Y, float Z) kept = Body.Sliding[slot].Local;

        float weight = IvpConstraintGroup.Relaxation;

        (float X, float Y, float Z) blended = (
            kept.X + ((local.X - kept.X) * weight),
            kept.Y + ((local.Y - kept.Y) * weight),
            kept.Z + ((local.Z - kept.Z) * weight));

        Body.Sliding[slot] = (
            Normal,
            Body.Sliding[slot].Holding,
            Body.Sliding[slot].First,
            Body.Sliding[slot].Second,
            blended,
            Point);

        return IvpQuaternion.Rotate(Body.Orientation, blended);
    }

    /// <summary>Whether this contact's manifold has already been rubbed this slice.</summary>
    /// <remarks>
    /// **The manifold is found by walking, so its members have to be marked.** Every contact of a
    /// body sharing a normal is one feature and takes one friction solve between them; the rest are
    /// folded into the centroid and the summed normal impulse rather than solved again.
    /// </remarks>
    public bool Rubbed { get; set; }

    /// <summary>The impulse applied so far, accumulated across iterations.</summary>
    public float Accumulated { get; private set; }

    /// <summary>How far UP the last position correction moved this body, in Source units.</summary>
    public float Lifted { get; private set; }

    /// <summary>The friction impulse the last solve ASKED for, before the cone.</summary>
    public float Wanted { get; private set; }

    /// <summary>And what the Coulomb cone allowed it — <c>friction × the normal impulse</c>.</summary>
    public float Allowed { get; private set; }

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
    public float EffectiveInverseMass => InverseMassAlong(Normal);

    /// <summary>The effective inverse mass along one direction, in the frame we apply it in.</summary>
    /// <param name="direction">A unit direction in world space.</param>
    /// <remarks>
    /// **Same physical quantity as the expression above, evaluated where it is USED, and that
    /// distinction was worth a day.** The engine's `rx*rx * core[+0x44] + …` is written in IVP's
    /// own contact frame, which is built by explicit cross products before the record is filled —
    /// so the cross product is not absent, it has already happened. Copying the expression while
    /// applying the impulse in WORLD space silently changes what it means, and the result is a
    /// number that no longer predicts what its own impulse will do.
    ///
    /// **The symptom was a contact that could not converge.** `Oppose` divides by this to size a
    /// pass and then applies the impulse through `InverseMass` and `InverseInertia` separately; if
    /// the two disagree, a pass removes less of the approach than it charged for. Measured: a body
    /// arriving at 230 units per second ran all hundred passes and was still approaching, because
    /// each one was worth about 0.66 rather than the 46 it was priced at. The loop hit its bound
    /// every slice, the body kept its speed, and the ragdoll tore itself apart on the joint.
    ///
    /// **`r × d` restored, because our `r` is a world-space lever arm**:
    /// `1/m + d · ((I⁻¹ (r × d)) × r)`, which is the standard scalar and reduces to the engine's
    /// form in the frame the engine writes it in.
    /// </remarks>
    public float InverseMassAlong((float X, float Y, float Z) direction) =>
        InverseMassAlong(direction, Arm);

    /// <summary>The same, about an arm that may be a manifold's centroid rather than this point.</summary>
    /// <param name="direction">A unit direction in world space.</param>
    /// <param name="arm">The lever arm to measure about.</param>
    public float InverseMassAlong(
        (float X, float Y, float Z) direction, (float X, float Y, float Z) arm)
    {
        (float X, float Y, float Z) turn = Cross(arm, direction);

        (float X, float Y, float Z) twist = Cross(
            (turn.X * Body.InverseInertia.X,
             turn.Y * Body.InverseInertia.Y,
             turn.Z * Body.InverseInertia.Z),
            arm);

        return Body.InverseMass +
            (direction.X * twist.X) + (direction.Y * twist.Y) + (direction.Z * twist.Z);
    }

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

    /// <summary>Runs this contact's own impulse loop until it stops approaching.</summary>
    /// <returns>How many passes it took — zero when there was nothing to oppose.</returns>
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
    ///
    /// **The loop belongs HERE, to one contact, and putting it anywhere else is a mis-transcription
    /// that the cost gives away.** `FUN_18008e290` solves ONE mindist — one pair of objects — and
    /// iterates that pair's own approach to zero. Wrapping the same hundred-pass bound around a
    /// sweep over every contact a ragdoll has is a different algorithm: it re-solves settled
    /// contacts a hundred times a slice and lets the joint solve re-supply the approach in between.
    /// Measured that way, six ticks of a single corpse ran past four hundred seconds and the owner
    /// stopped it — *"if this is partiy it shouldnt be doing this"*, which is the right test. The
    /// engine runs a server full of ragdolls at sixty-six ticks a second; a transcription that
    /// cannot is not a transcription.
    /// </remarks>
    public int Oppose() => Oppose(Arm);

    /// <summary>The same, run once for a MANIFOLD at its centroid.</summary>
    /// <param name="arm">The manifold's centroid rather than this point's own arm.</param>
    /// <remarks>
    /// **One arrival is one impulse.** Running the loop per touching vertex gives a body as many
    /// arrival impulses as it has corners on the floor, which is the same over-application the
    /// friction and separation passes had — see <see cref="Separate(float, ValueTuple{float, float, float}, float)"/>.
    /// </remarks>
    public int Oppose((float X, float Y, float Z) arm)
    {
        if (InverseMassAlong(Normal, arm) <= FloatEpsilon || Approach > Approaching)
        {
            return 0;
        }

        int passes = 0;

        while (passes < MaximumPasses && Closing(arm) < 0f)
        {
            (float X, float Y, float Z) direction = Direction(arm);

            // **Priced along the direction it is about to push**, not along the normal. The engine
            // does the same — `FUN_1800770f0` is called AFTER `FUN_180090240` has chosen the
            // direction, so `dVar18`/`dVar19` are the masses along that direction and not along the
            // surface. Using the normal's value here is what made a pass cost less than it charged.
            float along = InverseMassAlong(direction, arm);

            if (along <= FloatEpsilon)
            {
                break;
            }

            // -0.2 · m · v₀, with v₀ negative when approaching, so this is positive. The SPEED is
            // the one measured before the loop and never revised, which is the engine's shape; only
            // the mass it is divided by follows the direction.
            float applied = -PassFraction * Approach / along;

            // **Never past zero, which is the engine's calibrated final impulse in the form this
            // solver can use.** `FUN_18008e290` does not simply stop when the approach is gone: it
            // applies a UNIT impulse, remeasures what that did, subtracts its own accumulated
            // deltas back out of both bodies and reapplies the exact multiple the measurement calls
            // for — `if ((0.0 < dVar18) && (iVar15 != 100))`, then `dVar18 / dVar24`. The point of
            // that step is that the last impulse lands exactly rather than overshooting.
            //
            // **Skipping it left a permanent 20% restitution.** Each pass adds a fifth of the
            // ORIGINAL approach and the loop exits on the first pass past zero, so a landing could
            // end up to a fifth of its impact speed travelling upward — measured as a corpse
            // bouncing between z 7.6 and z 17 for ever, at 62 to 280 units a second, never
            // decaying. Clamping the last pass to what is actually left is the same landing the
            // calibration produces, without the unit-impulse probe our single-body case does not
            // need.
            float exact = -Closing(arm) / along;

            if (exact > 0f && applied > exact)
            {
                applied = exact;
            }

            Accumulated += applied;

            Push((direction.X * applied, direction.Y * applied, direction.Z * applied), arm);

            passes++;
        }

        return passes;
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
    public void Separate(float step) => Separate(step, Arm, Depth);

    /// <summary>Pushes a MANIFOLD back out, once, at its centroid.</summary>
    /// <param name="step">The timestep the depth is spread over.</param>
    /// <param name="arm">The manifold's centroid, not this point's own arm.</param>
    /// <param name="depth">The deepest penetration anywhere in the manifold.</param>
    /// <remarks>
    /// **Per contact this was the largest energy source in the solve.** Every touching vertex
    /// pushed independently at up to <see cref="MaximumRecovery"/>, and a corpse lying on the
    /// ground has sixteen to thirty-six of them — so one slice could hand a body a thousand units
    /// a second of outward velocity. Traced on a real scout ragdoll dropped on
    /// `koth_harvest_final`, it produced a permanent limit cycle: 200 to 350 units a second for ten
    /// seconds, the root bouncing between z 2 and z 12, never decaying and never settling.
    ///
    /// **A face resting on a face is ONE contact to IVP**, so one push, and this takes the
    /// manifold's deepest point because that is the overlap that has to clear.
    /// </remarks>
    public void Separate(float step, (float X, float Y, float Z) arm, float depth)
    {
        float effective = InverseMassAlong(Normal, arm);

        if (effective <= FloatEpsilon || step <= 0f)
        {
            return;
        }

        int slot = Remembered();

        float held = slot < 0 ? 0f : Body.Sliding[slot].Holding;

        // **The stored normal impulse is carried forward, and that is what HOLDS a resting body.**
        // Without it nothing in this solve supports a corpse that has landed: `Oppose` fires only
        // while a contact is approaching — the engine's `fVar17 <= _DAT_1800ee398` gate — so the
        // only support was a shove applied whenever the body was found inside something. Measured,
        // that is a limit cycle: sink, shove, fly, fall, sink, at 77 to 262 units a second for ten
        // seconds without decaying. Switching the shove off dropped the same ragdoll through the
        // map at five to eight hundred.
        //
        // **The engine's contact is persistent** — a friction-system contact carries its normal
        // term between PSIs (`contact+0x78`, half the Coulomb product `FUN_1800857c0` clamps
        // against) — so a body is held continuously rather than ejected repeatedly. This is that
        // state, warm-started by the same relaxation weight the tangential pair uses.
        // **Carried in FULL, not decayed, because a body at rest has nothing to re-derive it from.**
        // This warm-started at the relaxation weight like the tangential pair, and that is wrong for
        // the normal: the correction below is driven by the closing velocity, and a body in
        // equilibrium has none — so the stored support decayed geometrically to nothing, the
        // Coulomb limit `friction × Accumulated` went with it, and friction stopped existing exactly
        // when it was needed. Measured on a slope with a coefficient of ten times what the gradient
        // demands: the body slid at a steady 37 units a second with the whole solve dissipating,
        // `separate` contributing 0 and `rub` 3.6 — no pump, no budget either.
        //
        // **The engine's contact holds a real normal force between PSIs** (`contact+0x78`), not a
        // residue of how fast something was still sinking.
        float carried = held;

        if (carried > 0f)
        {
            Push((Normal.X * carried, Normal.Y * carried, Normal.Z * carried), arm);
            Accumulated += carried;
        }

        // **Stop it sinking first, THEN remove any overlap.** The first term is what a resting
        // contact is for and the second is this project's own, needed only because there is no
        // mindist scheduler to stop an overlap forming — see the remarks.
        //
        // **The BODY's linear velocity alone was tried here and measured WORSE.** Traced first: on
        // a rocking body the warm-started support went to exactly zero on many individual steps
        // with the same normal and the same slot, because `total` below clamps to zero WHOLESALE
        // and `Closing(arm)`'s spin term `ω × arm` swings through zero as the contact tips — so a
        // reading using linear velocity alone, dropping that spin term, looked like the fix. It
        // made the same test's landing speed WORSE, 31 units a second to 67. The spin term is
        // carrying real information this arm needs — a rocking body's rotation is still resting
        // weight on the surface it is rocking against, not noise to be filtered from the release
        // decision — so `Closing(arm)` is kept, and the actual zero-out this was chasing is
        // recorded rather than patched again on a guess: `total`'s all-or-nothing clamp is the
        // more likely target, not which velocity feeds it.
        float closing = Closing(arm);

        // **The hold alone does NOT support a body, and that is measured rather than assumed.**
        // Switching this term off drops the same ragdoll through the map to −1519 even with the
        // stored normal impulse carried and warm-started. So the persistence built above is not yet
        // doing the engine's job: `Remembered` keys on the contact normal, and a body settling into
        // a surface changes which face is shallowest, so the slot it accumulates into changes and
        // the support resets. IVP does not have that problem because its mindist keeps a
        // closest-feature PAIR rather than re-deriving a face each step.
        //
        // **Which is the same missing narrow phase four other measurements have pointed at.** Until
        // it exists this depth term is what actually holds a corpse up, and it is this project's
        // and not the engine's.
        float bias = depth > Slop
            ? MathF.Min(Recovery * (depth - Slop) / step, MaximumRecovery)
            : 0f;

        // **The depth is corrected in POSITION, not in velocity, and that is what stops the pump.**
        // Measured directly: with the solve's energy split three ways per tick, `Oppose` and `Rub`
        // both REMOVE kinetic energy and this term added between 28,000 and 46,000 — against
        // gravity's 7,500 — every tick, for ever. That is not a tuning error. Giving a body
        // velocity to fix a position error leaves the velocity behind once the error is gone, so
        // the body arrives back at the surface with speed it did not have before, and the cycle
        // pays for itself.
        //
        // **The engine has no such term at all**, because its mindist scheduler re-checks a pair
        // before the two reach each other and a body never carries an overlap. Ours exists only to
        // compensate for not having that, and the honest form of a compensator for a POSITION error
        // is a position correction: the body is moved out of what it is inside and gains nothing.
        if (bias > 0f)
        {
            float shift = MathF.Min(depth - Slop, bias * step);

            Body.Position = (
                Body.Position.X + (Normal.X * shift),
                Body.Position.Y + (Normal.Y * shift),
                Body.Position.Z + (Normal.Z * shift));


            // **How far UP this correction moved the body, which is the one term the energy split
            // cannot see.** `Energy()` is kinetic; a position correction adds POTENTIAL energy and
            // shows as nothing, so a solver lifting bodies for free reads as three passes all
            // dissipating while the thing speeds up. That is exactly what was measured — gravity
            // handing back 96,000 a tick to a corpse whose height never changed — and it stayed
            // hidden because the instrument had no term for it.
            Lifted = shift * Normal.Z;
        }
        else
        {
            Lifted = 0f;
        }

        // **Signed, so a contact carrying too much support gives it back.** Clamping this to the
        // sinking case alone would let the full carry above accumulate without ever unwinding, and
        // a body being pushed off a surface it is no longer resting on is the same launch the old
        // velocity recovery produced. The TOTAL is what is clamped non-negative, not this.
        float extra = -closing / effective;

        // **Bounding one step's release to a fraction of what is held — rather than clamping the
        // whole total to zero — was also tried and also measured WORSE.** Landing speed went from
        // 31 units a second to 42, a smaller regression than the linear-velocity attempt above but
        // a regression. Reverted. Both experiments assumed the zero-out itself is the fault; the
        // measurement says otherwise twice now, which is worth more than either guess: whatever the
        // real mechanism is, it is not upstream of this line in a way either the READING (`closing`)
        // or the CLAMP SHAPE (`total`'s floor) can fix in isolation. The next hypothesis needs to
        // explain why LOOSENING the clamp in two different ways both made a resting body slide
        // faster, which neither attempt predicted going in.
        //
        // **The TOTAL is clamped non-negative, not the increment**, which is what lets a later
        // slice pull back an earlier one's over-correction while never letting a contact suck a
        // body down.
        float total = MathF.Max(0f, carried + extra);

        float applied = total - carried;

        if (applied != 0f)
        {
            Push((Normal.X * applied, Normal.Y * applied, Normal.Z * applied), arm);
            Accumulated += applied;
        }

        if (Feature == Speculative)
        {
            return;
        }

        if (slot < 0)
        {
            Body.Sliding.Add((Normal, total, 0f, 0f, arm, Point));
        }
        else
        {
            Body.Sliding[slot] = (
                Normal,
                total,
                Body.Sliding[slot].First,
                Body.Sliding[slot].Second,
                Body.Sliding[slot].Local,
                Point);
        }

    }

    /// <summary>Opposes sliding at a contact that is resting rather than arriving.</summary>
    /// <remarks>
    /// **The impact solver's cone does nothing for a body that has already landed**, because
    /// `Oppose` returns on the first line for a contact that is not approaching — the engine's own
    /// `if (fVar17 &lt;= _DAT_1800ee398)` gate. So a resting corpse had no tangential force at all
    /// once the impact path was transcribed, and the friction that used to be here went with it.
    ///
    /// **Measured, and the measurement corrected my own claim.** A body dropped on a one-in-ten
    /// slope with a coefficient of 1 slid steadily UPHILL at about twelve units a second, unchanged
    /// between two seconds and six. I called that a ratchet from re-picking a contact; the owner
    /// asked whether it was just a body still slowing down, which was the right question and is
    /// ruled out by the speed being the same at both times. It slides because nothing opposes it.
    ///
    /// **This is the friction SYSTEM's job in the engine** — `FUN_1800836b0` walks each system's
    /// contacts once, having first summed a budget across all of them, and `FUN_1800857c0` solves
    /// one contact's tangential pair against it. That whole structure is still unbuilt; what is
    /// here is the per-contact half: Coulomb, limited by the normal impulse this contact actually
    /// applied, applied once. The SHARED budget is the part still missing, and with it the
    /// clamping would be a system property rather than a per-contact one.
    /// </remarks>
    public void Rub(float weight) => Rub(weight, Arm, Accumulated);

    /// <summary>Solves one contact's friction at a given arm, with a given normal impulse.</summary>
    /// <param name="weight">The relaxation weight the warm start decays by.</param>
    /// <param name="arm">Where to apply it — a MANIFOLD's centroid, not this point's own arm.</param>
    /// <param name="normal">The normal impulse the cone is limited by, summed over the manifold.</param>
    /// <remarks>
    /// **The arm and the normal impulse are parameters because a face contact is ONE contact.** A
    /// box resting on a floor is a single face-face feature pair to IVP — one mindist, one friction
    /// contact — where this project raises one per hull vertex. Solving each vertex's slip in turn
    /// is not the same operator and provably not friction: with the warm start off, so that each
    /// contact simply drove its own slip to zero, a sliding body SPED UP from 12 units a second to
    /// 21.3. Each cancellation retunes the body's spin and the next vertex then cancels a slip the
    /// previous one just created.
    ///
    /// **So the manifold is solved once, at its centroid, against its summed normal impulse**,
    /// which is the cardinality the engine has even though the feature dispatch behind it is not
    /// built. See <see cref="IvpEnvironment"/>'s resolve for the grouping.
    /// </remarks>
    public void Rub(float weight, (float X, float Y, float Z) arm, float normal)
    {
        // **PREDICTED contacts are rubbed too, and gating them out was tried and is worse.**
        // `Find` raises a contact for a point a sweep says will arrive within the lookahead, with
        // its depth set to exactly zero, so the solve can stop the body at the surface rather than
        // after it. Those are not touching, and the engine's equivalent is a scheduled mindist
        // rather than a member of a friction system — so refusing them looks obviously right.
        //
        // **Measured both ways, and neither is correct yet.** Rubbing them, a body on a slope comes
        // to REST but sits four units above it, where the most a two-unit cube can sit above a
        // plane it touches is `sqrt(3)`. Refusing them, the same body slides at 11.8 units a second
        // and never stops, because almost all of its contacts are predicted rather than
        // penetrating — this solver holds bodies just off a surface instead of on it.
        //
        // **Resting slightly high is the closer of the two**, so they are rubbed; the real fix is
        // that a resting body should have touching contacts at all, which is the mindist keeping a
        // distance rather than a sweep predicting an arrival.
        if (Body.Friction <= 0f)
        {
            return;
        }

        (float X, float Y, float Z) first = Tangent(Normal);
        (float X, float Y, float Z) second = Cross(Normal, first);

        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, arm);

        (float X, float Y, float Z) moving = (
            Body.Velocity.X + spin.X, Body.Velocity.Y + spin.Y, Body.Velocity.Z + spin.Z);

        int slot = Remembered();

        (float A, float B) stored = slot < 0
            ? (0f, 0f)
            : (Body.Sliding[slot].First, Body.Sliding[slot].Second);

        float slipFirst = Dot(moving, first);
        float slipSecond = Dot(moving, second);

        // **The stored pair is a SLIP VELOCITY, not an impulse, and getting that wrong injected
        // energy.** `FUN_1800857c0` forms `param_2[1] * f(contact + 0x6c) - local_64`, and
        // `local_64` is an output of `FUN_18009ca70`, which builds each core's Jacobian through
        // `FUN_18009d010` — so it is a relative velocity along that tangent. The term subtracted
        // from it must carry the same units, so `contact+0x6c` is velocity-like too.
        //
        // **Read that way the warm start is a DECAY, which is what makes it friction.** The target
        // is a fraction of last step's slip, so a sliding contact is asked to keep 40% of what it
        // had and loses the rest every step; a resting one is asked for zero and stays there. Read
        // as an accumulated impulse instead — which is what this did first — the right-hand side
        // adds an impulse to a velocity, and a sliding body sped up from 12 units a second to 15.8.
        // **`rub` is measured contributing a small POSITIVE energy — friction acting as a motor —
        // and the obvious clamp for it measured worse.** The warm start asks for a fraction of last
        // step's slip, so when the body has already slowed below that by other means the difference
        // is positive and the impulse pushes it back up to speed. Refusing any target faster than
        // the current slip took a sliding body from 25.4 units a second to 39.3, because the same
        // clamp fires in the ordinary case and switches friction off altogether: the stored slip is
        // routinely larger than the live one, so clamping to the live one asks for no change at all.
        //
        // **That says the stored value is stale rather than that the target is wrong.** It is
        // written after each solve, and there are several slices per tick, so what is read back is
        // several corrections behind. The engine's contact is solved once per PSI and does not have
        // that gap. Recorded rather than clamped over.
        float wantFirst = (weight * stored.A) - slipFirst;
        float wantSecond = (weight * stored.B) - slipSecond;

        // The symmetric 2x2 effective mass across the two tangents — `FUN_1800868d0` is handed
        // `a, b, b, d`, the same value twice, which is what makes it symmetric.
        float a = InverseMassAlong(first, arm);
        float d = InverseMassAlong(second, arm);
        float b = Coupling(first, second, arm);

        float determinant = (a * d) - (b * b);

        if (MathF.Abs(determinant) <= FloatEpsilon)
        {
            return;
        }

        float inverse = 1f / determinant;

        float impulseFirst = ((d * wantFirst) - (b * wantSecond)) * inverse;
        float impulseSecond = ((a * wantSecond) - (b * wantFirst)) * inverse;

        // **The cone clamps the PAIR after the solve**, scaling both, which is not the same as
        // limiting a single magnitude before it.
        float limit = Body.Friction * normal;
        float size = MathF.Sqrt((impulseFirst * impulseFirst) + (impulseSecond * impulseSecond));

        // **Carried out of the solve rather than recomputed by a caller** (B243). "Friction is not
        // holding it" has two entirely different causes — the cone refusing the impulse, and the
        // solve not asking for one — and only the pair of numbers tells them apart.
        Wanted = size;
        Allowed = limit;

        if (size > limit && size > FloatEpsilon)
        {
            impulseFirst = impulseFirst / size * limit;
            impulseSecond = impulseSecond / size * limit;
        }

        Push(
            (
                (first.X * impulseFirst) + (second.X * impulseSecond),
                (first.Y * impulseFirst) + (second.Y * impulseSecond),
                (first.Z * impulseFirst) + (second.Z * impulseSecond)),
            arm);

        // **The slip that RESULTED is what next step warms from**, measured after the impulse
        // rather than predicted from it, so a clamped solve stores the slip it actually left
        // behind and not the one it was aiming for.
        (float X, float Y, float Z) after = Cross(Body.AngularVelocity, arm);

        (float X, float Y, float Z) settled = (
            Body.Velocity.X + after.X, Body.Velocity.Y + after.Y, Body.Velocity.Z + after.Z);

        if (Feature == Speculative)
        {
            return;
        }

        if (slot < 0)
        {
            Body.Sliding.Add((Normal, 0f, Dot(settled, first), Dot(settled, second), arm, Point));
        }
        else
        {
            Body.Sliding[slot] = (
                Normal,
                Body.Sliding[slot].Holding,
                Dot(settled, first),
                Dot(settled, second),
                Body.Sliding[slot].Local,
                Point);
        }
    }

    /// <summary>Where this feature's slip was filed last step, or −1 when it is new.</summary>
    /// <remarks>
    /// **Matched by normal, because that is what identifies the feature** — see
    /// <see cref="IvpRigidBody.Sliding"/> for the measurement that ruled out matching by vertex.
    /// The list is one entry per surface a body rests against, so it is a handful at most and a
    /// linear walk is the right shape; a body that stops touching a surface simply stops finding
    /// its entry, and the stale one costs a slot rather than a wrong answer.
    /// </remarks>
    /// <summary>How closely two normals must agree to count as the same warm-start slot.</summary>
    /// <remarks>The same threshold <c>IvpEnvironment.Shared</c> groups a manifold by — about eight
    /// degrees, wide enough to survive float noise and narrow enough not to merge two real
    /// faces.</remarks>
    private const float SameNormal = 0.99f;

    private int Remembered()
    {
        if (Feature == Speculative)
        {
            return -1;
        }

        // **Keyed by NORMAL, not by the exact feature id, and the two are not the same thing.**
        // Two coplanar triangles — the common case for a flat slope built from more than one, or a
        // brush face the compiler split — share a normal but not a feature id once terrain
        // identifies itself per triangle. Matching the id exactly reset this warm start every time
        // the representative contact happened to land on the OTHER triangle of the same flat
        // surface, which is indistinguishable from a real change of surface if only the id is
        // compared. The doc this replaces already named normal identity as the correct key
        // — see the remarks above <see cref="IvpRigidBody.Sliding"/> — the comparison just never
        // matched what was actually stored.
        //
        // **And ALSO by which hull point, once friction started being solved at every manifold
        // member rather than one.** Normal alone collided every point resting on the same flat
        // surface onto the SAME slot, so within one step's multi-point loop each point's own Rub
        // call overwrote the tangential slip the point processed just before it — only the last
        // point in the list ever kept a real warm start. A slope test measured at 20 units a
        // second with the multi-point fix landed there DESPITE this bug, not because it was absent.
        for (int index = 0; index < Body.Sliding.Count; index++)
        {
            if (Body.Sliding[index].Point != Point)
            {
                continue;
            }

            (float X, float Y, float Z) stored = Body.Sliding[index].Normal;

            float cosine = (stored.X * Normal.X) + (stored.Y * Normal.Y) + (stored.Z * Normal.Z);

            if (cosine >= SameNormal)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>A tangent to the normal, chosen the same way every step.</summary>
    /// <remarks>
    /// **Deterministic, because a warm start is meaningless in a basis that moves.** The stored
    /// pair means nothing unless the two directions it is expressed in are the same next step, and
    /// IVP keeps its own pair on the mindist for exactly that reason. Picking the axis the normal
    /// leans on least gives a basis that is stable while the normal is.
    /// </remarks>
    private static (float X, float Y, float Z) Tangent((float X, float Y, float Z) normal)
    {
        (float X, float Y, float Z) axis = MathF.Abs(normal.Z) < 0.7f
            ? (0f, 0f, 1f)
            : (1f, 0f, 0f);

        (float X, float Y, float Z) tangent = Cross(normal, axis);

        float length = MathF.Sqrt(
            (tangent.X * tangent.X) + (tangent.Y * tangent.Y) + (tangent.Z * tangent.Z));

        return length <= FloatEpsilon
            ? (1f, 0f, 0f)
            : (tangent.X / length, tangent.Y / length, tangent.Z / length);
    }

    /// <summary>The off-diagonal of the tangential effective-mass matrix.</summary>
    private float Coupling(
        (float X, float Y, float Z) first,
        (float X, float Y, float Z) second,
        (float X, float Y, float Z) arm)
    {
        (float X, float Y, float Z) turn = Cross(arm, second);

        (float X, float Y, float Z) twist = Cross(
            (turn.X * Body.InverseInertia.X,
             turn.Y * Body.InverseInertia.Y,
             turn.Z * Body.InverseInertia.Z),
            arm);

        return Dot(first, twist);
    }

    private static float Dot(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);


    /// <summary>The contact point's speed along the normal — negative while approaching.</summary>
    private float Closing() => Closing(Arm);

    /// <summary>The same, about a given arm.</summary>
    private float Closing((float X, float Y, float Z) arm)
    {
        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, arm);

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
    private (float X, float Y, float Z) Direction((float X, float Y, float Z) arm)
    {
        (float X, float Y, float Z) spin = Cross(Body.AngularVelocity, arm);

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
    /// <summary>Contacts between one body's hull and the terrain triangles near it (B306).</summary>
    /// <param name="body">The body.</param>
    /// <param name="world">The static world.</param>
    /// <param name="centre">The body's position, in Source units.</param>
    /// <param name="orientation">Its orientation.</param>
    /// <param name="into">Where contacts are appended.</param>
    /// <param name="nearby">A reused buffer for the sphere query.</param>
    /// <returns>How many triangles produced a contact.</returns>
    /// <remarks>
    /// **This is the engine's question, and the per-vertex path is not.** vphysics reaches a
    /// displacement through `IVirtualMeshEvent::GetTrianglesInSphere` and collides the object's
    /// HULL against each triangle as a shape (`public/vphysics/virtualmesh.h`); this project
    /// instead walked the body's vertices and asked which triangle PLANE each was least far behind.
    /// A vertex against a plane cannot report an edge-edge touch and its "depth" is a plane
    /// distance rather than a penetration between two shapes.
    ///
    /// **A face-face contact needs SEVERAL points, which is why the faces had to be carried.** The
    /// incident face — the body face most nearly parallel to the triangle and facing it — is
    /// clipped against the triangle's three side planes, and every clipped point at or below the
    /// triangle's plane becomes a contact with its own depth. A box resting flat therefore gets the
    /// corners it actually rests on from ONE pair, where a single closest-point answer gives one
    /// and a per-vertex walk gives whatever the sampling happens to catch.
    ///
    /// **Side planes, not the triangle's own plane, decide containment**, so a point beyond an edge
    /// is dropped rather than pushed by a face it has already slid off.
    /// </remarks>
    private static int AgainstTerrain(
        IvpRigidBody body,
        IvpWorldCollision world,
        Vector3 centre,
        Quaternion orientation,
        ICollection<IvpContact> into,
        List<(IvpWorldTriangle Shape, int Index)> nearby)
    {
        if (body.Faces.Count == 0 || body.Hull.Count == 0)
        {
            return 0;
        }

        float reach = 0f;

        Vector3[] points = new Vector3[body.Hull.Count];

        for (int index = 0; index < body.Hull.Count; index++)
        {
            (float x, float y, float z) = body.Hull[index];

            points[index] = centre + Vector3.Transform(new Vector3(x, y, z), orientation);

            reach = MathF.Max(reach, (points[index] - centre).Length());
        }

        nearby.Clear();

        world.TrianglesInSphere(centre, reach + Slop, nearby);

        int found = 0;

        foreach ((IvpWorldTriangle shape, int index) in nearby)
        {
            // **The whole body above the triangle's plane cannot touch it**, and this is the cheap
            // reject that keeps a sphere query affordable.
            float highest = float.NegativeInfinity;
            float lowest = float.PositiveInfinity;

            for (int point = 0; point < points.Length; point++)
            {
                float above = Vector3.Dot(shape.Normal, points[point]) - shape.Distance;

                highest = MathF.Max(highest, above);
                lowest = MathF.Min(lowest, above);
            }

            if (lowest > Slop || highest < -TerrainReach)
            {
                continue;
            }

            // The incident face: the one facing the triangle most squarely.
            int incident = -1;
            float facing = 0f;

            for (int face = 0; face < body.Faces.Count; face++)
            {
                (int a, int b, int c) = body.Faces[face];

                if (a >= points.Length || b >= points.Length || c >= points.Length)
                {
                    continue;
                }

                Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);

                if (normal.LengthSquared() <= FloatEpsilon)
                {
                    continue;
                }

                float against = Vector3.Dot(Vector3.Normalize(normal), shape.Normal);

                if (incident < 0 || against < facing)
                {
                    incident = face;
                    facing = against;
                }
            }

            if (incident < 0)
            {
                continue;
            }

            (int first, int second, int third) = body.Faces[incident];

            List<Vector3> polygon =
                [points[first], points[second], points[third]];

            polygon = Clip(polygon, shape);

            bool any = false;

            foreach (Vector3 at in polygon)
            {
                float depth = shape.Distance - Vector3.Dot(shape.Normal, at);

                // **A point about to meet the face gets the engine's PRE-contact, not an overlap**
                // (B306). IVP's mindist scheduler raises a pair before the surfaces touch, which is
                // why the engine never carries a penetration to remove; `Find`'s per-vertex path
                // already does this with a sweep, and this pass had no equivalent — so a body
                // penetrated first and `Separate` had to lift it back out, which is a free-energy
                // pump measured keeping a cube tumbling on a slope too shallow to sustain one.
                //
                // **Filed as `Speculative` and at zero depth**, exactly as the sweep's is: it has no
                // overlap to resolve and no retained identity to accumulate, only a closing
                // velocity to cancel. Admitting these as ORDINARY terrain contacts instead — with
                // their own feature and warm start — measured 31.382078f against 9.285662f, so the
                // distinction is the whole of it.
                // **At or below the face, and this threshold is set by REAL corpses rather than by
                // the synthetic slope test** (B306). Four widths were tried; the synthetic test and
                // the real demo disagree about them, and the real demo wins:
                //
                //                                        slope test    corpse 2073 on cp_granary
                //   one slop above the face               12.207724f   —
                //   this step's closing travel             7.122617f   —
                //   penetrating by more than noise         6.621238f   −522, buried 106 deep
                //   at or below the plane  (this)          9.285662f   −377, resting correctly
                //
                // **The number the synthetic test likes buries a real body.** Requiring a body to be
                // measurably INSIDE a face before it is held means a corpse settling onto ground
                // loses its support the moment it stops penetrating, and it sinks until it does
                // again. The slope fixture cannot see that: it drops one cube from 150 units up and
                // scores the speed it still has after 400 steps, which rewards anything that damps
                // a tumble regardless of what it does to a body at rest.
                if (depth < 0f)
                {
                    continue;
                }

                any = true;

                into.Add(new IvpContact
                {
                    Body = body,
                    Arm = (at.X - centre.X, at.Y - centre.Y, at.Z - centre.Z),
                    Normal = (shape.Normal.X, shape.Normal.Y, shape.Normal.Z),
                    Depth = depth,
                    Point = -1,
                    Feature = IvpWorldCollision.TerrainFeature(index),
                });
            }

            if (any)
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>Clips a polygon to a triangle's three side planes — Sutherland-Hodgman.</summary>
    /// <remarks>
    /// **The side planes are perpendicular to the triangle's own**, so this keeps the part of the
    /// incident face that is actually over the triangle and discards what hangs past an edge.
    /// </remarks>
    private static int LedgeManifold(
        IvpRigidBody body,
        Vector3[] points,
        Vector3 centre,
        IvpWorldLedge ledge,
        int ledgeIndex,
        Vector3 direction,
        ICollection<IvpContact> into)
    {
        if (body.Faces.Count == 0 || ledge.Planes.Count == 0)
        {
            return 0;
        }

        // The face the body is resting ON: the one whose outward normal best matches the direction
        // between the closest features. See the call site for why this is not maximum separation.
        int reference = -1;
        float aligned = float.NegativeInfinity;

        for (int plane = 0; plane < ledge.Planes.Count; plane++)
        {
            float towards = Vector3.Dot(ledge.Planes[plane].Normal, direction);

            if (towards > aligned)
            {
                aligned = towards;
                reference = plane;
            }
        }

        if (reference < 0)
        {
            return 0;
        }

        (Vector3 referenceNormal, float referenceDistance) = ledge.Planes[reference];

        // The incident face: the body face turned most squarely against the reference.
        int incident = -1;
        float facing = 0f;

        for (int face = 0; face < body.Faces.Count; face++)
        {
            (int a, int b, int c) = body.Faces[face];

            if (a >= points.Length || b >= points.Length || c >= points.Length)
            {
                continue;
            }

            Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);

            if (normal.LengthSquared() <= FloatEpsilon)
            {
                continue;
            }

            float against = Vector3.Dot(Vector3.Normalize(normal), referenceNormal);

            if (incident < 0 || against < facing)
            {
                incident = face;
                facing = against;
            }
        }

        if (incident < 0)
        {
            return 0;
        }

        (int first, int second, int third) = body.Faces[incident];

        List<Vector3> polygon = [points[first], points[second], points[third]];

        // Sutherland-Hodgman against the ledge's SIDE planes — every plane but the reference, which
        // is the surface being measured against rather than a boundary of it. A ledge's planes face
        // outward, so a point is inside while `dot(N, p) - D <= 0`.
        for (int plane = 0; plane < ledge.Planes.Count && polygon.Count > 0; plane++)
        {
            if (plane == reference)
            {
                continue;
            }

            (Vector3 normal, float distance) = ledge.Planes[plane];

            List<Vector3> kept = [];

            for (int index = 0; index < polygon.Count; index++)
            {
                Vector3 current = polygon[index];
                Vector3 next = polygon[(index + 1) % polygon.Count];

                float here = Vector3.Dot(normal, current) - distance;
                float there = Vector3.Dot(normal, next) - distance;

                if (here <= 0f)
                {
                    kept.Add(current);
                }

                if ((here <= 0f) != (there <= 0f) && MathF.Abs(here - there) > FloatEpsilon)
                {
                    kept.Add(current + ((next - current) * (here / (here - there))));
                }
            }

            polygon = kept;
        }

        int raised = 0;

        foreach (Vector3 at in polygon)
        {
            // The same threshold the terrain manifold settled on: at or below the face, measured by
            // real corpses rather than by the synthetic slope fixture.
            float depth = referenceDistance - Vector3.Dot(referenceNormal, at);

            if (depth < 0f)
            {
                continue;
            }

            raised++;

            into.Add(new IvpContact
            {
                Body = body,
                Arm = (at.X - centre.X, at.Y - centre.Y, at.Z - centre.Z),
                Normal = (referenceNormal.X, referenceNormal.Y, referenceNormal.Z),
                Depth = depth,
                Point = -1,
                Feature = IvpWorldCollision.FeatureForLedge(ledgeIndex),
            });
        }

        return raised;
    }

    private static List<Vector3> Clip(List<Vector3> polygon, IvpWorldTriangle triangle)
    {
        Vector3[] corners = [triangle.A, triangle.B, triangle.C];

        for (int edge = 0; edge < 3 && polygon.Count > 0; edge++)
        {
            Vector3 from = corners[edge];
            Vector3 along = corners[(edge + 1) % 3] - from;

            Vector3 inward = Vector3.Cross(triangle.Normal, along);

            if (inward.LengthSquared() <= FloatEpsilon)
            {
                continue;
            }

            inward = Vector3.Normalize(inward);

            float offset = Vector3.Dot(inward, from);

            List<Vector3> kept = [];

            for (int index = 0; index < polygon.Count; index++)
            {
                Vector3 current = polygon[index];
                Vector3 next = polygon[(index + 1) % polygon.Count];

                float here = Vector3.Dot(inward, current) - offset;
                float there = Vector3.Dot(inward, next) - offset;

                if (here >= 0f)
                {
                    kept.Add(current);
                }

                if ((here >= 0f) != (there >= 0f) && MathF.Abs(here - there) > FloatEpsilon)
                {
                    kept.Add(current + ((next - current) * (here / (here - there))));
                }
            }

            polygon = kept;
        }

        return polygon;
    }

    /// <summary>How far behind a triangle this pass still looks, in Source units.</summary>
    private const float TerrainReach = 512f;


    /// <summary>The sphere query's reused buffer — this runs per body per slice.</summary>
    private static readonly List<(IvpWorldTriangle Shape, int Index)> _nearby = [];


    private void Push((float X, float Y, float Z) impulse, (float X, float Y, float Z) arm)
    {
        Body.Velocity = (
            Body.Velocity.X + (impulse.X * Body.InverseMass),
            Body.Velocity.Y + (impulse.Y * Body.InverseMass),
            Body.Velocity.Z + (impulse.Z * Body.InverseMass));

        (float X, float Y, float Z) twist = Cross(arm, impulse);

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

        // **One GJK manifold per (body, ledge) pair, replacing every per-point hit against that
        // ledge with a single contact — the divergence this loop's own comment below has named
        // since before this existed.** IVP holds one closest-feature pair per pair of objects; a
        // resting box previously raised eight contacts here, one per hull point, where the engine
        // raises one. `docs/findings/51` and `docs/memory/prove-the-equivalence-before-building-it`
        // both name measuring this rather than assuming it, so it is ADDITIVE: terrain and the
        // speculative/tunnel-prevention path below are untouched, and a ledge a manifold already
        // covers is skipped in the per-point walk purely by feature id, not by removing that walk.
        HashSet<int> covered = [];

        // **Terrain the engine's way: the hull against each nearby triangle** (B306). When this
        // produces contacts they REPLACE the per-vertex terrain sampling below, which is the
        // substitution being removed rather than a second opinion beside it.
        bool terrain = AgainstTerrain(body, world, centre, orientation, into, _nearby) > 0;

        if (body.Hull.Count > 0)
        {
            float bodyRadius = 0f;

            // The hull in world space, kept because the manifold below needs the actual points and
            // not just a support direction — computing it per ledge would repeat this per ledge.
            Vector3[] hullPoints = new Vector3[body.Hull.Count];

            for (int index = 0; index < body.Hull.Count; index++)
            {
                (float x, float y, float z) = body.Hull[index];
                float reach = MathF.Sqrt((x * x) + (y * y) + (z * z));

                bodyRadius = MathF.Max(bodyRadius, reach);

                hullPoints[index] = centre + Vector3.Transform(new Vector3(x, y, z), orientation);
            }

            Vector3 BodySupport(Vector3 direction)
            {
                Vector3 local = Vector3.Transform(direction, Quaternion.Inverse(orientation));

                Vector3 best = default;
                float bestDot = float.NegativeInfinity;

                for (int index = 0; index < body.Hull.Count; index++)
                {
                    (float x, float y, float z) = body.Hull[index];
                    Vector3 candidate = new(x, y, z);
                    float dot = Vector3.Dot(candidate, local);

                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = candidate;
                    }
                }

                return centre + Vector3.Transform(best, orientation);
            }

            for (int ledgeIndex = 0; ledgeIndex < world.Ledges.Count; ledgeIndex++)
            {
                IvpWorldLedge ledge = world.Ledges[ledgeIndex];

                if ((ledge.Contents & world.Mask) == 0)
                {
                    continue;
                }

                // Cheap sphere reject before the GJK call, mirroring the broadphase every other
                // world query already uses — without it this is a GJK call against every ledge in
                // the map, every body, every step.
                float apart = ledge.Radius + bodyRadius + committedLength + Slop;

                if ((ledge.Center - centre).LengthSquared() > apart * apart)
                {
                    continue;
                }

                if (Gjk.Distance(BodySupport, ledge.Support) is not { Distance: <= Slop } manifold)
                {
                    continue;
                }

                // **A zero normal is `Gjk`'s own honest signal that this pair is already
                // OVERLAPPING, not merely close — see its own remarks on why it stops short of
                // EPA.** A contact pushed along no direction moves nothing, so raising one here and
                // marking the ledge covered would suppress the per-point path's real, working
                // answer for exactly the case this pass cannot itself resolve. Measured: without
                // this guard a body already 0.9 units inside a floor fell straight through it,
                // because the one ledge it needed the per-point path for had been marked done by a
                // contact that pushed nowhere.
                if (manifold.Normal == Vector3.Zero)
                {
                    continue;
                }

                covered.Add(ledgeIndex);

                // **One contact per pair cannot hold a box flat** (B306): one contact has one arm,
                // and a body supported at a single point rotates about it. The engine's own answer
                // is a manifold, which `AgainstTerrain` already builds for triangles — this is that
                // for a ledge, and the ONLY thing it takes from the pair is which face to measure.
                //
                // **The reference face is chosen by alignment with the GJK normal, and that choice
                // is the whole fix.** An earlier version picked the plane the hull was furthest
                // OUTSIDE of, which is the standard convex test and is wrong here: a body straddling
                // the edge of a big floor brush is furthest outside a SIDE face, so the contact
                // normal shoved it sideways instead of holding it up, and `corpse-drop` went from
                // four seeds settling to three. `manifold.Normal` is the direction between the
                // closest features — IVP's mindist answer — so the face most aligned with it is the
                // face the body is actually resting on.
                int raised = LedgeManifold(
                    body, hullPoints, centre, ledge, ledgeIndex, manifold.Normal, into);

                // **The speculative contact stays when the manifold raises nothing**, which is the
                // separated case: every clipped vertex is still outside the face, so there is no
                // depth to resolve and only a closing velocity to cancel. Replacing it wholesale
                // would delete the pre-contact that stops a fast body tunnelling.
                if (raised == 0)
                {
                    into.Add(new IvpContact
                    {
                        Body = body,
                        Arm = (
                            manifold.PointOnA.X - centre.X,
                            manifold.PointOnA.Y - centre.Y,
                            manifold.PointOnA.Z - centre.Z),
                        Normal = (manifold.Normal.X, manifold.Normal.Y, manifold.Normal.Z),
                        Depth = MathF.Max(0f, Slop - manifold.Distance),
                        Point = -1,
                        Feature = IvpWorldCollision.FeatureForLedge(ledgeIndex),
                    });
                }
            }
        }

        // **A flag and three fields rather than a nullable tuple**, per
        // `docs/memory/nullable-pattern-on-a-struct-is-dead-code.md` — CA1508 rejects the nullable
        // form here outright, reporting the null test as always true.

        for (int index = 0; index < body.Hull.Count; index++)
        {
            (float x, float y, float z) = body.Hull[index];

            Vector3 arm = Vector3.Transform(new Vector3(x, y, z), orientation);

            // **The world half is RE-DERIVED here every step, and that is the divergence.** IVP
            // keeps a mindist — the pair of features that were closest — and re-measures that pair;
            // `Touching` instead returns whichever face the point is least far behind now, and
            // which face that is changes as a body settles.
            //
            // **Retaining the face and re-measuring against it was built and measured WORSE**:
            // corpses settling went from four of five to three, and the free lift did not move.
            // The reason is in `docs/findings/51` — a retained face is STICKY where a mindist is
            // *track and update*, so on a seventeen-body ragdoll it measures a surface the body has
            // already left. `IvpWorldCollision.Against` is what a correct version would use and is
            // kept for it.
            (Vector3 Normal, float Depth, int Feature)? hit =
                world.Touching(centre + arm, default);

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
                        // **A speculative contact gets no retained identity**, because it is not
                        // against a face yet — the sweep says which surface it WILL meet, and a
                        // contact that has not happened has nothing to accumulate. It is filed
                        // under `Speculative` so it never inherits a resting contact's stored
                        // impulse.
                        hit = (soon.Normal, 0f, Speculative);
                    }
                }
            }

            if (hit is not { } found)
            {
                continue;
            }

            // **A ledge already covered by the GJK manifold above is skipped here, not tested
            // twice.** This is what makes the manifold pass additive rather than doubled: a body
            // resting on a ledge gets the one contact the manifold raised for it and nothing more,
            // while terrain and every ledge the sphere reject above skipped still go through the
            // per-point path exactly as before.
            if (covered.Count > 0 && covered.Contains(IvpWorldCollision.LedgeOf(found.Feature)))
            {
                continue;
            }

            // **A terrain hit the hull-vs-triangle pass already answered is not sampled again**
            // (B306). Speculative contacts are exempt: those are a prediction about a surface not
            // yet reached, which that pass does not make.
            if (terrain && found.Feature <= -2)
            {
                continue;
            }

            // **Every touching point raises a contact, and ONE PER BODY was tried instead.** IVP
            // holds a single mindist per pair of objects, so a cube on a floor is one closest
            // feature where this is eight — a real divergence, and reproducing the count alone
            // measured worse: penetration went from 7 to 27 and corpses began leaving the world
            // with zero contacts. Our hull is a point cloud where IVP's features are faces and
            // edges, so one vertex cannot hold a resting box the way one face-face pair does.
            // **The manifold pass above closes this for BRUSH ledges** — terrain and the
            // speculative/tunnel-prevention path below are what remain per-point.
            into.Add(new IvpContact
            {
                Body = body,
                Arm = (arm.X, arm.Y, arm.Z),
                Normal = (found.Normal.X, found.Normal.Y, found.Normal.Z),
                Depth = found.Depth,
                Point = index,
                Feature = found.Feature,
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

    /// <summary>The engine's own bound on one contact's loop — <c>iVar15 &lt; 100</c>.</summary>
    /// <remarks>
    /// **A bound, not a count.** `for (; (0.0 &lt; dVar24 &amp;&amp; (iVar15 &lt; 100)); …)` ends on the
    /// condition; a contact that is already resting exits on the first test, and one taking a real
    /// impact needs about six, since each pass removes a fifth. Reaching a hundred means the engine
    /// gave up, and it checks for exactly that afterwards — `if ((0.0 &lt; dVar18) &amp;&amp;
    /// (iVar15 != 100))` skips the final calibrated impulse when the loop ran out.
    /// </remarks>
    private const int MaximumPasses = 100;


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
    /// **Not the engine's, and it exists because this project has no mindist scheduler.** IVP
    /// re-checks a pair before the bodies reach each other and never sees a six-unit overlap at
    /// all; a discrete test at 66 Hz does, on the first frame of every corpse that falls any
    /// distance. Sixty a second is about a unit per step — enough to clear a real overlap within a
    /// few frames and far too slow to look like a bounce.
    ///
    /// **The engine's own separation speed is `0.01` metres per second and was TRIED here**
    /// (`DAT_1800eb150`, grown by `sqrt(impacts)` in `FUN_18008e290`). It measured a body sinking
    /// to −40 through a floor it should rest 3 above. That number is about bounce, in a solver that
    /// never carries a penetration to remove, so it does not transfer to a compensator for the
    /// scheduler we lack. Recorded because the substitution looks obviously right and is not.
    /// </remarks>
    private const float MaximumRecovery = 60f;
}
