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

    /// <summary>
    /// Two bones at chosen bind positions, so the offset between them is predictable.
    /// </summary>
    /// <remarks>
    /// **`poseToBone` is the WORLD-to-bone matrix**, so a bone standing at `p` with no rotation
    /// carries a translation of `−p`. The root is at `(1, 2, 3)` and the child at `(4, 6, 3)`:
    /// asymmetric, and non-zero in two axes, so a transcription that swapped the two bones or
    /// transposed the multiply cannot land on the same answer.
    /// </remarks>
    private static IReadOnlyList<StudioBone> Skeleton() =>
        [
            Bone("bip_root", -1, 1f, 2f, 3f),
            Bone("bip_child", 0, 4f, 6f, 3f),
        ];

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
