using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One angular degree of freedom of a ragdoll joint (B58, D142, D146).
/// </summary>
/// <remarks>
/// **All three of a joint's constrained axes are ANGULAR**, which took a correction to establish:
/// the routine that looked like a point-to-point anchor solve builds the same rotational effective
/// mass as the other two, with no lever arm and no linear term. Position is corrected elsewhere, by
/// a separate one-shot block. See `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md`.
///
/// **The bounds are compared without a branch.** Nothing in the engine asks "is the angle outside
/// its range" — `min(0, θ − lower) + max(0, θ − upper)` is zero inside and is the signed overshoot
/// outside, and it is folded straight into the impulse.
/// </remarks>
public sealed class IvpJointAxis
{
    /// <summary>The axis this limit turns about, in the joint's own frame.</summary>
    public (float X, float Y, float Z) Direction { get; set; } = (1f, 0f, 0f);

    /// <summary>The lower bound in radians — flag block <c>+0x4</c>.</summary>
    public float Lower { get; set; }

    /// <summary>The upper bound in radians — flag block <c>+0x8</c>.</summary>
    public float Upper { get; set; }

    /// <summary>The per-axis scale the overshoot is measured in — flag block <c>+0xc</c>.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>The measure of this joint's current deflection, from <c>geom+0x100..0x108</c>.</summary>
    /// <remarks>
    /// **The solve READS this and, for a ragdoll, never writes it.** The only write is the unwrap
    /// inside the spring branch, which is dead here — so whoever measures the joint each tick owns
    /// the value, and it arrives already continuous.
    ///
    /// **Only ONE of a joint's three axes is measured as an angle.** `FUN_180038620` fills the four
    /// lanes at `geom+0x100` before dispatching, and a dumped lane mask picks a different quantity
    /// per lane: lane 0 gets `0 − atan2(…)` — a true angle, computed to about seven digits by
    /// `FUN_180036b80` — while lanes 1 and 2 get two different DOT PRODUCTS. So the two swing limits
    /// are compared against projections rather than radians.
    ///
    /// **Which is why this is named for the measure and not for an angle.** The bound and the
    /// deflection have to be in the same units, and how the degree bounds in
    /// `constraint_ragdollparams_t::axes[]` are converted for the two projection axes is NOT
    /// established — that is the next thing to read, and guessing it would give two joints that
    /// clamp at the wrong place with nothing to say so.
    /// </remarks>
    public float Angle { get; set; }

    /// <summary>Whether the clamp applies at all — bit <c>0</c> of the flag block.</summary>
    /// <remarks>
    /// **An axis free through a full turn is DISABLED, not clamped against unreachable bounds.**
    /// `FUN_180037890` clears the byte when the range covers 2π:
    ///
    /// <code>
    /// if (fVar4 &lt;= fVar1 - fVar5) { *(undefined1 *)(param_1 + 0xb0) = 0; }   // 6.2831855
    /// </code>
    ///
    /// and the byte is not tested — it indexes a pair of dumped 16-byte tuples, `1800ee970` being
    /// all zeroes and `1800ee980` all ones, which is anded against the overshoot. Off means the
    /// correction is masked away rather than skipped, which is the same answer by a cheaper route.
    /// </remarks>
    public bool Limited { get; set; } = true;
}

