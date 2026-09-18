using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A ragdoll on the ported driver, in IVP space, crossing into and out of Source at its boundary (B369, D172).</summary>
/// <remarks>
/// **The conversions are arithmetic** — `Source (x, y, z)` is `IVP (x, −z, y) × 0.0254` — so each prediction below is a number the
/// test chose, carried across by hand. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpRagdollTests
{
    private const float Metre = IvpTransform.MetresPerInch;

    /// <remarks>
    /// **The core stands at the mass centre in IVP space**: the child starts at Source `(3, 4, 0)` turned a quarter about Source Z,
    /// its mass centre one inch along its own X, which the turn sends along Source Y — the core at Source `(3, 5, 0)`, IVP
    /// `(3, 0, 5) × 0.0254`.
    /// </remarks>
    [Test]
    public void Create_AnElementWithAMassCenter_PlacesItsCoreThereInIvpSpace()
    {
        IvpRagdoll ragdoll = Ragdoll(out _, out IvpSimulation simulation, TurnedChild());

        // Asleep until the first PSI revives it (`FUN_180089210`), so the unit is looked for on both lists.
        IvpRigidBody child = simulation.Units.Sleeping.Concat(simulation.Units.Active).Single().Cores.Find(core => core.ObjectOffset.X < -0.5f * Metre)!;

        child.Position.X.ShouldBe(3d * Metre, 1e-5d);
        child.Position.Y.ShouldBe(0d, 1e-5d);
        child.Position.Z.ShouldBe(5d * Metre, 1e-5d);
        ragdoll.Body.Elements.Count.ShouldBe(2);
    }

    /// <remarks>**What comes back is the bone in Source**, the start pose exactly, the turn included.</remarks>
    [Test]
    public void State_BeforeAnyStep_ReportsEachBoneWhereItStartedInSource()
    {
        (Vector3, Quaternion)[] start = TurnedChild();
        IvpRagdoll ragdoll = Ragdoll(out _, out _, start);

        (Vector3 Position, Quaternion Orientation) child = ragdoll.State()[1];

        child.Position.X.ShouldBe(3f, 1e-4f);
        child.Position.Y.ShouldBe(4f, 1e-4f);
        child.Position.Z.ShouldBe(0f, 1e-4f);
        Quaternion.Dot(child.Orientation, start[1].Item2).ShouldBe(1f, 1e-5f);
    }

    /// <remarks>
    /// **The bone is reported at the environment's clock** (`GetPosition` reads `FUN_180073b80`): the first frame runs two PSIs,
    /// the second at the step, and leaves the clock `0.9999895` of a step past it, so the root has moved on by its committed
    /// velocity for that long — not where its core was last stepped.
    /// </remarks>
    [Test]
    public void State_AfterAFrame_IsTheBoneAtTheClock()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out IvpSimulation simulation, Straight());
        IvpRigidBody root = ragdoll.Bodies[0];

        world.Simulate(IvpRagdollWorldFrames.Step);

        float elapsed = (float)(simulation.Now - root.LastStepped);
        elapsed.ShouldBeGreaterThan(0.9f * IvpRagdollWorldFrames.Step, "the control: the clock is well past the last PSI");
        root.PreviousVelocity.Y.ShouldNotBe(0f, "the control: the root is falling");
        (_, _, float z) = IvpTransform.SourcePosition(0f, (float)(root.ObjectOrigin().Y + ((double)root.PreviousVelocity.Y * elapsed)), 0f);
        ragdoll.State()[0].Position.Z.ShouldBe(z, 1e-4f);
    }

    /// <remarks>**Source gravity is IVP +Y**, so a corpse falls along Source −Z and nowhere else.</remarks>
    [Test]
    public void Simulate_UnderGravity_TheRootFallsAlongSourceMinusZ()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out _, Straight());

        world.SimulateFrames(0.5d);

        Vector3 root = ragdoll.State()[0].Position;
        root.Z.ShouldBeLessThan(-50f, "half a second at 800 in/s² is about a hundred inches");
        root.X.ShouldBe(0f, 1f);
        root.Y.ShouldBe(0f, 1f);
    }

    /// <remarks>**The killing force crosses too**: an impulse along Source +X moves the struck bone along Source +X.</remarks>
    [Test]
    public void Kill_AForceAlongSourceX_PushesTheStruckBoneAlongIt()
    {
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out _, Straight());

        ragdoll.Kill(new Vector3(2000f, 0f, 0f), forceBone: 0);
        world.SimulateFrames(0.2d);

        ragdoll.State()[0].Position.X.ShouldBeGreaterThan(5f);
    }

    /// <remarks>
    /// **The joint holds the two bones together**: started with the child an inch off its bind offset, a second under the
    /// ported constraints brings it back nearer.
    /// </remarks>
    [Test]
    public void Simulate_AJointPulledApart_BringsTheBonesCloser()
    {
        (Vector3, Quaternion)[] start = [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 1f), Quaternion.Identity)];
        IvpRagdoll ragdoll = Ragdoll(out IvpRagdollWorld world, out IvpSimulation simulation, start);
        float before = Separation(ragdoll);

        world.SimulateFrames(1d);

        Separation(ragdoll).ShouldBeLessThan(
            before * 0.5f,
            $"units {simulation.AwakeUnits}, cores in first {simulation.Units.Active[0].Cores.Count}, entries " +
            $"{string.Join(",", simulation.Units.Active[0].Entries.ConvertAll(e => e.Controller.Priority))}; root {ragdoll.State()[0].Position}, child {ragdoll.State()[1].Position}");
    }

    // ---- Ported from RagdollDeathForceConformanceTests.cs (B58, D146) and RagdollSimulationConformanceTests.cs
    // (B58, D142, D146), against IvpRagdoll rather than the deleted RagdollSimulation/IvpEnvironment. See each
    // test's remarks for what changed crossing the seam and why the predicted numbers still hold.

    /// <summary>One PSI's worth of Source gravity, in in/s — what every Kill/Inherit prediction below has to carry.</summary>
    private const float Gravity = 800f * Step;

    private const float Step = 1f / 66f;

    private const float Tolerance = 1e-4f;

    /// <summary>A body's IVP velocity, read back into Source in/s the same way <see cref="IvpTransform.SourcePosition"/> reads a
    /// position — valid because that map is linear (axes and a uniform scale, no translation), so it carries a velocity exactly
    /// as it carries a point.</summary>
    private static Vector3 SourceVelocity(IvpRigidBody body)
    {
        (float x, float y, float z) = IvpTransform.SourcePosition(body.Velocity.X, body.Velocity.Y, body.Velocity.Z);

        return new Vector3(x, y, z);
    }

    /// <remarks>
    /// **Ported from <c>RagdollDeathForceConformanceTests.Kill_OnTheStruckBone_AddsTheWholeForceOverItsMass</c>.**
    /// <see cref="IvpRagdoll.Kill"/> converts the force through <see cref="IvpTransform.Position"/> and
    /// <see cref="IvpPush.ApplyForceCenter"/> stages it exactly as the deleted engine's did, so the same "whole force
    /// over its mass, less one step of gravity" prediction survives — read back with <see cref="SourceVelocity"/>,
    /// whose conversion is the exact inverse of <c>Kill</c>'s, so the numbers are unchanged from the old test.
    /// **One PSI, not one <c>Step()</c>**: the ported driver's first PSI is due at the world's own time zero, and
    /// `Advance_ABodyUnderGravity_MovesOnlyOnTheSecondStep` (<c>IvpSimulationTests</c>) already establishes that a PSI
    /// at time zero both drains a staged push and applies gravity in the same tick — the same "damping, flush,
    /// gravity" order the old engine had. Advancing to a target inside the first step, before the second PSI at
    /// <c>Step</c>, isolates exactly that one tick.
    /// </remarks>
    [Test]
    public void Kill_OnTheStruckBone_AddsTheWholeForceOverItsMass()
    {
        IvpRagdoll ragdoll = KillRagdoll(out _, out IvpSimulation simulation);

        ragdoll.Kill(new Vector3(0f, 0f, 1000f), forceBone: 0);

        // Staged, so nothing has moved yet — the control on the staging itself.
        SourceVelocity(ragdoll.Bodies[0]).Z.ShouldBe(0f);

        simulation.Advance(Step * 0.5d);

        SourceVelocity(ragdoll.Bodies[0]).Z.ShouldBe(100f - Gravity, 1e-2f);
    }

    /// <remarks>
    /// **Ported from <c>Kill_OnEveryOtherBody_SharesTheForceByMassAtTheStruckPosition</c>.** The mass-weighted share
    /// and the offset push are <see cref="IvpRagdoll.Kill"/>'s own arithmetic, carried unchanged from the deleted
    /// engine, so the numeric share (a sixth of the force, over two kilos) is unchanged too.
    ///
    /// **The spin check no longer names two axes.** <c>AngularVelocity</c> is in IVP's own axes, which are a
    /// permutation of Source's, so whichever axis the old test excluded (Z, parallel to the force) is not
    /// necessarily excluded here — asserting the total is nonzero is the permutation-independent form of the same
    /// claim: an offset push spins the body and a centre push would not.
    /// </remarks>
    [Test]
    public void Kill_OnEveryOtherBody_SharesTheForceByMassAtTheStruckPosition()
    {
        IvpRagdoll ragdoll = KillRagdoll(out _, out IvpSimulation simulation);

        ragdoll.Kill(new Vector3(0f, 0f, 1200f), forceBone: 0);
        simulation.Advance(Step * 0.5d);

        // share = 2 / 12; impulse = 200; over 2 kg that is 100 units a second, less one step of gravity.
        SourceVelocity(ragdoll.Bodies[1]).Z.ShouldBe(100f - Gravity, 1e-2f);

        (float X, float Y, float Z) spin = ragdoll.Bodies[1].AngularVelocity;
        MathF.Sqrt((spin.X * spin.X) + (spin.Y * spin.Y) + (spin.Z * spin.Z)).ShouldBeGreaterThan(
            0f, "an offset push spins the body; a centre push would leave this at zero");
    }

    /// <remarks>**Ported from <c>Kill_WithNoForceBone_PushesNothing</c>.** Unchanged reasoning: no force bone means
    /// no <c>forcePosition</c>, so the second loop in <see cref="IvpRagdoll.Kill"/> never runs.</remarks>
    [Test]
    public void Kill_WithNoForceBone_PushesNothing()
    {
        IvpRagdoll ragdoll = KillRagdoll(out _, out IvpSimulation simulation);

        ragdoll.Kill(new Vector3(0f, 0f, 1000f), forceBone: -1);
        simulation.Advance(Step * 0.5d);

        // Gravity has run, so the control is that nothing went UP.
        SourceVelocity(ragdoll.Bodies[0]).Z.ShouldBeLessThan(0f);
        SourceVelocity(ragdoll.Bodies[1]).Z.ShouldBeLessThan(0f);
    }

    /// <remarks>**Ported from <c>Inherit_WithBodiesOfDifferentMass_GivesThemTheSameVelocity</c>.**
    /// <see cref="IvpRagdoll.Inherit"/> converts the velocity through <see cref="IvpTransform.Position"/> and calls
    /// <see cref="IvpPush.AddVelocity"/> directly — mass-independent exactly as before, and the round trip through
    /// <see cref="IvpTransform.Position"/> then <see cref="SourceVelocity"/> is its own inverse, so 200 in/s stays
    /// 200 in/s.</remarks>
    [Test]
    public void Inherit_WithBodiesOfDifferentMass_GivesThemTheSameVelocity()
    {
        IvpRagdoll ragdoll = KillRagdoll(out _, out IvpSimulation simulation);

        ragdoll.Inherit(new Vector3(200f, 0f, 0f));
        simulation.Advance(Step * 0.5d);

        SourceVelocity(ragdoll.Bodies[0]).X.ShouldBe(200f, 1e-2f);
        SourceVelocity(ragdoll.Bodies[1]).X.ShouldBe(200f, 1e-2f);
    }

    /// <summary>A two-body ragdoll, ten kilos and two, with no joint and no world to land on — the Kill/Inherit fixture.</summary>
    private static IvpRagdoll KillRagdoll(out IvpRagdollWorld world, out IvpSimulation simulation)
    {
        world = new IvpRagdollWorld(Step, new Vector3(0f, 0f, -800f), new VphysicsSurfaceProps([]));
        simulation = world.Simulation;

        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties: RagdollMasses.Uniform(2));

        RagdollBody body = RagdollBody.Build(physics, RagdollSkeletons.Straight())!;

        List<(Vector3 Position, Quaternion Orientation)> start =
        [
            (new Vector3(0f, 0f, 0f), Quaternion.Identity),

            // Offset sideways as well as up (B58): with the child directly above the struck bone the lever arm is
            // parallel to the force and the cross product is zero, so an offset push and a centre push would read
            // the same spin of none.
            (new Vector3(10f, 0f, 20f), Quaternion.Identity),
        ];

        return IvpRagdoll.Create(world, body, start);
    }

    /// <remarks>
    /// **Ported from <c>Create_WithAStateThatDoesNotMatchTheElements_Refuses</c>.** Unchanged: <see cref="IvpRagdoll.Create"/>
    /// throws the same way <c>RagdollSimulation.Create</c> did, for the same reason — a `.phy` is a stranger's file and a
    /// mismatched state would silently simulate a different skeleton.
    /// </remarks>
    [Test]
    public void Create_WithAStateThatDoesNotMatchTheElements_Refuses()
    {
        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));
        RagdollBody ragdoll = RagdollBody.Build(Physics(), RagdollSkeletons.Straight())!;

        Should.Throw<ArgumentException>(
            () => IvpRagdoll.Create(world, ragdoll, [(Vector3.Zero, Quaternion.Identity)]));
    }

    /// <remarks>
    /// **Ported from <c>Create_ForAConstraint_MakesTheChildTheReferenceBody</c>.** Unchanged: <see cref="IvpRagdollWorld"/>'s
    /// <c>Create</c> builds the joint from <c>constraint.Child</c> as <c>BodyA</c> exactly as the deleted engine did — see the
    /// remarks on <see cref="IvpRagdollJoint.AnchorA"/> for the citation. Reads the joint through the <see cref="IvpRagdoll.Joints"/>
    /// accessor added for this port (the group used to be a local the caller never saw again).
    /// </remarks>
    [Test]
    public void Create_ForAConstraint_MakesTheChildTheReferenceBody()
    {
        IvpRagdoll ragdoll = Ragdoll(out _, out _, Straight());

        IvpRagdollJoint joint = ragdoll.Joints!.Joints[0];

        joint.BodyA.ShouldBeSameAs(ragdoll.Bodies[1], "the child is the reference");
        joint.BodyB.ShouldBeSameAs(ragdoll.Bodies[0], "the parent is the attached");
    }

    /// <remarks>
    /// **Ported from <c>Create_WithAConstraintJoiningABodyToItself_MakesTheBodiesAndNoJoint</c>.** Unchanged: both
    /// engines null a constraint whose child and parent are the same element (`ragdoll_shared.cpp:217`), so the
    /// bodies are still built and no joint reaches the group.
    /// </remarks>
    [Test]
    public void Create_WithAConstraintJoiningABodyToItself_MakesTheBodiesAndNoJoint()
    {
        RagdollConstraint bogus = new(1, 1, JointAxis, JointAxis, JointAxis);
        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));

        IvpRagdoll ragdoll = IvpRagdoll.Create(world, RagdollBody.Build(PhysicsWith(bogus), RagdollSkeletons.Straight())!, Straight());

        ragdoll.Bodies.Count.ShouldBe(2);
        ragdoll.Joints!.Joints.Count.ShouldBe(0);
    }

    /// <remarks>
    /// **Ported from <c>Create_WithThreeUnequalAxisRanges_GivesTheTwistTheAxisItTurnsAbout</c> (B306).**
    /// <see cref="RagdollJointAxes.Turning"/> is the same mechanical rule the deleted engine used, scored over anchors
    /// and axes that have all been carried through the SAME orthogonal change of basis (<c>P·A·Pᵀ</c>, axes permuted
    /// and one negated, positions and arms scaled uniformly) — a rotation-with-reflection changes neither a cross
    /// product's length nor which candidate scores highest, so the winning axis is still the one the file declares
    /// ±20° for, and the predicted RADIAN range is unchanged from the old test even though it now lives at a
    /// different IVP slot internally. Traced by hand for this fixture: the bind offset (3, 4, 0) crosses into IVP as
    /// (3, 0, 4) × <see cref="Metre"/>, and scoring that arm against the Y axis gives the largest
    /// <c>|anchor × axis|²</c> of the three candidates — the same physical axis the old, Source-space arithmetic
    /// picked, just relabelled.
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

        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));
        IvpRagdoll ragdoll = IvpRagdoll.Create(world, RagdollBody.Build(PhysicsWith(constraint), RagdollSkeletons.Straight())!, Straight());

        IvpRagdollJoint joint = ragdoll.Joints!.Joints[0];

        const float Radian = 0.017453292f;

        joint.Constraint.Twist.Lower.ShouldBe(-20f * Radian, Tolerance, "the axis the anchor turns about, unchanged by the IVP permutation");
        joint.Constraint.Twist.Upper.ShouldBe(20f * Radian, Tolerance);
    }

    /// <remarks>
    /// **Ported from <c>Create_AnElementWithHullInertia_TakesThePerAxisInertiaOfItsTemplate</c> (B403).**
    /// <see cref="RagdollElement.IvpHullInertia"/> is read directly by <see cref="IvpRagdollWorld"/>'s <c>Create</c> —
    /// no axis swap and no Source conversion, unlike the deleted engine's <c>HullInertia</c> — so the prediction is
    /// taken straight from the fixture's raw IVP numbers, (1, 3, 2) per kilogram at mass 2: (2, 6, 4), scaled back up
    /// by <c>Squared</c> for a readable assertion. The floor (`0.1 × length`) is about 0.075 × <c>Squared</c>⁻¹,
    /// under every axis, so it changes nothing.
    /// </remarks>
    [Test]
    public void Create_AnElementWithHullInertia_TakesThePerAxisInertiaOfItsTemplate()
    {
        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));
        IvpRagdoll ragdoll = IvpRagdoll.Create(world, RagdollBody.Build(WithMassProperties(), RagdollSkeletons.Straight())!, TurnedChild());

        IvpRigidBody child = ragdoll.Bodies[1];
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

        (child.Inertia.X * Squared).ShouldBe(2f, 1e-3f);
        (child.Inertia.Y * Squared).ShouldBe(6f, 1e-3f);
        (child.Inertia.Z * Squared).ShouldBe(4f, 1e-3f);
        (child.InverseInertia.Z * child.Inertia.Z).ShouldBe(1f, 1e-4f, "reciprocals, whatever axis they land on");
        child.InverseMass.ShouldBe(0.5f, 1e-6f, "mass has no unit crossing to make, so this is unchanged from the old test");
    }

    /// <remarks>
    /// **Ported from <c>Create_AJoint_AnchorsBothEndsInCoreSpace</c> (B403).** <see cref="IvpRagdollJoint.AnchorA"/>
    /// and <see cref="IvpRagdollJoint.AnchorB"/> are stored in IVP metres and axes now, not Source inches — so the
    /// old prediction (Source `(-1, 0, ?)` and `(3, 2, ?)`) is re-derived here from the same raw fixture: the
    /// child's own offset is <c>-IvpMassCenter</c> = <c>-(Metre, 0, 0)</c>, and the parent's anchor is the bind
    /// offset <c>(3, 4, 0)</c> converted through <see cref="IvpTransform.Position"/> — <c>(3, 0, 4) × Metre</c> —
    /// plus the parent's own offset, <c>-(0, 0, 2 × Metre)</c>: <c>(3, 0, 2) × Metre</c>. Source Y crossed into IVP
    /// Z, which is why the assertions read <c>AnchorB.Z</c> where the old test read <c>AnchorB.Y</c>.
    /// </remarks>
    [Test]
    public void Create_AJoint_AnchorsBothEndsInCoreSpace()
    {
        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));
        IvpRagdoll ragdoll = IvpRagdoll.Create(world, RagdollBody.Build(WithMassProperties(), RagdollSkeletons.Straight())!, TurnedChild());

        IvpRagdollJoint joint = ragdoll.Joints!.Joints[0];

        joint.AnchorA.X.ShouldBe(-Metre, 1e-4f);
        joint.AnchorA.Y.ShouldBe(0f, 1e-4f);
        joint.AnchorB.X.ShouldBe(3f * Metre, 1e-4f);
        joint.AnchorB.Z.ShouldBe(2f * Metre, 1e-4f, "Source Y, which crosses into IVP Z");
    }

    /// <remarks>
    /// **Ported from <c>Create_AJointWhoseChildTurnsEasilyAboutOneAxis_GivesThatAxisTheTwist</c> (B403,
    /// `docs/findings/51`).** The fixture's raw hull inertia — root isotropic, child thin about its OWN raw Y —
    /// crosses the P permutation exactly as the anchors and axes do, so the same physical axis (thin about the
    /// child's inertia, matching one end of the 45° range) still wins and the predicted radian range is unchanged
    /// from the old test.
    /// </remarks>
    [Test]
    public void Create_AJointWhoseChildTurnsEasilyAboutOneAxis_GivesThatAxisTheTwist()
    {
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

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

                // Raw IVP, per the fixture's own comment in the deleted test: about Source (1, 0.001, 1) per kilogram.
                new PhysicsMassProperties(Vector3.Zero, new Vector3(1f, 1f, 0.001f) / Squared),
            ]);

        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));
        IvpRagdoll ragdoll = IvpRagdoll.Create(world, RagdollBody.Build(physics, RagdollSkeletons.Straight())!, Straight());

        IvpRagdollJoint joint = ragdoll.Joints!.Joints[0];

        const float Radian = 0.017453292f;

        joint.Constraint.Twist.Lower.ShouldBe(-45f * Radian, Tolerance, "the axis the child turns easily about, unchanged by the IVP permutation");
        joint.Constraint.Twist.Upper.ShouldBe(45f * Radian, Tolerance);
    }

    /// <remarks>
    /// **Ported from <c>Create_AJointWithBothAnchorsOffTheBone_ScoresTheirSquaredArmsFromEachCore</c> (B403).** The
    /// anchors are re-derived from the same raw fixture directly, since they are IVP quantities now: the child's
    /// mass centre is the raw <c>(-3, 2, -1) × Metre</c>, so its own anchor is <c>(3, -2, 1) × Metre</c>; the
    /// parent's is the bind offset <c>(3, 4, 0)</c> through <see cref="IvpTransform.Position"/> — <c>(3, 0, 4) × Metre</c>
    /// — plus its own offset <c>-(3, 0, 1) × Metre</c>, giving <c>(0, 0, 3) × Metre</c>. The winning axis and its
    /// 45° range are unchanged by the same permutation argument as the test above.
    /// </remarks>
    [Test]
    public void Create_AJointWithBothAnchorsOffTheBone_ScoresTheirSquaredArmsFromEachCore()
    {
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

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
                new PhysicsMassProperties(new Vector3(3f, 0f, 1f) * Metre, new Vector3(1f, 1f, 1f) / Squared),
                new PhysicsMassProperties(new Vector3(-3f, 2f, -1f) * Metre, new Vector3(1f, 1f, 1f) / Squared),
            ]);

        IvpRagdollWorld world = new(Step, Vector3.Zero, new VphysicsSurfaceProps([]));
        IvpRagdoll ragdoll = IvpRagdoll.Create(world, RagdollBody.Build(physics, RagdollSkeletons.Straight())!, Straight());

        IvpRagdollJoint joint = ragdoll.Joints!.Joints[0];

        // The anchors this score is taken over, so a wrong answer below cannot be blamed on the fixture.
        joint.AnchorA.X.ShouldBe(3f * Metre, 1e-3f);
        joint.AnchorA.Z.ShouldBe(1f * Metre, 1e-3f);
        joint.AnchorB.Z.ShouldBe(3f * Metre, 1e-3f);

        const float Radian = 0.017453292f;

        joint.Constraint.Twist.Lower.ShouldBe(-45f * Radian, Tolerance, "the axis with the largest squared arms, unchanged by the IVP permutation");
        joint.Constraint.Twist.Upper.ShouldBe(45f * Radian, Tolerance);
    }

    /// <summary>The two-solid ragdoll whose surfaces carry a mass centre and inertia, in IVP metres — for the anchor and inertia tests.</summary>
    private static PhysicsModel WithMassProperties()
    {
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

        return PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, JointAxis, JointAxis, JointAxis)],
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

    private static readonly ConstraintAxis JointAxis = new(-30f, 30f, 0f);

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

    private static float Separation(IvpRagdoll ragdoll)
    {
        (Vector3 Position, Quaternion Orientation)[] state = ragdoll.State();

        return (state[1].Position - (state[0].Position + Vector3.Transform(new Vector3(3f, 4f, 0f), state[0].Orientation))).Length();
    }

    private static IvpRagdoll Ragdoll(out IvpRagdollWorld world, out IvpSimulation simulation, (Vector3, Quaternion)[] start)
    {
        world = new IvpRagdollWorld(1f / 66f, new Vector3(0f, 0f, -800f), new VphysicsSurfaceProps([]));
        simulation = world.Simulation;

        return IvpRagdoll.Create(world, RagdollBody.Build(Physics(), RagdollSkeletons.Straight())!, start);
    }

    private static (Vector3, Quaternion)[] Straight() =>
        [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 0f), Quaternion.Identity)];

    private static (Vector3, Quaternion)[] TurnedChild() =>
        [(Vector3.Zero, Quaternion.Identity), (new Vector3(3f, 4f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f))];

    /// <summary>A root and a child with a joint of ±30° on each axis, the child's mass centre one inch along its own X.</summary>
    private static PhysicsModel Physics()
    {
        ConstraintAxis axis = new(-30f, 30f, 0f);
        const float Squared = IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre;

        return PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, axis, axis, axis)],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: null,
            massProperties:
            [
                new PhysicsMassProperties(Vector3.Zero, new Vector3(1f, 1f, 1f) / Squared),
                new PhysicsMassProperties(new Vector3(Metre, 0f, 0f), new Vector3(1f, 1f, 1f) / Squared),
            ]);
    }
}
