using System;
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
    /// **The twist axis is chosen by MECHANICS, which is the engine's rule** — `FUN_1800393d0`
    /// takes the axis whose rotation moves the two anchors most, weighted by inverse mass, and only
    /// then orders the remaining two by declared range.
    ///
    /// **This test asserted the opposite until B306, and it was asserting our own departure.** The
    /// twist used to be given the WIDEST range, which is a guess this project made when the anchor
    /// term looked unavailable — and a wrong permutation decides which limit clamps which motion,
    /// so it let elbows swing where they should twist. The owner, on the first corpse to draw above
    /// ground: *"thats contorted as hell, theres some other parity point you havent noticed"*.
    ///
    /// **The prediction is arithmetic from the fixture, not from the code.** `RagdollSkeletons`
    /// puts the child at a bind offset of `(3, 4, 0)` from its parent, and rotation about an axis
    /// carries the anchor by `|axis x anchor|`:
    ///
    /// | axis | moved |
    /// |---|---|
    /// | x | `sqrt(4² + 0²)` = 4 |
    /// | y | `sqrt(3² + 0²)` = 3 |
    /// | z | `sqrt(3² + 4²)` = **5** |
    ///
    /// So Z turns the joint and takes the twist — and Z's declared range is the MIDDLE one at 40°,
    /// which is what makes this fixture able to tell the two rules apart. The ranges are 10°, 90°
    /// and 40°, all different, so declaration order lands somewhere else again.
    /// </remarks>
    [Test]
    public void Create_WithThreeUnequalAxisRanges_GivesTheTwistTheAxisItTurnsAbout()
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

        joint.Twist.Lower.ShouldBe(-20f * Radian, Tolerance, "the Z axis, which the anchor turns about");
        joint.Twist.Upper.ShouldBe(20f * Radian, Tolerance);
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

    /// <remarks>
    /// **A jointed ragdoll WITH hulls, resting on a floor — the condition that hung the viewer.**
    /// Joints alone are cheap and contacts alone are cheap; it took both together, and there was no
    /// fixture with both, so the only instrument was a viewer run that takes the desktop, orphans
    /// itself when killed, and cannot be told apart from a wait on the machine-wide lock. Three
    /// runs were spent on that confusion.
    ///
    /// **The bound is what makes this a test rather than a second hang.** A step that needs more
    /// than a few slices is already the defect; asserting a generous ceiling fails in
    /// milliseconds where the real thing failed in four hundred seconds.
    /// </remarks>
    [Test]
    public void Simulate_AJointedRagdollRestingOnAFloor_TakesABoundedNumberOfSlices()
    {
        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(Solid(), Skeleton())!,
            Step,
            [
                (new Vector3(0f, 0f, 40f), Quaternion.Identity),
                (new Vector3(3f, 4f, 40f), Quaternion.Identity),
            ]);

        simulation.Environment.World = FlatFloor();

        for (int step = 0; step < 200; step++)
        {
            simulation.Step();

            IvpRigidBody root = simulation.Environment.Bodies[0];
            IvpRigidBody child = simulation.Environment.Bodies[1];

            float fastest = MathF.Max(Speed(root), Speed(child));

            // **Asserted inside the loop, because the end state cannot tell a settle from an orbit.**
            // A ragdoll that tore itself apart and flew off would be reported by its final position
            // too, but only just — and the number that says which happened is the speed at the
            // moment it goes wrong. Two thousand is the environment's own clamp, so anything near
            // it means the solve saturated rather than converged.
            fastest.ShouldBeLessThan(
                400f,
                $"step {step}: nothing here may approach the velocity clamp, and " +
                $"root {root.Position} child {child.Position}");
        }

        // Two hundred steps, and a settled body needs one slice each.
        simulation.Environment.Slices.ShouldBeLessThan(
            2000, "a resting ragdoll must not subdivide its step over and over");

        ((double)simulation.State()[0].Position.Z).ShouldBeGreaterThan(
            0d, "and it is resting ON the floor, which is what makes the slice count mean anything");
    }

    /// <remarks>
    /// **The core sits at the hull's mass center, not at the bone** (B403) — `FUN_180073df0` places it there
    /// and keeps the object at `−massCenter` inside it. The child starts at `(3, 4, 0)` turned a quarter about
    /// Z, and its mass center is one unit along its own X, which the turn sends along world Y: the core is at
    /// `(3, 5, 0)`. A core left at the bone would be at `(3, 4, 0)`; one offset without the turn, at `(4, 4, 0)`.
    /// </remarks>
    [Test]
    public void Create_AnElementWithAMassCenter_PlacesItsCoreThere()
    {
        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(WithMassProperties(), Skeleton())!, Step, TurnedChild());

        IvpRigidBody child = simulation.Environment.Bodies[1];

        child.Position.X.ShouldBe(3d, 1e-4d);
        child.Position.Y.ShouldBe(5d, 1e-4d);
        child.Position.Z.ShouldBe(0d, 1e-4d);
        child.ObjectOffset.X.ShouldBe(-1f, 1e-4f);
    }

    /// <remarks>
    /// **What the rest of the program reads is the BONE**, as vphysics' `GetPosition` composes the offset back
    /// in (`FUN_180032740`): before any step the child's state is where it started, `(3, 4, 0)`, even though
    /// its core is a unit away.
    /// </remarks>
    [Test]
    public void State_BeforeAnyStep_ReportsEachBoneWhereItStartedNotItsCore()
    {
        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(WithMassProperties(), Skeleton())!, Step, TurnedChild());

        Vector3 child = simulation.State()[1].Position;

        child.X.ShouldBe(3f, Tolerance);
        child.Y.ShouldBe(4f, Tolerance);
        child.Z.ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **Per-axis inertia from the hull, times the scale, times the mass** (`IvpObjectTemplate.CoreInertia`).
    /// The child's hull inertia is `(1, 2, 3)` per kilogram about its own axes, its scale 1 and its mass 2:
    /// `(2, 4, 6)`, above the floor of a tenth of their length (0.75). Its reciprocals follow, and so does the
    /// inverse of the clamped mass.
    /// </remarks>
    [Test]
    public void Create_AnElementWithHullInertia_TakesThePerAxisInertiaOfItsTemplate()
    {
        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(WithMassProperties(), Skeleton())!, Step, TurnedChild());

        IvpRigidBody child = simulation.Environment.Bodies[1];

        child.Inertia.X.ShouldBe(2f, 1e-3f);
        child.Inertia.Y.ShouldBe(4f, 1e-3f);
        child.Inertia.Z.ShouldBe(6f, 1e-3f);
        child.InverseInertia.Z.ShouldBe(1f / 6f, 1e-4f);
        child.InverseMass.ShouldBe(0.5f, 1e-6f);
    }

    /// <remarks>
    /// **A joint's anchors are measured from each body's CORE.** The engine's constraint is created in object
    /// space and taken into core space by the offset: the child's anchor is its own bone origin, which in its
    /// core is `−(1, 0, 0)`; the parent's is where the child's bone stands in the parent's space, `(3, 4, 0)`,
    /// less the parent's mass center `(0, 2, 0)`.
    /// </remarks>
    [Test]
    public void Create_AJoint_AnchorsBothEndsInCoreSpace()
    {
        RagdollSimulation simulation = RagdollSimulation.Create(
            RagdollBody.Build(WithMassProperties(), Skeleton())!, Step, TurnedChild());

        IvpRagdollJoint joint = simulation.Environment.Constraints.Joints[0];

        joint.AnchorA.X.ShouldBe(-1f, 1e-4f);
        joint.AnchorA.Y.ShouldBe(0f, 1e-4f);
        joint.AnchorB.X.ShouldBe(3f, 1e-4f);
        joint.AnchorB.Y.ShouldBe(2f, 1e-4f);
    }

    /// <remarks>
    /// **The twist axis also weighs how easily each CORE turns about the axis** — `FUN_18003d320`, the
    /// accumulator `FUN_1800393d0` hands a purely angular row, adds `a_k² · invInertia_k` for each body
    /// (`docs/findings/51`, *The joint's twist axis*). The anchor term alone picks Z here: the parent's
    /// anchor is `(3, 4, 0)` at an inverse mass of a tenth, scoring `(1.6, 0.9, 2.5)`. But the child's hull is
    /// thin about Y — `(1, 0.001, 1)` per kilogram at mass 2, floored to `0.283` about Y by a tenth of its
    /// length — so its inverse inertia about Y is `3.54` against `0.5` about the others, and the parent adds
    /// `0.1` on every axis. The totals are about `(2.2, 4.54, 3.1)`: **Y takes the twist**, and with it Y's
    /// declared 45°. A rule without the inertia term picks Z and its 20°.
    /// </remarks>
    [Test]
    public void Create_AJointWhoseChildTurnsEasilyAboutOneAxis_GivesThatAxisTheTwist()
    {
        const float Squared = 39.37f * 39.37f;

        RagdollConstraint constraint = new(
            0,
            1,
            new ConstraintAxis(-5f, 5f, 0f),
            new ConstraintAxis(-45f, 45f, 0f),
            new ConstraintAxis(-20f, 20f, 0f));

        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [constraint],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties:
            [
                new PhysicsMassProperties(Vector3.Zero, new Vector3(1f, 1f, 1f) / Squared),

                // Source (1, 0.001, 1) per kilogram: about IVP's x, y and z that is (1, 1, 0.001).
                new PhysicsMassProperties(Vector3.Zero, new Vector3(1f, 1f, 0.001f) / Squared),
            ]);

        RagdollSimulation simulation = RagdollSimulation.Create(RagdollBody.Build(physics, Skeleton())!, Step, Start());

        IvpRagdollConstraint joint = simulation.Environment.Constraints.Joints[0].Constraint;

        const float Radian = 0.017453292f;

        joint.Twist.Lower.ShouldBe(-45f * Radian, Tolerance, "the Y axis, which the child turns about most easily");
        joint.Twist.Upper.ShouldBe(45f * Radian, Tolerance);
    }

    /// <remarks>
    /// **Both anchors count, measured from each CORE, and their lever arms are SQUARED** — `FUN_1800393d0`
    /// scores `invMass_A · |r_A × a|² + … + invMass_B · |r_B × a|²`. The child's mass center is `(−3, −1, −2)` and
    /// the parent's `(3, 1, 0)`, so in core space the anchors are `(3, 1, 2)` and `(0, 3, 0)`. With inverse
    /// masses of a half and a tenth and the same inertia on every axis:
    ///
    /// | axis | child | parent | total |
    /// |---|---|---|---|
    /// | x | 0.5 × 5 | 0.1 × 9 | 3.4 |
    /// | y | 0.5 × 13 | 0 | **6.5** |
    /// | z | 0.5 × 10 | 0.1 × 9 | 5.9 |
    ///
    /// **Y takes the twist and its 45°.** Found by search so that every wrong rule lands elsewhere: unsquared arms
    /// pick Z, the parent's anchor alone picks X, and the old single anchor `(3, 4, 0)` picks Z.
    /// </remarks>
    [Test]
    public void Create_AJointWithBothAnchorsOffTheBone_ScoresTheirSquaredArmsFromEachCore()
    {
        const float Squared = 39.37f * 39.37f;

        RagdollConstraint constraint = new(
            0,
            1,
            new ConstraintAxis(-5f, 5f, 0f),
            new ConstraintAxis(-45f, 45f, 0f),
            new ConstraintAxis(-20f, 20f, 0f));

        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [constraint],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties:
            [
                // Source (3, 1, 0) is IVP (3, 0, −1)... inverted: IVP (x, y, z) = Source (x, −z, y) / 39.37.
                new PhysicsMassProperties(new Vector3(3f, 0f, 1f) * Metre, new Vector3(1f, 1f, 1f) / Squared),

                // Source (−3, −1, −2) is IVP (−3, 2, −1) / 39.37.
                new PhysicsMassProperties(new Vector3(-3f, 2f, -1f) * Metre, new Vector3(1f, 1f, 1f) / Squared),
            ]);

        RagdollSimulation simulation = RagdollSimulation.Create(RagdollBody.Build(physics, Skeleton())!, Step, Start());

        IvpRagdollJoint joint = simulation.Environment.Constraints.Joints[0];

        // The anchors this score is taken over, so a wrong answer below cannot be blamed on the fixture.
        joint.AnchorA.X.ShouldBe(3f, 1e-3f);
        joint.AnchorA.Z.ShouldBe(2f, 1e-3f);
        joint.AnchorB.Y.ShouldBe(3f, 1e-3f);

        const float Radian = 0.017453292f;

        joint.Constraint.Twist.Lower.ShouldBe(-45f * Radian, Tolerance, "the Y axis, by the squared arms");
    }

    /// <summary>The two-solid ragdoll whose surfaces carry a mass center and inertia, in IVP metres.</summary>
    /// <remarks>
    /// Chosen to land on round Source numbers: the child's mass center is Source `(1, 0, 0)`, which is IVP
    /// `(0.0254, 0, 0)`; the parent's is Source `(0, 2, 0)`, IVP `(0, 0, 0.0508)`, since Source Y is IVP Z. The
    /// child's hull inertia is Source `(1, 2, 3)` per kilogram, which about IVP's axes is `(1, 3, 2) / 39.37²`.
    /// </remarks>
    private static PhysicsModel WithMassProperties()
    {
        const float Squared = 39.37f * 39.37f;

        return PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, Axis, Axis, Axis)],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties:
            [
                new PhysicsMassProperties(new Vector3(0f, 0f, 2f * Metre), new Vector3(1f, 1f, 1f) / Squared),
                new PhysicsMassProperties(new Vector3(Metre, 0f, 0f), new Vector3(1f, 3f, 2f) / Squared),
            ]);
    }

    /// <summary>The bind positions of <see cref="Start"/>, with the child turned a quarter about Z.</summary>
    private static (Vector3, Quaternion)[] TurnedChild() =>
        [
            (Vector3.Zero, Quaternion.Identity),
            (new Vector3(3f, 4f, 0f), new Quaternion(0f, 0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f))),
        ];

    private static float Speed(IvpRigidBody body) =>
        MathF.Sqrt(
            (body.Velocity.X * body.Velocity.X) +
            (body.Velocity.Y * body.Velocity.Y) +
            (body.Velocity.Z * body.Velocity.Z));

    /// <summary>The two-solid ragdoll with real collision hulls on both bodies.</summary>
    /// <remarks>
    /// **Authored in IVP metres**, because that is what a `.phy` holds and what
    /// <c>RagdollBody.HullInBoneSpace</c> converts from — a fixture written in Source units would
    /// describe a file that does not exist and would be 39 times too big.
    /// </remarks>
    private static PhysicsModel Solid()
    {
        const float Half = 4f * Metre;

        List<Vector3> points =
        [
            new(-Half, -Half, -Half), new(Half, -Half, -Half),
            new(Half, Half, -Half), new(-Half, Half, -Half),
            new(-Half, -Half, Half), new(Half, -Half, Half),
            new(Half, Half, Half), new(-Half, Half, Half),
        ];

        List<(int A, int B, int C)> triangles =
        [
            (4, 5, 6), (4, 6, 7), (0, 2, 1), (0, 3, 2),
            (0, 1, 5), (0, 5, 4), (2, 3, 7), (2, 7, 6),
            (1, 2, 6), (1, 6, 5), (3, 0, 4), (3, 4, 7),
        ];

        List<PhysicsLedge> hull = [new PhysicsLedge(points, triangles, new (int, int, int)[triangles.Count], Vector3.Zero, Half * 2f)];

        return PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, Axis, Axis, Axis)],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: [hull, hull],
            massProperties: RagdollMasses.Uniform(2));
    }

    private const float Metre = 0.0254f;

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
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties: RagdollMasses.Uniform(2));

    /// <summary>Two bones at chosen bind positions — <see cref="RagdollSkeletons.Straight"/>.</summary>
    private static IReadOnlyList<StudioBone> Skeleton() => RagdollSkeletons.Straight();
}
