using System;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's per-core integration step, transcribed from `vphysics.dll` (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Predicted from the decompiled bodies of `FUN_180099a00`, `FUN_180099fc0`, `FUN_180071680` and
/// `FUN_180070d60`, before any of this existed.** The solver is closed — `src/vphysics` ships no
/// source — so these are read out of the binary rather than out of the SDK, and every constant here
/// was DUMPED rather than inferred from what the arithmetic looked like.
///
/// **What makes each of these worth a test is that the plausible version is wrong in a way nothing
/// crashes on.** A corpse integrated with the obvious Euler step, a true sine, one clock, or a
/// sequential angular update settles somewhere else — silently, and only against TF2 side by side
/// would anyone notice.
/// </remarks>
public sealed class IvpIntegratorConformanceTests
{
    /// <summary>Tolerance for a value predicted through single-precision trigonometry.</summary>
    private const double Close = 1e-5;

    /// <remarks>
    /// **The delta rotation is a per-axis THIRD-ORDER TAYLOR sine, not a real one**
    /// (`FUN_180071680`):
    ///
    /// <code>
    /// dVar2 = param_3 * DAT_1800ee388;                              // dt/2,  DAT_1800ee388 = 0.5
    /// fVar4 = (float)((double)*param_2 * dVar2);                    // theta = w * dt/2
    /// dVar6 = (double)(fVar4 - fVar4*fVar4*fVar4*DAT_1800eb148);    // DAT_1800eb148 = 0.16666667f
    /// </code>
    ///
    /// **This input separates the two.** With `w = 2` and `dt = 0.5`, theta is exactly 0.5, where
    /// the series gives `0.5 - 0.125/6 = 0.47916667` and a true `sin(0.5)` gives `0.47942554` — a
    /// gap of 2.6e-4, far outside the tolerance below. A transcription reaching for `MathF.Sin`
    /// because it "is" the sine reddens here.
    /// </remarks>
    [Test]
    public void Delta_ForAQuarterRadianHalfAngle_UsesTheTaylorSineRatherThanASine()
    {
        (float X, float Y, float Z, float W) delta =
            IvpQuaternion.Delta((2f, 0f, 0f), 0.5f);

        delta.X.ShouldBe(0.47916667f, Close, "0.5 - 0.5^3/6, not sin(0.5)");
        delta.Y.ShouldBe(0f, Close);
        delta.Z.ShouldBe(0f, Close);

        // w = sqrt(1 - |xyz|^2), recovered from unit length rather than from a cosine series.
        delta.W.ShouldBe(MathF.Sqrt(1f - (0.47916667f * 0.47916667f)), Close);
    }

    /// <remarks>
    /// **Each axis gets its own half-angle independently**, which is NOT a rotation about the
    /// combined axis. With equal rates on two axes the true rotation would be about their bisector
    /// by the combined angle; IVP writes each component's own series and then recovers `w` from
    /// whatever length is left over.
    ///
    /// So the vector part below is symmetric and the real part is `sqrt(1 - 2x^2)` — smaller than a
    /// single-axis turn of the same per-axis rate, which is the observable difference.
    /// </remarks>
    [Test]
    public void Delta_ForTwoAxesAtOnce_BuildsEachAxisSeparatelyRatherThanOneCombinedTurn()
    {
        (float X, float Y, float Z, float W) delta = IvpQuaternion.Delta((2f, 2f, 0f), 0.5f);

        delta.X.ShouldBe(0.47916667f, Close);
        delta.Y.ShouldBe(0.47916667f, Close);
        delta.Z.ShouldBe(0f, Close);

        delta.W.ShouldBe(
            MathF.Sqrt(1f - (2f * 0.47916667f * 0.47916667f)),
            Close,
            "the real part is what unit length leaves, not cos of a combined angle");
    }

    /// <remarks>
    /// **The product is the plain Hamilton product with NO alignment step**, which is where IVP and
    /// Valve's own mathlib differ. `QuaternionMult` in `mathlib_base.cpp` calls `QuaternionAlign`
    /// first and negates the second rotation when the two point opposite ways; `FUN_180070d60` does
    /// not, and reaching for the mathlib one because it is already in this codebase would flip a
    /// sign on exactly the inputs alignment exists for.
    ///
    /// The two quaternions below are more than a half-turn apart, so alignment WOULD flip the second
    /// — the input where the two implementations differ rather than agree.
    /// </remarks>
    [Test]
    public void Product_ForTwoRotationsMoreThanHalfATurnApart_DoesNotAlignThem()
    {
        (float X, float Y, float Z, float W) first = (0f, 0f, 0.70710678f, 0.70710678f);
        (float X, float Y, float Z, float W) second = (0f, 0f, -0.70710678f, -0.70710678f);

        (float X, float Y, float Z, float W) product = IvpQuaternion.Product(first, second);

        // Unaligned: (0,0,s,c) * (0,0,-s,-c) = (0, 0, -2sc, -(c^2 - s^2)) = (0, 0, -1, 0).
        product.X.ShouldBe(0f, Close);
        product.Y.ShouldBe(0f, Close);
        product.Z.ShouldBe(-1f, Close, "aligned, this would be +1");
        product.W.ShouldBe(0f, Close);
    }

