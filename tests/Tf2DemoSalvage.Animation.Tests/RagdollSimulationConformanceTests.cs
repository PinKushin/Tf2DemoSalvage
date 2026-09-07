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

    /// <remarks>
    /// **The CHILD is the reference body and the parent is the attached one**, which is the
    /// opposite of what a reader would assume from `childElement.parentIndex`. The engine spells it
    /// out at the call:
    ///
    /// <code>
    ///   childElement.pConstraint = pPhysEnv->CreateRagdollConstraint( childElement.pObject,
    ///       ragdoll.list[constraint.parentIndex].pObject, ragdoll.pGroup, constraint );
    /// </code>
    ///
    /// (`ragdoll_shared.cpp:253`) against
    /// `CreateRagdollConstraint( IPhysicsObject *pReferenceObject, IPhysicsObject *pAttachedObject,
    /// … )` — *"Create a constraint in the space of pReferenceObject which is attached by the
    /// constraint to pAttachedObject"* (`vphysics_interface.h:572`).
    ///
    /// **It decides two things, and neither is cosmetic.** The frames are per SIDE —
    /// `constraintToReference` is the identity the CHILD carries and `constraintToAttached` is the
    /// bone-to-bone transform the PARENT carries — and the joint friction is scaled by the
    /// reference object's `GetMass` (`FUN_18000eac0`, `docs/findings/51`), which is the limb's own
    /// mass rather than whatever it hangs from.
    /// </remarks>
    [Test]
    public void Create_ForAConstraint_MakesTheChildTheReferenceBody()
    {
        RagdollSimulation simulation = Simulation();

        IvpRagdollJoint joint = simulation.Environment.Constraints.Joints[0];

        joint.BodyA.ShouldBeSameAs(simulation.Environment.Bodies[1], "the child is the reference");
        joint.BodyB.ShouldBeSameAs(simulation.Environment.Bodies[0], "the parent is the attached");
    }

    /// <remarks>
    /// **The control the two frames exist for, and the only test here that can see them.**
    /// `constraintToReference` and `constraintToAttached` are shipped as a PAIR precisely so that
    /// `R_ref · (toReference · e_k)` and `R_att · (toAttached · e_k)` are the same world vector at
    /// the bind pose — so a ragdoll standing in its own bind pose must measure no deflection at
    /// all, whatever its bones are turned to.
    ///
    /// **The prediction is exact and it is three different quantities**: the twist is an angle and
    /// reads `0`, the swing is a SINE and reads `0`, and the cone is a COSINE and reads `1`.
    ///
    /// **The skeleton is turned, which is what makes this test able to fail.** With an unrotated
    /// pair every reading is `0, 0, 1` whether the frames are carried or thrown away — the
    /// condition where correct and broken predict the same observation. With the child turned a
    /// quarter turn, identity frames put the two primary axes at right angles and the cone reads
    /// `0`; swapping the reference and attached bodies does the same.
    /// </remarks>
    [Test]
    public void Rebuild_AtTheBindPoseOfATurnedSkeleton_ReadsNoDeflectionOnAnyAxis()
    {
        IvpRagdollJoint joint = TurnedSimulation().Environment.Constraints.Joints[0];

        joint.Rebuild();

        joint.Constraint.Twist.Angle.ShouldBe(0f, Tolerance, "the joint is not twisted at rest");
        joint.Constraint.Swing.Angle.ShouldBe(0f, Tolerance, "nor swung — this one is a sine");
        joint.Constraint.Cone.Angle.ShouldBe(1f, Tolerance, "and the cone is a cosine, so it is one");
    }

    /// <remarks>
    /// **A constraint joining a body to itself makes no joint at all.** `RagdollAddConstraint`
    /// nulls BOTH indices on it — *"Bogus constraint on ragdoll %s"*, `ragdoll_shared.cpp:217` —
    /// so the `childIndex >= 0 &amp;&amp; parentIndex >= 0` gate below never opens and
    /// `CreateRagdollConstraint` is never reached.
    ///
    /// **The bodies are still built**, which is the half that separates "dropped the constraint"
    /// from "refused the file": `RagdollAddSolid` ran before any constraint was looked at, and a
    /// `.phy` is a stranger's file (D32) rather than something to reject wholesale.
    /// </remarks>
    [Test]
    public void Create_WithAConstraintJoiningABodyToItself_MakesTheBodiesAndNoJoint()
    {
        RagdollConstraint bogus = new(1, 1, Axis, Axis, Axis);

        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(PhysicsWith(bogus), Skeleton())!, Step, Start());

        simulation.Environment.Bodies.Count.ShouldBe(2);
        simulation.Environment.Constraints.Joints.Count.ShouldBe(0);
    }

    /// <summary>The turned skeleton, with each body started at the orientation it binds in.</summary>
    /// <remarks>
    /// **The starting state is the bind pose, which is the whole point.** A body's orientation is
    /// its bone's in the world, so the root stands unrotated and the child stands turned; anything
    /// else here would be asserting about a corpse mid-fall rather than about the frames.
    /// </remarks>
    private static RagdollSimulation TurnedSimulation() =>
        RagdollSimulation.Create(
            RagdollBody.Build(Physics(), RagdollSkeletons.Turned())!,
            Step,
            [
                (Vector3.Zero, Quaternion.Identity),
                (Vector3.Zero,
                    new Quaternion(0f, 0f, RagdollSkeletons.SinOf45, RagdollSkeletons.SinOf45)),
            ]);

    private static RagdollSimulation Simulation() =>
        RagdollSimulation.Create(RagdollBody.Build(Physics(), Skeleton())!, Step, Start());

    /// <remarks>
    /// **A joint pulls its bodies TOGETHER, and the sign is the whole of it.** A ball-and-socket
    /// with the two halves swapped drives them apart instead, and the error grows every sweep — so
    /// this is not a refinement of the position, it is the difference between a ragdoll and an
    /// explosion. It was measured as one: a corpse pass that took fifteen seconds took past twenty
    /// minutes, because bodies flung across the map make the collision broadphase examine
    /// everything.
    ///
    /// **The condition is a joint pulled apart along its own axis** — the child moved a further
    /// four units from where its bind pose puts it — and the measurement is the separation after a
    /// step, which must be SMALLER. A test that only asserted the bodies moved would pass either
    /// way, which is the whole failure this predicts against.
    ///
    /// **Gravity needs no switching off, because the measurement is RELATIVE**: it pulls both
    /// bodies equally, so it cancels out of the separation and only the joint can change it.
    /// </remarks>
    [Test]
    public void Step_WithAJointPulledApart_BringsTheBodiesCloserTogether()
    {
        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(Physics(), Skeleton())!,
            Step,
            [
                (Vector3.Zero, Quaternion.Identity),
                (new Vector3(7f, 4f, 0f), Quaternion.Identity),
            ]);

        // Bound at (3, 4, 0) and started at (7, 4, 0), so the joint is four units open.
        float before = Separation(simulation);

        before.ShouldBe(4f, Tolerance, "the fixture opens the joint by exactly four units");

        simulation.Step();
        simulation.Step();

        Separation(simulation).ShouldBeLessThan(
            before, "a ball-and-socket closes its error rather than growing it");
    }

    [Test]
    public void Scratch_JointedSliceCount()
    {
        RagdollSimulation simulation = Simulation();

        simulation.Environment.World = FlatFloor();

        for (int step = 0; step < 200; step++)
        {
            simulation.Step();
        }

        TestContext.Out.WriteLine(
            $"slices {simulation.Environment.Slices} contacts {simulation.Environment.Contacts} " +
            $"root {simulation.State()[0].Position} child {simulation.State()[1].Position}");

        simulation.Environment.Slices.ShouldBeLessThan(200000);
    }

    private static IvpWorldCollision FlatFloor()
    {
        IvpWorldCollision world = new();

        world.AddTriangle(
            new Vector3(-500f, -500f, 0f),
            new Vector3(500f, -500f, 0f),
            new Vector3(500f, 500f, 0f));

        world.AddTriangle(
            new Vector3(-500f, -500f, 0f),
            new Vector3(500f, 500f, 0f),
            new Vector3(-500f, 500f, 0f));

        return world;
    }

    /// <summary>How far the child is from where its joint says it should be.</summary>
    private static float Separation(RagdollSimulation simulation)
    {
        (Vector3 Position, Quaternion Orientation)[] state = simulation.State();

        return (state[1].Position - (state[0].Position + new Vector3(3f, 4f, 0f))).Length();
    }

    /// <summary>The two bodies where their bind pose puts them, a joint's length apart.</summary>
    /// <remarks>
    /// **Both used to start at the origin, and that describes a ragdoll that cannot exist.**
    /// `RagdollSkeletons.Straight` binds the child at `(3, 4, 0)` from its parent, so two bodies
    /// stacked on the same point are already a joint's length out of place before the first step.
    /// Nothing noticed while the joints were angular only — an angular constraint cannot see where
    /// a body IS — and the moment the ball-and-socket arrived it correctly hauled them together,
    /// which read as the root drifting sideways by 0.045 units.
    ///
    /// **The code was right and the fixture was wrong**, so the fixture moved. Every prediction
    /// these tests make is about the ROOT, which still starts at the origin, so none of them
    /// changes meaning — and `Step_TwiceUnderGravity_MovesTheRootDownInSourceSpace` now measures a
    /// root that is not being pulled by a joint error the test never meant to create.
    /// </remarks>
    private static (Vector3, Quaternion)[] Start() =>
        [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 0f), Quaternion.Identity)];

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

    /// <summary>Two bones at chosen bind positions — <see cref="RagdollSkeletons.Straight"/>.</summary>
    private static IReadOnlyList<StudioBone> Skeleton() => RagdollSkeletons.Straight();
}
