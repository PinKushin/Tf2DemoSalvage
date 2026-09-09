using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Building a ragdoll from a model's <c>.phy</c> — <c>RagdollCreateObjects</c> (B58).
/// </summary>
/// <remarks>
/// **`ragdoll_shared.cpp:264` and the two helpers it calls**, transcribed. `RagdollAddSolid`
/// (`:177`) maps each solid to a bone BY NAME and asserts that the running count equals the solid's
/// own index; `RagdollAddConstraint` (`:215`) records the child's parent and the child bone's origin
/// in the parent's space:
///
/// <code>
///   int boneIndex = Studio_BoneIndexByName( params.pStudioHdr, solid.name );
///   ragdoll.boneIndex[ragdoll.listCount] = boneIndex;
///   ...
///   childElement.parentIndex = constraint.parentIndex;
///   Studio_CalcBoneToBoneTransform( params.pStudioHdr,
///       ragdoll.boneIndex[constraint.childIndex],
///       ragdoll.boneIndex[constraint.parentIndex],
///       constraint.constraintToAttached );
///   MatrixGetColumn( constraint.constraintToAttached, 3, childElement.originParentSpace );
/// </code>
///
/// **Synthetic, because a synthetic fixture has ground truth** (D38). A two-bone skeleton whose
/// bind positions this test chose gives a predicted offset that a real `.phy` could only be
/// compared against a second reading of itself.
///
/// **What is NOT covered here and is not covered anywhere: the solver.** `src/vphysics` is not in
/// the SDK — only its headers — so the integrator behind `CreatePolyObject` and
/// `CreateRagdollConstraint` is closed. D142 records the boundary. Everything asserted below is
/// construction, which IS published.
/// </remarks>
public sealed class RagdollBodyConformanceTests
{
    private const double Tolerance = 1e-5;

    /// <remarks>
    /// **A solid's hull is in its own BODY's space, not the model's, and this is the input that
    /// tells those apart.** The child bone is bound at `(4, 6, 3)`, so a hull authored at the origin
    /// stays at the origin under the right reading and moves to the bind offset under the wrong
    /// one. A skeleton bound at the origin would predict the same thing either way, which is the
    /// condition that let this ship.
    ///
    /// **The engine says so twice.** `CreatePolyObject( params.pCollide-&gt;solids[solid.index],
    /// surfaceData, vec3_origin, vec3_angle, &amp;solid.params )` (`ragdoll_shared.cpp:200`) creates
    /// every solid at the origin with no rotation and only then places the body at its bone — a
    /// model-space hull would put every limb at the model origin.
    ///
    /// **And the files say so.** A medic's twenty-four hulls have bounding-sphere centres within
    /// ten units of zero; in model space a head's would sit some seventy units up.
    ///
    /// **What the wrong reading looked like**: corpses on `koth_harvest_final` reporting contacts
    /// nineteen thousand units below the floor, because every limb's collision was displaced by its
    /// own bone's bind offset and hit geometry that was not there.
    /// </remarks>
    [Test]
    public void Build_WithABoneBoundAwayFromTheOrigin_LeavesTheHullInBodySpace()
    {
        PhysicsLedge ledge = new(
            [new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0.1f, 0f)],
            [(0, 1, 2)],
            Vector3.Zero,
            0.1f);

