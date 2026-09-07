using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The relaxation a ragdoll's joints are solved with (B58, D146).
/// </summary>
/// <remarks>
/// **Two iterations, each walking the list forwards and then backwards.** The count is
/// `additionalIterations + 2`, established by an exact inverse pair — `FUN_18003c330` stores
/// `+ 2` and `FUN_18003d240` reads back `- 2` — and `ragdoll_shared.cpp:274-276` leaves
/// `additionalIterations` at zero for every ragdoll. So a TF2 corpse gets exactly two.
///
/// **The weight comes off a dumped table** at `0x1800eeb70`, whose first four entries are all
/// `0.4`, and reaches the solve as `FUN_180038620`'s fourth argument — read from the driver's own
/// call sites:
///
/// <code>
/// 18003c815  MOVAPS XMM0,[0x1800eeb70]   ; {0.4, 0.4, 0.4, 0.4}
/// 18003c864  MOVAPS XMM3,XMM12           ; the REBUILD gets 1.0
/// 18003c881  CALL   [RAX + 0x18]         ; slot 3, expensive
/// 18003c8e5  MOVAPS XMM3,XMM6            ; each SWEEP gets the weight
/// 18003c8fd  CALL   [RAX + 0x20]         ; slot 4, descending
/// 18003c93c  CALL   [RAX + 0x20]         ; slot 4, ascending
/// </code>
/// </remarks>
public sealed class IvpConstraintGroupConformanceTests
{
    private const float Close = 1e-5f;

    /// <summary>A joint holding two bodies, limited hard on its swing.</summary>
    private static IvpRagdollJoint Joint(IvpRigidBody a, IvpRigidBody b)
    {
        IvpRagdollConstraint constraint = IvpRagdollConstraint.FromDegrees(
            primary: (-30f, 15f),
            narrower: (-25f, 25f),
            wider: (-79f, 57f),

            // Two bodies whose bind frames agree, which is what identity on both sides means —
            // these tests are about the relaxation and the sweep order, not about the frames.
            reference: IvpConstraintFrame.Identity,
            attached: IvpConstraintFrame.Identity);

        return new IvpRagdollJoint { BodyA = a, BodyB = b, Constraint = constraint };
    }

    /// <remarks>
    /// **The stock count is two, and it is a floor rather than a setting** — the getter subtracts
    /// the same 2 the constructor added, so `additionalIterations` of zero means two sweeps and
    /// never fewer.
    /// </remarks>
    [Test]
    public void Iterations_WithTheRagdollDefaults_IsExactlyTwo()
    {
        new IvpConstraintGroup().Iterations.ShouldBe(2);

        new IvpConstraintGroup { AdditionalIterations = 3 }.Iterations.ShouldBe(5);
    }

    /// <remarks>
    /// **Each iteration walks the list forwards and then backwards**, which is what stops a chain
    /// of joints biasing toward whichever end is solved first. Two iterations is therefore FOUR
    /// passes over the list, and the order is recorded so a change to it fails here rather than
    /// showing up as a corpse that leans.
    /// </remarks>
    [Test]
    public void Solve_OverTheStockTwoIterations_WalksTheListDescendingThenAscendingEachTime()
    {
        List<int> order = [];

        IvpConstraintGroup group = new();

        for (int index = 0; index < 3; index++)
        {
            int captured = index;

            group.Joints.Add(new IvpRagdollJoint
            {
                BodyA = new IvpRigidBody(),
                BodyB = new IvpRigidBody(),
                Constraint = IvpRagdollConstraint.FromDegrees(
                    (0f, 0f),
                    (0f, 0f),
                    (0f, 0f),
                    IvpConstraintFrame.Identity,
                    IvpConstraintFrame.Identity),
                Solved = () => order.Add(captured),
            });
        }

        group.Solve();

        order.ShouldBe([2, 1, 0, 0, 1, 2, 2, 1, 0, 0, 1, 2]);
    }

    /// <remarks>
    /// **A body inside its limits is left alone.** The control for everything below — without it,
    /// "the solver corrected the joint" and "the solver perturbs everything it touches" are the
    /// same observation.
    /// </remarks>
    [Test]
    public void Solve_WithAJointAtItsBindPose_LeavesBothBodiesAlone()
    {
        IvpRigidBody a = new();
        IvpRigidBody b = new();

        IvpConstraintGroup group = new();

        group.Joints.Add(Joint(a, b));
        group.Solve();

        a.AngularVelocity.X.ShouldBe(0f, Close);
        b.AngularVelocity.X.ShouldBe(0f, Close);
    }

