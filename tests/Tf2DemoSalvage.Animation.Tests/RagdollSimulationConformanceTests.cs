using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A model's ragdoll, turned into running bodies and joints (B58, D142, D146).
/// </summary>
/// <remarks>
/// **This is the seam between the published half of a corpse and the closed one.** Everything on
/// the `RagdollBody` side comes out of `ragdoll_shared.cpp`; everything on the `IvpEnvironment`
/// side was read out of `vphysics.dll`. Both halves had green suites before this type existed and
/// neither could tell whether they were ever joined — which is the shape that has shipped three
/// no-ops in this project.
/// </remarks>
public sealed class RagdollSimulationConformanceTests
{
    private const float Tolerance = 1e-4f;

    private const float Step = 1f / 66f;

    /// <remarks>
    /// **A body per solid and a joint per constraint** — the count assertion that catches a
    /// constraint silently dropped, which is what a `.phy` naming an out-of-range body would do.
    /// </remarks>
    [Test]
    public void Create_FromATwoSolidRagdoll_MakesTwoBodiesAndOneJoint()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Environment.Bodies.Count.ShouldBe(2);
        simulation.Environment.Constraints.Joints.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **A corpse falls, and this is the assertion that says the whole chain is connected** —
    /// gravity reaching a body, the integrator advancing it, and the position crossing back out of
    /// IVP's metres-and-Y-up convention into Source's inches-and-Z-up.
    ///
    /// **Two steps, not one**, because IVP integrates position by the PREVIOUS step's velocity: a
    /// body gains speed on the step gravity is applied and moves on the next one. Asserting after
    /// one step would read zero and look like a broken pipeline.
    ///
    /// The predicted drop is `800 / 66 / 66` inches — `sv_gravity` for one step, applied for one
    /// step — and it is NEGATIVE in Z, which is the axis-and-sign half of the conversion.
    /// </remarks>
    [Test]
    public void Step_TwiceUnderGravity_MovesTheRootDownInSourceSpace()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Step();
        simulation.Step();

        simulation.State()[0].Position.Z.ShouldBe(-800f / 66f / 66f, 1e-2f);
        simulation.State()[0].Position.X.ShouldBe(0f, Tolerance, "and it does not drift sideways");
    }

    /// <remarks>
    /// **The output-level assertion: a stepped ragdoll produces bone matrices.** Everything else
    /// here tests the simulation; this tests that its state is in the shape `RagdollBody.Pose`
    /// consumes, which is the only thing a renderer will ever ask for.
    ///
    /// **The root's matrix must have MOVED**, which is what separates "the pipe is connected" from
    /// "the pipe returns identity matrices" — the failure a shape-only assertion could not see.
    /// </remarks>
    [Test]
    public void Pose_AfterStepping_ProducesBoneMatricesThatHaveMoved()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Step();
        simulation.Step();

        float[][] bones = simulation.Pose(boneCount: 2);

        bones.Length.ShouldBe(2);
        bones[0].Length.ShouldBe(12);
        bones[0][11].ShouldBeLessThan(0f, "the root fell, and the matrix carries it");
    }

    /// <remarks>
    /// **A joint's three axes are not interchangeable, so the permutation is asserted.** The engine
    /// orders them by mechanics; this orders them by declared range, which is a stated departure —
    /// and either way the WIDEST must land on the axis the twist is measured about, because that is
    /// the one whose bounds are negated and swapped.
    ///
    /// The ranges below are 10°, 90° and 40°, all different, so a transcription that took them in
    /// declaration order lands somewhere else.
    /// </remarks>
    [Test]
    public void Create_WithThreeUnequalAxisRanges_GivesTheTwistTheWidestOne()
    {
        RagdollConstraint constraint = new(
            0,
            1,
            new ConstraintAxis(-5f, 5f, 0f),
            new ConstraintAxis(-45f, 45f, 0f),
            new ConstraintAxis(-20f, 20f, 0f));

        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(PhysicsWith(constraint), Skeleton())!, Step, Start());

        IvpRagdollConstraint joint = simulation.Environment.Constraints.Joints[0].Constraint;

        const float Radian = 0.017453292f;

        joint.Twist.Lower.ShouldBe(-45f * Radian, Tolerance, "the 90° axis, negated hi");
        joint.Twist.Upper.ShouldBe(45f * Radian, Tolerance);
    }

    /// <remarks>
    /// **A starting state of the wrong length is refused rather than truncated**, because a `.phy`
    /// is a stranger's file and a mismatch here would silently simulate a different skeleton.
    /// </remarks>
    [Test]
    public void Create_WithAStateThatDoesNotMatchTheElements_Refuses()
    {
        RagdollBody ragdoll = RagdollBody.Build(Physics(), Skeleton())!;

        Should.Throw<System.ArgumentException>(
            () => RagdollSimulation.Create(ragdoll, Step, [(Vector3.Zero, Quaternion.Identity)]));
    }

    private static RagdollSimulation Simulation() =>
        RagdollSimulation.Create(RagdollBody.Build(Physics(), Skeleton())!, Step, Start());

    private static (Vector3, Quaternion)[] Start() =>
        [(Vector3.Zero, Quaternion.Identity), (Vector3.Zero, Quaternion.Identity)];

    private static readonly ConstraintAxis Axis = new(-30f, 30f, 0f);

    private static PhysicsModel Physics() => PhysicsWith(new RagdollConstraint(0, 1, Axis, Axis, Axis));

    private static PhysicsModel PhysicsWith(RagdollConstraint constraint) =>
        PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [constraint],
            2,
            checksum: 0);

    private static IReadOnlyList<StudioBone> Skeleton() =>
        [
            Bone("bip_root", -1, 1f, 2f, 3f),
            Bone("bip_child", 0, 4f, 6f, 3f),
        ];

    /// <summary>A bone at a bind position, with the world-to-bone matrix that implies.</summary>
    private static StudioBone Bone(string name, int parent, float x, float y, float z) =>
        new(
            name,
            parent,
            (x, y, z),
            (0f, 0f, 0f, 1f),
            new float[]
            {
                1f, 0f, 0f, -x,
                0f, 1f, 0f, -y,
                0f, 0f, 1f, -z,
            });
}