/// <summary>
/// A constraint axis's cached geometry — <c>FUN_180037bd0</c> (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Built once per constraint per tick and reused by every relaxation sweep**, which is the whole
/// reason the engine splits the constraint vtable into an expensive slot 3 and a cheap slot 4. The
/// two sweeps a TF2 ragdoll runs share one of these.
///
/// **The immovable gate lives HERE rather than in the solve.** A body failing
/// <c>(*coreFlags &amp; 0x12) == 0</c> gets zeroed rows, so the unconditional accumulate later nets
/// to nothing for it — a tidier arrangement than a branch in the inner loop, and one worth copying
/// rather than improving.
/// </remarks>
public readonly struct IvpJacobian : IEquatable<IvpJacobian>
{
    /// <summary>Body A's row — <c>cache[8..0xb]</c>, the axis scaled by its inverse inertia.</summary>
    public (float X, float Y, float Z) ResponseA { get; private init; }

    /// <summary>Body B's row — <c>cache[0xc..0xf]</c>, built from the NEGATED axis.</summary>
    /// <remarks>
    /// **This is where equal-and-opposite comes from.** Both bodies are handed the same impulse with
    /// a `+`; the sign lives in the row, because `FUN_180037bd0` rotates `−anchor` for body B.
    /// </remarks>
    public (float X, float Y, float Z) ResponseB { get; private init; }

    /// <summary>Body A's axis in its own frame, for the rate.</summary>
    public (float X, float Y, float Z) AxisA { get; private init; }

    /// <summary>Body B's axis, negated.</summary>
    public (float X, float Y, float Z) AxisB { get; private init; }

    /// <summary>One over the effective mass — <c>cache[0x14]</c>, or zero when degenerate.</summary>
    public float InverseEffectiveMass { get; private init; }

    /// <summary>Builds the cached rows for one axis.</summary>
    /// <param name="a">The first body.</param>
    /// <param name="b">The second.</param>
    /// <param name="axis">The constraint axis, in world space.</param>
    /// <returns>The cache the sweeps read.</returns>
    /// <exception cref="ArgumentNullException">Either body is null.</exception>
    /// <remarks>
    /// **The effective mass has no lever arm**, which is the reading that established all three axes
    /// are rotational: it is `Σ r·(invI ⊙ r)` with a plain rotated vector and no cross product. A
    /// point constraint would need `1/mA + 1/mB + (r × n)·I⁻¹·(r × n)`, and none of those terms
    /// appear.
    ///
    /// **The inverse inertia is a DIAGONAL, not a tensor.** IVP keeps each body in its own principal
    /// frame, so the axis is rotated into that frame rather than the tensor into the world.
    ///
    /// **A degenerate joint yields zero rather than infinity.** The engine reciprocates with
    /// `rcpps` plus one Newton-Raphson step and then selects `0.0` when `K` is not above
    /// `FLT_EPSILON`, so every impulse for that axis multiplies out to nothing — a behaviour, not a
    /// guard, and a corpse whose joint goes singular simply stops being corrected there.
    ///
    /// **The reciprocal is a plain divide here, and that is a stated departure rather than an
    /// oversight.** `rcpps`'s result is not architecturally specified — the seed is about 12 bits
    /// and its exact value differs between CPU vendors — so the engine does not agree with itself
    /// across machines and bit-parity is unattainable on principle. One Newton step from a 12-bit
    /// seed reaches float precision, which is where a correctly-rounded divide already is.
    /// </remarks>
    public static IvpJacobian Build(IvpRigidBody a, IvpRigidBody b, (float X, float Y, float Z) axis)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        (float X, float Y, float Z) axisA = a.Immovable ? default : IvpQuaternion.Rotate(a.Orientation, axis);

        (float X, float Y, float Z) axisB = b.Immovable
            ? default
            : IvpQuaternion.Rotate(b.Orientation, (-axis.X, -axis.Y, -axis.Z));

        (float X, float Y, float Z) responseA = (
            axisA.X * a.InverseInertia.X, axisA.Y * a.InverseInertia.Y, axisA.Z * a.InverseInertia.Z);

        (float X, float Y, float Z) responseB = (
            axisB.X * b.InverseInertia.X, axisB.Y * b.InverseInertia.Y, axisB.Z * b.InverseInertia.Z);

        float mass =
            (axisA.X * responseA.X) + (axisA.Y * responseA.Y) + (axisA.Z * responseA.Z) +
            (axisB.X * responseB.X) + (axisB.Y * responseB.Y) + (axisB.Z * responseB.Z);

        return new IvpJacobian
        {
            AxisA = axisA,
            AxisB = axisB,
            ResponseA = responseA,
            ResponseB = responseB,
            InverseEffectiveMass = mass > FloatEpsilon ? 1f / mass : 0f,
        };
    }

    /// <summary>The rate the joint is turning at — <c>ωA · Ja + ωB · Jb</c>.</summary>
    /// <param name="a">The first body.</param>
    /// <param name="b">The second.</param>
    /// <returns>Radians per second along the constraint axis.</returns>
    /// <exception cref="ArgumentNullException">Either body is null.</exception>
    /// <remarks>
    /// **Recomputed every sweep even though the rows are cached**, and that is the point of the
    /// relaxation: the other axes have changed these velocities since the row was built, so a sweep
    /// that reused a cached rate would be solving all three axes against the same stale state.
    /// </remarks>
    public float Rate(IvpRigidBody a, IvpRigidBody b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return
            (a.AngularVelocity.X * AxisA.X) + (a.AngularVelocity.Y * AxisA.Y) + (a.AngularVelocity.Z * AxisA.Z) +
            (b.AngularVelocity.X * AxisB.X) + (b.AngularVelocity.Y * AxisB.Y) + (b.AngularVelocity.Z * AxisB.Z);
    }

    /// <summary>Whether another cache holds the same rows.</summary>
    /// <param name="other">The cache to compare against.</param>
    /// <returns><c>true</c> when every row and the multiplier match bit for bit.</returns>
    /// <remarks>
    /// **Bitwise, deliberately, and it exists because CA1815 demands it rather than because
    /// anything compares two of these.** A tolerance would be wrong here: two caches built from
    /// different states are different caches, and there is no sense in which nearly-equal rows are
    /// the same row.
    /// </remarks>
    public bool Equals(IvpJacobian other) =>
        Same(AxisA, other.AxisA)
        && Same(AxisB, other.AxisB)
        && Same(ResponseA, other.ResponseA)
        && Same(ResponseB, other.ResponseB)
        && Same(InverseEffectiveMass, other.InverseEffectiveMass);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IvpJacobian other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(AxisA, AxisB, ResponseA, ResponseB, InverseEffectiveMass);

    /// <summary>Whether two caches hold the same rows.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns><c>true</c> when they match.</returns>
    public static bool operator ==(IvpJacobian left, IvpJacobian right) => left.Equals(right);

    /// <summary>Whether two caches differ.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns><c>true</c> when they do not match.</returns>
    public static bool operator !=(IvpJacobian left, IvpJacobian right) => !left.Equals(right);

    /// <summary>Component-wise equality, compared as bits rather than as numbers.</summary>
    private static bool Same((float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        Same(left.X, right.X) && Same(left.Y, right.Y) && Same(left.Z, right.Z);

    /// <summary>Whether two floats are the same value, bit for bit.</summary>
    /// <remarks>
    /// **Compared as integers on purpose.** The question here is whether two caches are the SAME
    /// cache, which is an identity question and not a numeric one — a tolerance would answer a
    /// question nobody asked, and the analyzer is right that `==` on floats usually does.
    /// </remarks>
    private static bool Same(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    /// <summary><c>FLT_EPSILON</c> — 2⁻²³, the cutoff a degenerate axis falls below.</summary>
    private const float FloatEpsilon = 1.1920929e-07f;
}

/// <summary>
/// The angular limit solve — <c>FUN_180036f80</c> and <c>FUN_1800372c0</c> (B58, D142, D146).
/// </summary>
/// <remarks>
/// **For a TF2 ragdoll this is the WHOLE constraint solve**, and it is six lines. Both decompiled
/// routines are dominated by a spring/friction branch gated on a byte `FUN_18000eac0` sets from
/// `(virtualCall × torque) != 0`, and `SetAxisFriction( rmin, rmax, friction )` puts `friction`
/// straight into `torque` — so what decides it is the model's own `.phy`.
///
/// **Measured over every `.phy` the game ships**, 4,755 files and 37 of them jointed: `0 of 1734`
/// joint axes declare a nonzero friction, all nine player classes included. So for a corpse there
/// is no spring, no friction, no warm start and no impulse carried between sweeps — only the limit.
/// The census is `ragdoll-constraints`, so a content update that changed it would be caught by
/// re-running one probe rather than by watching a corpse behave oddly.
///
/// **This is dead for TF2's content, not dead code.** Valve's own
/// `physics_prop_ragdoll.cpp:1525` ships `SetAxisFriction( -2, 2, 20 )`, so a ragdoll from another
/// game would need the branch transcribed before it simulated correctly.
///
/// **The two engine routines have opposite sign conventions and they cancel.** One builds
/// `θ = rate·gain + angle` and subtracts the overshoot; the other builds `θ = angle − rate·gain` and
/// adds it. Same physics, measured in opposite directions. Copying one routine's signs onto the
/// other's angle would give a joint that drives itself further out of its limit the harder it is
/// pushed in, so only one convention is transcribed and the caller supplies the axis direction that
/// makes it right.
///
/// **Two sweeps, forwards then backwards, at 0.4 each.** The group runs
/// `additionalIterations + 2` iterations and `ragdoll_shared.cpp:274-276` leaves that at zero, so a
/// corpse gets exactly two; each walks the constraint list descending and then ascending, which is
/// what stops a chain of joints biasing toward whichever end is solved first.
/// </remarks>
public static class IvpAngularLimit
{
    /// <summary>The relaxation weight both of a ragdoll's sweeps carry.</summary>
    /// <remarks>
    /// **Dumped from the table at `0x1800eeb70`** — `0.4, 0.4, 0.4, 0.4, 1.0, 1.0, 0.8, 0.6, …` —
    /// indexed by iteration, and at the stock two iterations both entries are `0.4`.
    ///
    /// **It is NOT the `0.8` that also appears in this solve.** That one is an error-reduction term
    /// inside the dead spring branch, from a different table at `0x1800ee9b0`. Reading either as the
    /// other would be easy and wrong, so both are named where they are used.
    ///
    /// **It reaches <see cref="Solve"/> inside the per-sweep gain vector rather than on its own.**
    /// The driver hands each constraint a four-lane vector built from the weight; the composition
    /// is not read, so this constant is carried for callers to fold in rather than applied here.
    /// </remarks>
    public const float StockPassWeight = 0.4f;

    /// <summary>Solves one axis for one sweep.</summary>
    /// <param name="a">The first body.</param>
    /// <param name="b">The second.</param>
    /// <param name="axis">The limit.</param>
    /// <param name="jacobian">The cached rows, from <see cref="IvpJacobian.Build"/>.</param>
    /// <param name="rateGain">
    /// How far ahead the angle is predicted — <c>param_1[0]</c>, from the constraint descriptor at
    /// <c>+0x2d0</c> scaled by its own fourth lane. Its provenance is NOT established; the shape is
    /// a timestep, which is an inference.
    /// </param>
    /// <param name="impulseGain">
    /// <c>param_1[1]</c>, the second lane of the same per-sweep vector. **The relaxation weight
    /// reaches the solve through this vector**, which the group driver builds before calling the
    /// constraint's cheap slot; exactly how it composes the weight with the timestep is NOT
    /// established, so the two lanes are passed in rather than derived here.
    /// </param>
    /// <param name="damping">
    /// The constraint descriptor's first lane — <c>constraint+0x2d0</c>, which
    /// <c>SetAxisFriction</c>'s <c>torque</c> feeds. The twist axis receives it scaled by the
    /// descriptor's own fourth lane and the two swing axes receive it unscaled.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// **The angle solved against is PREDICTED, not current.** Without the rate term a joint only
    /// ever resists a limit it has already broken, and that is visible: a limb overshoots and is
    /// dragged back rather than stopping.
    ///
    /// **The clamp is arithmetic, not a branch**, which is why a search for an "is it outside" test
    /// found nothing:
    ///
    /// <code>
    /// auVar19._0_4_ = (fVar34 - fVar16) * fVar23;   // predicted - lower
    /// auVar14._0_4_ = (fVar34 - fVar2 ) * fVar23;   // predicted - upper
    /// auVar15 = minps(auVar19, zero);
    /// auVar18 = maxps(auVar14, zero);
    /// fVar29 = fVar29 - ((auVar18._0_4_ + auVar15._0_4_) &amp; param_2[0x18]);
    /// </code>
    ///
    /// **All four factors multiply the SCALE the overshoot is measured in**, on the live path
    /// outside the dead branch:
    ///
    /// <code>
    /// fVar23 = fVar24 * *param_8 * fVar23 * param_2[0x14];
    /// //       └ +0xc ┘ └ +0x2d0 ┘ └ p1[1] ┘ └ 1/K ┘
    /// </code>
    ///
    /// **An earlier reading put this inside the dead branch**, which would have made a corpse snap
    /// to its limits in one step rather than relax into them over two sweeps.
    ///
    /// **Both bodies are accumulated with a `+`.** The opposition comes out of
    /// <see cref="IvpJacobian.ResponseB"/>, which is built from the negated axis.
    /// </remarks>
    public static void Solve(
        IvpRigidBody a,
        IvpRigidBody b,
        IvpJointAxis axis,
        in IvpJacobian jacobian,
        float rateGain,
        float impulseGain,
        float damping)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(axis);

        float predicted = (jacobian.Rate(a, b) * rateGain) + axis.Angle;

        float scale = axis.Scale * damping * impulseGain * jacobian.InverseEffectiveMass;

        float overshoot =
            Math.Min(0f, (predicted - axis.Lower) * scale) +
            Math.Max(0f, (predicted - axis.Upper) * scale);

        // The enable is a mask against a dumped all-zero or all-ones tuple rather than a test, so an
        // unlimited axis reaches this line and multiplies out to nothing.
        float impulse = axis.Limited ? -overshoot : 0f;

        a.AngularVelocity = (
            a.AngularVelocity.X + (impulse * jacobian.ResponseA.X),
            a.AngularVelocity.Y + (impulse * jacobian.ResponseA.Y),
            a.AngularVelocity.Z + (impulse * jacobian.ResponseA.Z));

        b.AngularVelocity = (
            b.AngularVelocity.X + (impulse * jacobian.ResponseB.X),
            b.AngularVelocity.Y + (impulse * jacobian.ResponseB.Y),
            b.AngularVelocity.Z + (impulse * jacobian.ResponseB.Z));
    }
}