    /// <remarks>
    /// **A quaternion already at unit length is returned untouched** — `FUN_180070c60` tests
    /// `|1 - |q|^2|` against a tolerance and only then runs its Newton-Raphson reciprocal square
    /// root. An implementation that always divides changes the bits of a value the engine leaves
    /// alone, which accumulates over a corpse's lifetime rather than showing up at once.
    /// </remarks>
    [Test]
    public void Normalise_ForAQuaternionAlreadyAtUnitLength_ReturnsItUnchanged()
    {
        (float X, float Y, float Z, float W) unit = (0f, 0f, 0f, 1f);

        IvpQuaternion.Normalise(unit).ShouldBe(unit);
    }

    /// <remarks>
    /// **And one that is not gets scaled to unit length**, which is the control on the assertion
    /// above: without it, a `Normalise` that did nothing at all would pass.
    /// </remarks>
    [Test]
    public void Normalise_ForAQuaternionOffUnitLength_ScalesItBack()
    {
        (float X, float Y, float Z, float W) result = IvpQuaternion.Normalise((0f, 0f, 0f, 4f));

        result.W.ShouldBe(1f, Close);
    }

    /// <remarks>
    /// **Rotation sub-steps when `|w| * dt` passes 1/6 radian**, and the constants behind that were
    /// dumped: `DAT_1800fdf90` is 0.027777777777777776 — exactly 1/36 — compared against
    /// `|w|^2 * dt^2`, and `DAT_1800fdfa0` is 144.0, so the count is
    /// `(int)sqrt( |w|^2 dt^2 * 144 ) + 1`, which is `floor( 12 |w| dt ) + 1`.
    ///
    /// With `|w| = 1` and `dt = 0.5` that is `floor(6) + 1 = 7`. **A transcription stepping rotation
    /// once per PSI is right for a settling corpse and wrong for a limb that is whipping**, which is
    /// the frame anyone watching a demo is looking at.
    /// </remarks>
    [Test]
    public void SubSteps_ForAFastSpin_IsTwelveTimesTheTurnPlusOne()
    {
        IvpIntegrator.SubSteps((1f, 0f, 0f), 0.5f).ShouldBe(7);
    }

    /// <remarks>
    /// **The comparison is strictly less-than** — `if ( _DAT_1800fdf90 &lt; dVar10 )` — so a turn
    /// below 1/6 radian does not sub-step at all.
    ///
    /// **`1f / 6f` is NOT a usable boundary input, and finding that out was the point of trying it.**
    /// The step is a float in the engine too, and `(float)(1/6)` is 0.16666667163…, whose square is
    /// 0.02777778… — just ABOVE the dumped threshold of 0.027777777777777776. So the arithmetic
    /// sub-steps at what looks like the exact boundary, and a test asserting otherwise fails against
    /// correct code. The inputs below sit either side of it with room to spare.
    /// </remarks>
    [Test]
    public void SubSteps_ForATurnBelowTheThreshold_StaysAtOne()
    {
        IvpIntegrator.SubSteps((1f, 0f, 0f), 0.16f).ShouldBe(
            1, "0.16 radian of turn is under 1/6, so the whole step is one rotation");

        IvpIntegrator.SubSteps((1f, 0f, 0f), 0.17f).ShouldBe(
            3, "a hair past it, and 12 * 0.17 = 2.04 floors to 2");
    }

