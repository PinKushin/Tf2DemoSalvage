using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// What a ragdoll joint's angular limit does to the two bodies it joins (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Transcribed from `FUN_180036f80` and `FUN_1800372c0`, both decompiled in full** — see
/// `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md`. For a TF2 ragdoll the spring/friction
/// branch inside both is dead (its gate byte is zeroed at construction and never written), so what
/// survives is six lines: predict the angle, measure the overshoot past the bounds, mask it by the
/// per-axis enable, divide by the effective mass, and add the impulse to both bodies' angular
/// velocity.
///
/// **The predictions below are hand-computed from that arithmetic**, not read back from the
/// implementation. Every body here is given unit inertia and a unit axis so the numbers can be
/// written out — an effective mass that has to be trusted rather than derived would make each
/// assertion a description of whatever the code does.
/// </remarks>
public sealed class IvpAngularLimitConformanceTests
{
    private const float Close = 1e-5f;

    /// <summary>The routine these tests exercise — <c>FUN_180036f80</c>, the twist.</summary>
    /// <remarks>
    /// **Named at every call rather than defaulted.** The engine's other routine adds the overshoot
    /// where this one subtracts it, and with the rate gain at zero that is the ONLY difference — so
    /// a default would silently drive two of a joint's three axes the wrong way.
    /// </remarks>
    private const IvpAngularLimit.Routine Bisector = IvpAngularLimit.Routine.Bisector;

    /// <summary>A joint whose axis is X, limited to a tenth of a radian either side.</summary>
    private static IvpJointAxis Axis() => new()
    {
        Direction = (1f, 0f, 0f),
        Lower = -0.1f,
        Upper = 0.1f,
    };

    /// <summary>Two bodies with unit inertia, so the effective mass is exactly two.</summary>
    private static (IvpRigidBody A, IvpRigidBody B) Pair() => (new IvpRigidBody(), new IvpRigidBody());

    /// <remarks>
    /// **The clamp is `min(0, θ − lower) + max(0, θ − upper)`, which is zero inside the range.**
    /// There is no "is it outside" test anywhere in either routine — the comparison is arithmetic,
    /// which is why a search for a branch found nothing.
    /// </remarks>
    [Test]
    public void Solve_WithTheAngleInsideItsBounds_ChangesNothing()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        a.AngularVelocity = (0.5f, 0f, 0f);

        IvpJointAxis axis = Axis();
        axis.Angle = 0.02f;

        IvpAngularLimit.Solve(a, b, axis, IvpJacobian.Build(a, b, axis.Direction), 0f, 1f, 1f, Bisector);