        PhysicsModel physics = PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [],
            2,
            checksum: 0,
            collisionRules: null,
            hulls: [[ledge], [ledge]]);

        RagdollBody body = RagdollBody.Build(physics, Skeleton())!;

        // The child's hull, whose bone is bound at (4, 6, 3). Its first point was authored at the
        // origin, so under the right reading it is still there — converted from metres and nothing
        // else. Under the wrong one it sits at the bind offset instead.
        Vector3 first = body.Elements[1].Hull[0];

        first.X.ShouldBe(0f, 1e-3f);
        first.Y.ShouldBe(0f, 1e-3f);
        first.Z.ShouldBe(0f, 1e-3f);

        // The control: the hull is really being read and converted, so a zero here is not the
        // answer an empty hull would also give.
        body.Elements[1].Hull.Count.ShouldBe(3);
        body.Elements[1].Hull[1].X.ShouldBe(0.1f / 0.0254f, 1e-2f, "metres became Source units");
    }

    /// <remarks>
    /// **Solids map to bones by NAME, and the element order is the file's order.** A `.phy`'s
    /// constraints reference solids by index, so an element list in any other order silently
    /// rewires the skeleton.
    /// </remarks>
    [Test]
    public void Build_ASolidPerBone_MapsThemByNameInFileOrder()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        body.Elements.Count.ShouldBe(2);
        body.Elements[0].BoneIndex.ShouldBe(0);
        body.Elements[1].BoneIndex.ShouldBe(1);
    }

    /// <remarks>
    /// **`Studio_CalcBoneToBoneTransform( hdr, child, parent, out )` is
    /// `poseToBone[parent] × inverse(poseToBone[child])`** (`bone_setup.cpp:1770`), so its
    /// translation column is the CHILD bone's origin expressed in the PARENT's space.
    ///
    /// The fixture puts the root at `(1, 2, 3)` and the child at `(4, 6, 3)`, so the answer is
    /// `(3, 4, 0)` — and a transcription with the two bones the wrong way round produces
    /// `(−3, −4, 0)`, which is why the offsets are asymmetric and non-zero in two axes.
    /// </remarks>
    [Test]
    public void Build_AConstraint_RecordsTheChildOriginInItsParentsSpace()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        body.Elements[1].ParentIndex.ShouldBe(0);

        body.Elements[1].OriginParentSpace.X.ShouldBe(3f, Tolerance);
        body.Elements[1].OriginParentSpace.Y.ShouldBe(4f, Tolerance);
        body.Elements[1].OriginParentSpace.Z.ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The engine keeps the whole matrix, and the ROTATION is the half this used to throw away.**
    /// `RagdollAddConstraint` takes only the translation column into `originParentSpace`, but
    /// `constraint.constraintToAttached` — the matrix itself — is handed to
    /// `CreateRagdollConstraint` and becomes the attached body's constraint frame
    /// (`vphysics_interface.h:572`, *"Create a constraint in the space of pReferenceObject"*).
    /// Discarding it makes every joint measure its deflection from an IDENTITY rest pose instead of
    /// its bind pose, biasing each limit by the bind offset.
    ///
    /// **The turned fixture is what makes the rotation visible at all.** With both bones unrotated
    /// the answer is the identity, which is also what a transcription that kept nothing returns —
    /// the condition for which correct and broken predict the same observation. So the child is
    /// turned a quarter turn about Z: its own X axis then points along the parent's `+Y` and its Y
    /// axis along the parent's `−X`, and the columns of `constraintToAttached` are exactly those
    /// images.
    /// </remarks>
    [Test]
    public void Build_AConstraintWithATurnedChild_KeepsTheBoneToBoneRotation()
    {
        RagdollBody body = RagdollBody.Build(Physics(), TurnedSkeleton())!;

        RagdollAxes axes = body.Elements[1].AxesParentSpace;

        axes.X.X.ShouldBe(0f, Tolerance);
        axes.X.Y.ShouldBe(1f, Tolerance, "the child's own X points along its parent's +Y");
        axes.X.Z.ShouldBe(0f, Tolerance);

        axes.Y.X.ShouldBe(-1f, Tolerance, "and its Y along the parent's −X");
        axes.Y.Y.ShouldBe(0f, Tolerance);
        axes.Y.Z.ShouldBe(0f, Tolerance);

        axes.Z.Z.ShouldBe(1f, Tolerance, "the axis it was turned about is unmoved");
    }

    /// <remarks>
    /// **The control for the test above**, and it is the reason the turned fixture exists: an
    /// element with no constraint never reaches `Studio_CalcBoneToBoneTransform` at all, so its
    /// frame is the identity — the same answer a discarded rotation gives. The two together
    /// separate "kept the rotation" from "returned the identity twice".
    /// </remarks>
    [Test]
    public void Build_TheRootElement_KeepsTheIdentityRotation()
    {
        RagdollAxes axes = RagdollBody.Build(Physics(), TurnedSkeleton())!.Elements[0].AxesParentSpace;

        axes.X.ShouldBe(Vector3.UnitX);
        axes.Y.ShouldBe(Vector3.UnitY);
        axes.Z.ShouldBe(Vector3.UnitZ);
    }

    /// <remarks>
    /// The root has no constraint and therefore no parent — `ragdollelement_t::parentIndex` is left
    /// at the −1 `RagdollAddSolid` writes, and only `RagdollAddConstraint` ever changes it.
    /// </remarks>
    [Test]
    public void Build_TheRootElement_HasNoParent()
    {
        RagdollBody.Build(Physics(), Skeleton())!.Elements[0].ParentIndex.ShouldBe(-1);
    }

    /// <remarks>
    /// **A solid naming a bone the model does not have desynchronises every constraint**, because
    /// `RagdollAddSolid` increments `listCount` only when the lookup succeeded while constraints go
    /// on referencing the solid's own index — which is what `Assert( ragdoll.listCount ==
    /// solid.index )` is there to catch. The assert is compiled out of a release build, so the
    /// engine's own behaviour past that point is a mis-wired skeleton rather than a decision.
    ///
    /// Refused here instead, and this is the one place this file departs from the engine: a
    /// mis-indexed constraint is a corpse with its shin joined to its skull, and a corpse posed by
    /// its animation is strictly better than that. Said out loud rather than left as a silent
    /// improvement.
    /// </remarks>
    [Test]
    public void Build_ASolidNamingNoBone_IsRefusedRatherThanMisIndexed()
    {
        PhysicsModel physics = PhysicsWith(
            new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
            new PhysicsSolid(1, "bip_absent", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f));

        RagdollBody.Build(physics, Skeleton()).ShouldBeNull();
    }

    /// <remarks>
    /// **Valve's own words: "Bogus constraint on ragdoll".** `RagdollAddConstraint` nulls both ends
    /// when they are equal (`ragdoll_shared.cpp:217`) rather than joining a body to itself, and the
    /// element keeps the −1 parent it was created with.
    /// </remarks>
    [Test]
    public void Build_AConstraintJoiningASolidToItself_IsDropped()
    {
        PhysicsModel physics = PhysicsWith(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            new RagdollConstraint(1, 1, Axis, Axis, Axis));

        RagdollBody.Build(physics, Skeleton())!.Elements[1].ParentIndex.ShouldBe(-1);
    }

    /// <remarks>
    /// **A statue weighs a tonne, literally.** `RagdollAddSolid` overwrites the file's mass with
    /// `1000.f` when `params.fixedConstraints` is set (`ragdoll_shared.cpp:186`), which is the gold
    /// and ice corpses — `m_bFixedConstraints` is set for both in `CreateTFRagdoll`.
    ///
    /// The control is the ordinary build above, which keeps the file's own mass.
    /// </remarks>
    [Test]
    public void Build_WithFixedConstraints_OverwritesEveryMassWithATonne()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton(), fixedConstraints: true)!;

        body.Elements[0].Mass.ShouldBe(1000f);
        body.Elements[1].Mass.ShouldBe(1000f);

        RagdollBody.Build(Physics(), Skeleton())!.Elements[0].Mass.ShouldBe(10f);
    }

    /// <remarks>
    /// **The read-back is where the engine refuses to let a ragdoll stretch**, and it is the reason
    /// a solver owes one position rather than seventeen:
    ///
    /// <code>
    ///   element.pObject-&gt;GetPositionMatrix( &amp;pBoneToWorld.GetBoneForWrite( boneIndex ) );
    ///   if ( element.parentIndex &gt;= 0 &amp;&amp; !ragdoll.allowStretch )
    ///   {
    ///       VectorTransform( element.originParentSpace, pBoneToWorld.GetBone( parentBoneIndex ), out );
    ///       MatrixSetColumn( out, 3, pBoneToWorld.GetBoneForWrite( boneIndex ) );
    ///   }
    /// </code>
    ///
    /// `ragdoll_shared.cpp:562`. The simulation supplies the ORIENTATION; the position of every
    /// non-root bone is rebuilt from its parent's transform and the fixed offset above.
    ///
    /// **The manipulation is a position the solver got wrong on purpose.** The child is handed
    /// `(500, 500, 500)`, which must not survive: with the root at the origin and unrotated, the
    /// child has to land exactly on its recorded offset.
    /// </remarks>
    [Test]
    public void Pose_ANonRootElement_TakesItsPositionFromItsParentRatherThanTheSolver()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        float[][] bones = body.Pose(
            [
                (Vector3.Zero, Quaternion.Identity),
                (new Vector3(500f, 500f, 500f), Quaternion.Identity),
            ],
            boneCount: 2);

        bones[1][3].ShouldBe(3f, Tolerance);
        bones[1][7].ShouldBe(4f, Tolerance);
        bones[1][11].ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The control for the pair above: the ROOT keeps what the solver gave it.** Without it,
    /// "ignores the solver's positions entirely" passes the test above and the ragdoll can never
    /// move anywhere.
    /// </remarks>
    [Test]
    public void Pose_TheRootElement_KeepsTheSolversPosition()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        float[][] bones = body.Pose(
            [
                (new Vector3(7f, 8f, 9f), Quaternion.Identity),
                (Vector3.Zero, Quaternion.Identity),
            ],
            boneCount: 2);

        bones[0][3].ShouldBe(7f, Tolerance);
        bones[0][7].ShouldBe(8f, Tolerance);
        bones[0][11].ShouldBe(9f, Tolerance);
    }

    /// <remarks>
    /// **A rotated parent carries its child around with it**, which is the half of the read-back a
    /// test with identity rotations cannot see: `VectorTransform` puts the offset through the
    /// parent's whole matrix, not just its translation.
    ///
    /// A quarter turn about Z sends the offset `(3, 4, 0)` to `(−4, 3, 0)`.
    /// </remarks>
    [Test]
    public void Pose_ARotatedParent_TurnsTheOffsetWithIt()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        float[][] bones = body.Pose(
            [
                (Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f)),
                (Vector3.Zero, Quaternion.Identity),
            ],
            boneCount: 2);

        bones[1][3].ShouldBe(-4f, Tolerance);
        bones[1][7].ShouldBe(3f, Tolerance);
        bones[1][11].ShouldBe(0f, Tolerance);
    }

    /// <summary>An axis with no rotation allowed, for constraints these tests do not exercise.</summary>
    private static ConstraintAxis Axis => new(0f, 0f, 0f);

    /// <summary>Two solids joined by one constraint, named for the skeleton below.</summary>
    /// <remarks>
    /// **A gib's solid names no bone, and that is normal rather than broken** (B371). Measured on
    /// `models/player/gibs/medicgib001.mdl`: the solid is `medicgib001_reference` and the model's
    /// only bone is `polymsh`. `Build` refuses it — correctly, because a ragdoll's solids each
    /// drive a named bone — and the engine never asks: `CreateGibsFromList` reaches
    /// `BreakModelCreateSingle` (`props_shared.cpp:1497`), which makes a `prop_physics` body from
    /// the VCollide alone.
    ///
    /// **Both halves are asserted together**, because the point is the DIFFERENCE between them. A
    /// test that only showed `BuildProp` working would pass just as well if `Build` had been
    /// loosened to accept unnamed solids — which is the change that must not happen, since it would
    /// silence a real ragdoll defect to permit a normal prop.
    /// </remarks>
    [Test]
    public void BuildProp_ForASolidNamingNoBone_BuildsWhereBuildRefuses()
    {
        PhysicsModel physics = Prop("medicgib001_reference");

        RagdollBody.Build(physics, PropSkeleton())
            .ShouldBeNull("a ragdoll solid must name a bone");

        RagdollBody prop = RagdollBody.BuildProp(physics)!;

        prop.Elements.Count.ShouldBe(1);
        prop.Constraints.Count.ShouldBe(0, "a single body has nothing to be jointed to");
        prop.Elements[0].BoneIndex.ShouldBe(0, "a one-bone prop's body IS the model transform");
        prop.Elements[0].Hull.Count.ShouldBeGreaterThan(0);
    }

    /// <remarks>
    /// **A body with no hull could not be collided with**, so it is refused rather than returned as
    /// a gib that would fall through the world for ever. The `.phy` is a stranger's file (D32) and
    /// a solid without a hull is exactly the shape a truncated one leaves behind.
    /// </remarks>
    [Test]
    public void BuildProp_WithNoHull_Refuses() =>
        RagdollBody.BuildProp(
            PhysicsModel.From(
                [new PhysicsSolid(0, "gib_reference", "", "flesh", 5f, 1f, 0f, 0f, 10f, 0f)],
                [],
                1,
                checksum: 0,
                collisionRules: null,
                hulls: null))
            .ShouldBeNull();

    /// <remarks>
    /// **The `.phy`'s own mass and surface reach the body**, because a gib that weighs nothing or
    /// slides like ice is a gib that behaves wrongly on the ground it was just given.
    /// </remarks>
    [Test]
    public void BuildProp_ForAProp_CarriesItsMassAndSurface()
    {
        RagdollBody prop = RagdollBody.BuildProp(Prop("gib_reference"))!;

        prop.Elements[0].Mass.ShouldBe(5f);
        prop.Elements[0].SurfaceProp.ShouldBe("flesh");
    }

    /// <summary>A single-solid prop with one hull, as every gib model is.</summary>
    private static PhysicsModel Prop(string solidName)
    {
        PhysicsLedge ledge = new(
            [new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0.1f, 0f)],
            [(0, 1, 2)],
            Vector3.Zero,
            0.1f);

        return PhysicsModel.From(
            [new PhysicsSolid(0, solidName, "", "flesh", 5f, 1f, 0f, 0f, 10f, 0f)],
            [],
            1,
            checksum: 0,
            collisionRules: null,
            hulls: [[ledge]]);
    }

    /// <summary>A gib's skeleton: one bone, named nothing the <c>.phy</c> mentions.</summary>
    /// <remarks>
    /// `polymsh` is the real name on every TF2 gib, measured — an exporter's default, which is
    /// exactly why it matches no solid.
    /// </remarks>
    private static IReadOnlyList<StudioBone> PropSkeleton() =>
        [
            new StudioBone(
                "polymsh",
                -1,
                (0f, 0f, 0f),
                (0f, 0f, 0f, 1f),
                new float[]
                {
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f,
                }),
        ];

    private static PhysicsModel Physics() =>
        PhysicsWith(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            new RagdollConstraint(0, 1, Axis, Axis, Axis));

    private static PhysicsModel PhysicsWith(params PhysicsSolid[] solids) =>
        PhysicsModel.From(solids, [], solids.Length, checksum: 0);

    private static PhysicsModel PhysicsWith(
        IReadOnlyList<PhysicsSolid> solids, params RagdollConstraint[] constraints) =>
        PhysicsModel.From(solids, constraints, solids.Count, checksum: 0);

    /// <summary>Two bones at chosen bind positions — <see cref="RagdollSkeletons.Straight"/>.</summary>
    private static IReadOnlyList<StudioBone> Skeleton() => RagdollSkeletons.Straight();

    /// <summary>The same pair with the child turned — <see cref="RagdollSkeletons.Turned"/>.</summary>
    private static IReadOnlyList<StudioBone> TurnedSkeleton() => RagdollSkeletons.Turned();
}