    /// <remarks>
    /// **Euler's torque-free equations, and all three axes read the OLD angular velocity.** The
    /// decompiled body precomputes the two cross products it needs before writing any of them back:
    ///
    /// <code>
    /// fVar17 = local_110 * local_118;    // wz * wx, using the OLD wx
    /// fVar18 = local_114 * local_118;    // wy * wx, using the OLD wx
    /// local_118 = (local_110 * local_114 * fVar9) * dVar11 + local_118;   // wx updated
    /// local_114 = (fVar17 * fVar16) * dVar11 + local_114;                 // uses the precomputed
    /// local_110 = (fVar18 * fVar15) * dVar11 + local_110;
    /// </code>
    ///
    /// So it is a SIMULTANEOUS update. Writing the three in sequence — the way anyone would — feeds
    /// the new `wx` into `wy` and both into `wz`, and the difference grows with the timestep. The
    /// input below has all three rates non-zero and an asymmetric inertia, which is the only shape
    /// where the two orders disagree.
    /// </remarks>
    [Test]
    public void FreeRotation_WithAllThreeRatesTurning_UpdatesEveryAxisFromTheOldVelocity()
    {
        (float X, float Y, float Z) inertia = (2f, 3f, 5f);
        (float X, float Y, float Z) inverse = (1f / 2f, 1f / 3f, 1f / 5f);

        (float X, float Y, float Z) turned =
            IvpIntegrator.FreeRotation((1f, 2f, 4f), inertia, inverse, 0.25f);

        // (Iy - Iz) * invIx = (3 - 5) * 0.5   = -1      -> wx += wz*wy*-1*0.25   = 1 - 2      = -1
        // (Iz - Ix) * invIy = (5 - 2) / 3     =  1      -> wy += wz*wx* 1*0.25   = 2 + 1      =  3
        // (Ix - Iy) * invIz = (2 - 3) * 0.2   = -0.2    -> wz += wy*wx*-0.2*0.25 = 4 - 0.1    =  3.9
        turned.X.ShouldBe(-1f, Close);
        turned.Y.ShouldBe(3f, Close);
        turned.Z.ShouldBe(3.9f, Close);
    }

    /// <remarks>
    /// **Position integrates against the PREVIOUS step's velocity**, and the cache is refreshed
    /// only afterwards:
    ///
    /// <code>
    /// *(double *)(param_1 + 0x150) = (double)*(float *)(param_1 + 0x170) * dVar9 + …
    /// …
    /// *(undefined4 *)(param_1 + 0x170) = *(undefined4 *)(param_1 + 0x140);
    /// </code>
    ///
    /// **So a body does not move on the step its velocity was first set.** A ragdoll built with a
    /// velocity from its death animation stands still for one step and then goes; an integrator
    /// using the current velocity moves it immediately and is a step ahead of TF2 for ever after.
    /// </remarks>
    [Test]
    public void Step_OnABodysFirstStep_DoesNotMoveItYet()
    {
        IvpRigidBody body = new()
        {
            Velocity = (10f, 0f, 0f),
        };

        IvpIntegrator.Step(body, positionDelta: 1d, orientationDelta: 1f);

        body.Position.X.ShouldBe(0d, "the previous-step velocity was zero");

        // The control: the cache took the velocity, so the NEXT step moves it.
        IvpIntegrator.Step(body, positionDelta: 1d, orientationDelta: 1f);

        body.Position.X.ShouldBe(10d, Close);
    }

    /// <remarks>
    /// **The visible orientation is committed BEFORE the working one advances**, so it is one step
    /// behind by design:
    ///
    /// <code>
    /// *(undefined8 *)(param_1 + 0x180) = *(undefined8 *)(param_1 + 0x1a0);   // current := predicted
    /// …
    /// FUN_180070d60((double *)(param_1 + 0x1a0),(double *)(param_1 + 0x1a0),local_48);
    /// </code>
    ///
    /// **This document had it the other way round for half a day**, which would draw a corpse a step
    /// ahead of the engine. The assertion is that after one step the visible quaternion is still the
    /// identity it started as, while the working one has moved — the input where committing before
    /// and after differ.
    ///
    /// **The step below is deliberately slow enough not to sub-step.** A first version used
    /// `w = 2, dt = 0.5`, which is a full radian of turn and therefore thirteen sub-steps, so the
    /// predicted single-step value was wrong against correct code. `dt = 0.05` puts the turn at 0.1
    /// radian, under the 1/6 threshold, and the half-angle is exactly 0.05.
    /// </remarks>
    [Test]
    public void Step_AfterOneStep_LeavesTheVisibleOrientationOneStepBehind()
    {
        IvpRigidBody body = new()
        {
            AngularVelocity = (2f, 0f, 0f),
        };

        IvpIntegrator.Step(body, positionDelta: 1d, orientationDelta: 0.05f);

        body.Orientation.ShouldBe(
            (0f, 0f, 0f, 1f), "the visible orientation is last step's working one");

        // 0.05 - 0.05^3 / 6 = 0.049979167
        body.WorkingOrientation.X.ShouldBe(
            0.049979167f, Close, "while the working one has already turned");
    }
}