        a.AngularVelocity.X.ShouldBe(0.5f, Close, "the angle is inside, so no correction");
        b.AngularVelocity.X.ShouldBe(0f, Close);
    }

    /// <remarks>
    /// **The whole prediction, written out.** With unit inertia on both bodies and the axis along X,
    /// each body's row is `(1,0,0)`, so `K = 1 + 1 = 2` and `1/K = 0.5`. The rate gain is zero here
    /// so the predicted angle is the current one, `0.3`, which is `0.2` past the upper bound. The
    /// scale is `axisScale × passWeight × gain × (1/K)` = `1 × 1 × 1 × 0.5` = `0.5`, so the
    /// overshoot is `0.1` and the impulse is `−0.1`. Each body's response row is `r ⊙ invInertia` =
    /// `(1,0,0)` for A and `(−1,0,0)` for B.
    /// </remarks>
    [Test]
    public void Solve_WithTheAngleAboveItsUpperBound_PushesBothBodiesBack()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        IvpJointAxis axis = Axis();
        axis.Angle = 0.3f;

        IvpAngularLimit.Solve(a, b, axis, IvpJacobian.Build(a, b, axis.Direction), 0f, 1f, 1f, Bisector);

        a.AngularVelocity.X.ShouldBe(-0.1f, Close);
        b.AngularVelocity.X.ShouldBe(0.1f, Close, "equal and opposite, from the negated row");
    }

    /// <remarks>
    /// **Below the lower bound the sign flips**, because `min(0, θ − lower)` is negative there where
    /// `max(0, θ − upper)` was positive above. The magnitude is the mirror of the test above.
    /// </remarks>
    [Test]
    public void Solve_WithTheAngleBelowItsLowerBound_PushesTheOtherWay()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        IvpJointAxis axis = Axis();
        axis.Angle = -0.3f;

        IvpAngularLimit.Solve(a, b, axis, IvpJacobian.Build(a, b, axis.Direction), 0f, 1f, 1f, Bisector);

        a.AngularVelocity.X.ShouldBe(0.1f, Close);
        b.AngularVelocity.X.ShouldBe(-0.1f, Close);
    }

    /// <remarks>
    /// **An axis free through a full turn has its clamp DELETED, not widened.** `FUN_180037890`
    /// clears the enable byte when `upper − lower >= 2π`, and the byte selects between two dumped
    /// 16-byte tuples — `1800ee970` is all zeroes and `1800ee980` is all ones — which is anded
    /// against the overshoot. Zero tuple, no correction, whatever the bounds say.
    ///
    /// **The angle here is outside the bounds on purpose.** With it inside, an enabled axis and a
    /// disabled one predict the same observation and the test would pass against a solver that
    /// ignored the flag entirely.
    /// </remarks>
    [Test]
    public void Solve_WithAnUnlimitedAxis_LeavesBothBodiesAloneEvenWellOutside()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        IvpJointAxis axis = Axis();
        axis.Angle = 3f;
        axis.Limited = false;

        IvpAngularLimit.Solve(a, b, axis, IvpJacobian.Build(a, b, axis.Direction), 0f, 1f, 1f, Bisector);

        a.AngularVelocity.X.ShouldBe(0f, Close);
        b.AngularVelocity.X.ShouldBe(0f, Close);
    }

    /// <remarks>
    /// **The relaxation weight is live, and this is the assertion that says so.** It was reported
    /// once as reaching the solve only inside the dead branch, which was wrong: `fVar23 = fVar24 *
    /// *param_8 * fVar23 * param_2[0x14]` sits after the branch closes, so every one of those four
    /// factors multiplies the scale the overshoot is measured in, every sweep. Reported dead, a
    /// corpse would snap to its limits in one step instead of relaxing into them over two.
    ///
    /// **The weight rides in the per-sweep gain vector** — `param_1[1]`, the `impulseGain` argument
    /// — rather than arriving on its own, so it is passed there. Same setup as the upper-bound test
    /// with `0.4` instead of `1`, giving 40% of that impulse.
    /// </remarks>
    [Test]
    public void Solve_WithTheStockRelaxationWeight_AppliesFourTenthsOfTheCorrection()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        IvpJointAxis axis = Axis();
        axis.Angle = 0.3f;

        IvpAngularLimit.Solve(
            a, b, axis, IvpJacobian.Build(a, b, axis.Direction),
            0f, IvpAngularLimit.StockPassWeight, 1f, Bisector);

        a.AngularVelocity.X.ShouldBe(-0.04f, Close);
        b.AngularVelocity.X.ShouldBe(0.04f, Close);
    }

    /// <remarks>
    /// **The rate gain is what makes this predictive rather than reactive** — the angle solved
    /// against is `angle + rate × gain`, where the rate is `ωA·Ja + ωB·Jb` off `core+0x130` for both
    /// bodies. Here the joint sits at `0.05`, inside its bounds, and is turning at `1` radian a
    /// second; with a gain of `0.5` the predicted angle is `0.55`, which is `0.45` past the upper
    /// bound.
    ///
    /// **Without it a joint only ever resists a limit it has already broken**, which is visible: a
    /// limb overshoots and is dragged back rather than stopping.
    /// </remarks>
    [Test]
    public void Solve_WithARateGain_ClampsThePredictedAngleRatherThanTheCurrentOne()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        a.AngularVelocity = (1f, 0f, 0f);

        IvpJointAxis axis = Axis();
        axis.Angle = 0.05f;

        IvpAngularLimit.Solve(
            a, b, axis, IvpJacobian.Build(a, b, axis.Direction), 0.5f, 1f, 1f, Bisector);

        // rate 1 × gain 0.5 + 0.05 = 0.55; overshoot (0.55 − 0.1) × 0.5 = 0.225.
        a.AngularVelocity.X.ShouldBe(1f - 0.225f, Close);
        b.AngularVelocity.X.ShouldBe(0.225f, Close);
    }

    /// <remarks>
    /// **A body carrying the immovable bit contributes no inverse inertia and receives no impulse**,
    /// and both halves come from the same guard: `FUN_180037bd0` includes a body only when
    /// `(*coreFlags &amp; 0x12) == 0`, and a skipped body's cache rows are left zero, so the
    /// unconditional accumulate later nets to nothing for it.
    ///
    /// With B static the effective mass is `1`, not `2`, so the same overshoot produces twice the
    /// correction on A — which is the behaviour, not a rounding difference: a limb hitting a limit
    /// against the world stops harder than one hitting a limit against another limb.
    /// </remarks>
    [Test]
    public void Solve_AgainstAnImmovableBody_MovesOnlyTheOtherAndTwiceAsHard()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        b.Immovable = true;

        IvpJointAxis axis = Axis();
        axis.Angle = 0.3f;

        IvpAngularLimit.Solve(a, b, axis, IvpJacobian.Build(a, b, axis.Direction), 0f, 1f, 1f, Bisector);

        a.AngularVelocity.X.ShouldBe(-0.2f, Close, "1/K is 1 rather than 0.5");
        b.AngularVelocity.ShouldBe((0f, 0f, 0f), "and the static body is never pushed");
    }

    /// <remarks>
    /// **A degenerate joint yields a zero multiplier rather than an infinity.** The engine takes the
    /// reciprocal of the effective mass with `rcpps` plus one Newton-Raphson refinement and then
    /// selects `0.0` when `K` is not above `FLT_EPSILON` — so every impulse for that axis multiplies
    /// out to nothing.
    ///
    /// **That is a behaviour and not a guard**, which is why it is asserted here: written as a guard
    /// — an early return, or a division left to produce infinity — the corpse either keeps a stale
    /// correction or is flung apart on the frame a joint goes singular.
    /// </remarks>
    [Test]
    public void Build_WithNoInverseInertiaAtAll_YieldsAZeroMultiplierRatherThanInfinity()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        a.InverseInertia = (0f, 0f, 0f);
        b.InverseInertia = (0f, 0f, 0f);

        IvpJacobian jacobian = IvpJacobian.Build(a, b, (1f, 0f, 0f));

        jacobian.InverseEffectiveMass.ShouldBe(0f);

        IvpJointAxis axis = Axis();
        axis.Angle = 3f;

        IvpAngularLimit.Solve(a, b, axis, jacobian, 0f, 1f, 1f, Bisector);

        a.AngularVelocity.ShouldBe((0f, 0f, 0f));
        b.AngularVelocity.ShouldBe((0f, 0f, 0f));
    }

    /// <remarks>
    /// **The cutoff is a strict `&gt;` against `FLT_EPSILON`, and it is worth a boundary test**
    /// because the two sides of it differ by everything: just above, the multiplier is eight million
    /// and the joint is enormously stiff; at it, the multiplier is zero and the joint is not solved
    /// at all. A `&gt;=` would put the singular case on the stiff side.
    ///
    /// **`K` is set exactly by construction** — one body immovable, the other's inverse inertia the
    /// value wanted — so the input sits on the boundary rather than near it.
    /// </remarks>
    [Test]
    public void Build_WithAnEffectiveMassAtTheCutoff_IsZeroAndJustAboveItIsNot()
    {
        (IvpRigidBody a, IvpRigidBody b) = Pair();

        b.Immovable = true;

        // `FLT_EPSILON` itself — 2^-23, not `float.Epsilon`, which is the smallest subnormal.
        a.InverseInertia = (1.1920929e-07f, 0f, 0f);

        IvpJacobian.Build(a, b, (1f, 0f, 0f)).InverseEffectiveMass
            .ShouldBe(0f, "exactly at the cutoff is not above it");

        a.InverseInertia = (1.1920930e-07f, 0f, 0f);

        IvpJacobian.Build(a, b, (1f, 0f, 0f)).InverseEffectiveMass
            .ShouldBeGreaterThan(8_000_000f, "and one ulp above it the joint is solved");
    }
}
