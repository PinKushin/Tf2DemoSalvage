using System;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's per-step speed and spin damping — <c>FUN_180077a20</c> (B58, D146).
/// </summary>
/// <remarks>
/// **Written against the decompiled routine before the code that satisfies it**, with every
/// constant settled in the disassembly rather than in the decompiled C — see
/// <see cref="IvpDamping"/> for the transcription and the citations.
///
/// **The two branches are the whole subject.** IVP damps by `exp(−damp·dt)` but only once the step
/// is large enough to be worth a transcendental, and uses `1 − damp·dt` below that — so a test that
/// exercised only one branch would pass against an implementation that had never heard of the
/// other, and the thresholds differ between the rotational and the linear halves.
/// </remarks>
public sealed class IvpDampingConformanceTests
{
    private const float Step = 1f / 66f;

    /// <remarks>
    /// **`fVar4 = DAT_1800ea988 - fVar4`, and `DAT_1800ea988` is `1.0`.** The rotational branch is
    /// chosen on the SUM OF SQUARES of the three per-axis products against `0.5`, so a TF2
    /// ragdoll's own numbers — rotational damping 4 to 16 at a 66 Hz step — sit under it: three
    /// lanes of `4 × 0.01515` square to `0.011`.
    /// </remarks>
    [Test]
    public void Apply_WithADampingBelowTheThreshold_ScalesSpinByOneMinusTheProduct()
    {
        IvpRigidBody body = Body();

        body.RotationDamping = 4f;
        body.AngularVelocity = (10f, 20f, 30f);

        IvpDamping.Apply([body], Step);

        float expected = 1f - (4f * Step);

        body.AngularVelocity.X.ShouldBe(10f * expected, 1e-4f);
        body.AngularVelocity.Y.ShouldBe(20f * expected, 1e-4f);
        body.AngularVelocity.Z.ShouldBe(30f * expected, 1e-4f);
    }

    /// <remarks>
    /// **The other branch, and the input that reaches it.** Three lanes of `damp × dt` must square
    /// to at least `0.5`, so `damp = 30` at this step gives `3 × (0.4545)² = 0.62`. Below that the
    /// linear form is used and the two predictions differ by about 10%, which is what makes this a
    /// real condition rather than a restatement.
    /// </remarks>
    [Test]
    public void Apply_WithADampingAboveTheThreshold_ScalesSpinByTheExponential()
    {
        IvpRigidBody body = Body();

        body.RotationDamping = 30f;
        body.AngularVelocity = (10f, 10f, 10f);

        IvpDamping.Apply([body], Step);

        float product = 30f * Step;

        // The control on the condition itself: this input really is on the exponential side.
        (3f * product * product).ShouldBeGreaterThan(0.5f);

        body.AngularVelocity.X.ShouldBe(10f * MathF.Exp(-product), 1e-4f);

        // And the two branches genuinely disagree here, or the test above proves nothing.
        MathF.Abs(MathF.Exp(-product) - (1f - product)).ShouldBeGreaterThan(0.01f);
    }

    /// <remarks>
    /// **The linear half has its OWN threshold, `0.25`, and it is not a sum of squares** — it is
    /// the single product `dt × speedDamp`. Sharing the rotational test would pass against an
    /// implementation that used one threshold for both.
    /// </remarks>
    [Test]
    public void Apply_WithASpeedDamping_ScalesVelocityByItsOwnBranch()
    {
        IvpRigidBody slow = Body();

        slow.Damping = 4f;
        slow.Velocity = (100f, 200f, 300f);

        IvpDamping.Apply([slow], Step);

        float gentle = 1f - (4f * Step);

        slow.Velocity.X.ShouldBe(100f * gentle, 1e-3f);

        IvpRigidBody fast = Body();

        fast.Damping = 30f;
        fast.Velocity = (100f, 0f, 0f);

        IvpDamping.Apply([fast], Step);

        // 30 x 0.01515 is 0.4545, over the 0.25 the linear half tests against — where the SAME
        // number sat under the rotational half's 0.5 in the test above. That is the pair of
        // thresholds, and one constant for both would fail here.
        fast.Velocity.X.ShouldBe(100f * MathF.Exp(-30f * Step), 1e-3f);
    }

    /// <remarks>
    /// **The control.** A body declaring no damping must come out untouched — without this, "damps
    /// correctly" and "scales everything by something" are the same observation, and TF2's own
    /// elements declare LINEAR damping of exactly zero.
    /// </remarks>
    [Test]
    public void Apply_WithNoDamping_ChangesNothing()
    {
        IvpRigidBody body = Body();

        body.Velocity = (100f, 200f, 300f);
        body.AngularVelocity = (10f, 20f, 30f);

        IvpDamping.Apply([body], Step);

        body.Velocity.X.ShouldBe(100f);
        body.AngularVelocity.Z.ShouldBe(30f);
    }

    /// <remarks>
    /// **Bit `0x10` skips this as well as gravity**, because the engine calls it from inside that
    /// gate — `FUN_180074c80` tests the bit and then makes both helper calls and the add. A body
    /// that skipped gravity but was still damped would slow to a halt in mid-air.
    /// </remarks>
    [Test]
    public void Apply_ForABodyThatSkipsGravity_SkipsDampingToo()
    {
        IvpRigidBody body = Body();

        body.SkipsGravity = true;
        body.RotationDamping = 4f;
        body.Damping = 4f;
        body.Velocity = (100f, 0f, 0f);
        body.AngularVelocity = (10f, 0f, 0f);

        IvpDamping.Apply([body], Step);

        body.Velocity.X.ShouldBe(100f);
        body.AngularVelocity.X.ShouldBe(10f);
    }

    private static IvpRigidBody Body() => new();
}