    /// <remarks>
    /// **A joint swung past its limit is pushed back, and the two bodies move oppositely.** The
    /// exact magnitude depends on the whole chain — deflection, effective mass, the 0.4 weight,
    /// four passes — so this asserts the DIRECTION and the antisymmetry, which is what a wrong sign
    /// anywhere in that chain breaks.
    ///
    /// **The swing is the axis used** because it is the one measured as a sine, so its deflection
    /// grows monotonically from zero and the sign of the correction is unambiguous.
    /// </remarks>
    [Test]
    public void Solve_WithASwingPastItsLimit_PushesTheTwoBodiesOppositeWays()
    {
        IvpRigidBody a = new();

        // 40 degrees about the narrower swing axis, against a limit of 25.
        IvpRigidBody b = new() { Orientation = (0f, 0.34202015f, 0f, 0.9396926f) };

        IvpConstraintGroup group = new();

        group.Joints.Add(Joint(a, b));
        group.Solve();

        a.AngularVelocity.ShouldNotBe((0f, 0f, 0f), "the limit was exceeded, so it was corrected");

        b.AngularVelocity.X.ShouldBe(-a.AngularVelocity.X, Close, "equal and opposite");
        b.AngularVelocity.Y.ShouldBe(-a.AngularVelocity.Y, Close);
        b.AngularVelocity.Z.ShouldBe(-a.AngularVelocity.Z, Close);
    }

    /// <remarks>
    /// **This test exists because a sabotage reddened NOTHING.** Reversing the cone axis from
    /// `cross(B[primary], A[primary])` to its opposite left every test green, which says the suite
    /// could not see the cone's sign at all — the swing test's antisymmetry survives a flip that
    /// negates both bodies together, and the bind-pose test has a zero cross either way.
    ///
    /// **The demoman's own joints hide it too, which is worth knowing.** His wider swing is 136°,
    /// so the cone bound is `±1.187` and a cosine can never leave it — the cone limit never fires
    /// on that joint at all. A joint with a narrow wider-swing is needed to reach the clamp.
    ///
    /// **The direction asserted here is READ, not guessed.** `FUN_1800372c0` builds its axis as
    /// `param_5 × param_6` — basis crossed with axis, so `B[primary] × A[primary]`. Tipping body B
    /// by 40° about Y makes that `(0, −sin 40°, 0)`, and the impulse is added, so body A's
    /// correction must be NEGATIVE about Y. What is still open is whether a limit in that sense is
    /// physically sensible; that is `docs/findings/51`'s business, and this test pins the
    /// transcription either way.
    /// </remarks>
    [Test]
    public void Solve_WithANarrowConeExceeded_CorrectsAlongTheCrossOfTheTwoPrimaryAxes()
    {
        IvpRigidBody a = new();
        IvpRigidBody b = new() { Orientation = (0f, 0.34202015f, 0f, 0.9396926f) };

        IvpRagdollConstraint constraint = IvpRagdollConstraint.FromDegrees(
            primary: (-30f, 15f),
            narrower: (-25f, 25f),
            wider: (-20f, 20f),
            reference: IvpConstraintFrame.Identity,
            attached: IvpConstraintFrame.Identity);

        IvpConstraintGroup group = new();

        group.Joints.Add(new IvpRagdollJoint { BodyA = a, BodyB = b, Constraint = constraint });
        group.Solve();

        a.AngularVelocity.Y.ShouldBeLessThan(0f, "the cone axis is B × A, which points along −Y here");
    }

    /// <remarks>
    /// **An immovable body takes no correction at all and the other takes it whole**, which is how
    /// a corpse's limb behaves against the world rather than against another limb.
    /// </remarks>
    [Test]
    public void Solve_AgainstAnImmovableBody_MovesOnlyTheOther()
    {
        IvpRigidBody a = new();

        IvpRigidBody b = new()
        {
            Immovable = true,
            Orientation = (0f, 0.34202015f, 0f, 0.9396926f),
        };

        IvpConstraintGroup group = new();

        group.Joints.Add(Joint(a, b));
        group.Solve();

        a.AngularVelocity.ShouldNotBe((0f, 0f, 0f));
        b.AngularVelocity.ShouldBe((0f, 0f, 0f), "static bodies are never pushed");
    }
}
