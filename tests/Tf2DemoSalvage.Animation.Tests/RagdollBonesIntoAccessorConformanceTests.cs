using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A ragdoll's bones replacing the animation's, in the accessor (B58, D146).
/// </summary>
/// <remarks>
/// **This is the seam, and the engine's own arrangement is what makes it one line.**
/// `C_BaseAnimating::BuildTransformations` runs the ragdoll FIRST, into the same array everything
/// else writes, marking each bone it touched:
///
/// <code>
/// if ( m_pRagdoll )
/// {
///     int oldWritableBones = m_BoneAccessor.GetWritableBones();
///     int oldReadableBones = m_BoneAccessor.GetReadableBones();
///     m_BoneAccessor.SetWritableBones( BONE_USED_BY_ANYTHING );
///     m_BoneAccessor.SetReadableBones( BONE_USED_BY_ANYTHING );
///     m_pRagdoll->RagdollBone( this, pbones, hdr->numbones(), boneSimulated, m_BoneAccessor );
///     m_BoneAccessor.SetWritableBones( oldWritableBones );
///     m_BoneAccessor.SetReadableBones( oldReadableBones );
/// }
/// </code>
///
/// `c_baseanimating.cpp:1473-1492`. `boneSimulated[]` is this project's <see cref="BoneBitList"/>,
/// and the per-bone loop later skips what it marks — so a simulated bone is not "overridden after
/// the fact", it is simply never computed from an animation.
///
/// **The widen-and-restore is not decoration.** A ragdoll drives bones outside whatever mask the
/// caller asked for, and without it those writes would be refused by the accessor's own guard.
/// </remarks>
public sealed class RagdollBonesIntoAccessorConformanceTests
{
    private const float Tolerance = 1e-4f;

    /// <remarks>
    /// **Every element's bone is marked, and nothing else is.** `RagdollBone` marks exactly
    /// `boneSimulated[m_ragdoll.boneIndex[i]]` per element, so a model with more bones than solids
    /// leaves the rest for the animation — which is the whole point of the split.
    /// </remarks>
    [Test]
    public void PoseInto_WithATwoElementRagdoll_MarksOnlyTheBonesItDrives()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        BoneAccessor into = new(4) { WritableBones = -1, ReadableBones = -1 };
        BoneBitList written = new(4);

        body.PoseInto(Rest(), into, written);

        written.IsMarked(0).ShouldBeTrue("the root's bone is driven");
        written.IsMarked(1).ShouldBeTrue("and the child's");
        written.IsMarked(2).ShouldBeFalse("this model has bones the ragdoll does not drive");
        written.IsMarked(3).ShouldBeFalse();
    }

    /// <remarks>
    /// **The root keeps the solver's position and a child does not** — `RagdollGetBoneMatrix`
    /// overwrites every non-root position from its parent's finished matrix and the offset recorded
    /// at build, under Valve's note *"overwrite the position from physics to force rigid
    /// attachment"*.
    ///
    /// **The child is handed a deliberately wrong position**, which is the manipulation: with the
    /// root at the origin and unrotated, the child must land on its recorded offset and not on the
    /// `(500, 500, 500)` the solver claimed.
    /// </remarks>
    [Test]
    public void PoseInto_WithAChildTheSolverMisplaced_RebuildsItFromItsParent()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        BoneAccessor into = new(4) { WritableBones = -1, ReadableBones = -1 };

        body.PoseInto(
            [
                (Vector3.Zero, Quaternion.Identity),
                (new Vector3(500f, 500f, 500f), Quaternion.Identity),
            ],
            into,
            new BoneBitList(4));

        into.Bone(1)[3].ShouldBe(3f, Tolerance);
        into.Bone(1)[7].ShouldBe(4f, Tolerance);
        into.Bone(1)[11].ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The control: the ROOT keeps what the solver gave it.** Without this, "ignores the solver's
    /// positions entirely" passes the test above and a ragdoll can never move anywhere.
    /// </remarks>
    [Test]
    public void PoseInto_WithTheRootElement_KeepsTheSolversPosition()
    {
        RagdollBody body = RagdollBody.Build(Physics(), Skeleton())!;

        BoneAccessor into = new(4) { WritableBones = -1, ReadableBones = -1 };

        body.PoseInto(
            [(new Vector3(7f, 8f, 9f), Quaternion.Identity), (Vector3.Zero, Quaternion.Identity)],
            into,
            new BoneBitList(4));

        into.Bone(0)[3].ShouldBe(7f, Tolerance);
        into.Bone(0)[7].ShouldBe(8f, Tolerance);
        into.Bone(0)[11].ShouldBe(9f, Tolerance);
    }

    /// <remarks>
    /// **A bone the ragdoll drives is left ALONE by the animation, and that is the seam working.**
    /// `SkeletonPose.Build` skips a bone already marked, so the order in
    /// `BuildTransformations` — ragdoll, then merge, then the per-bone loop — needs no undo step.
    ///
    /// **The unmarked bone is the control.** Without it this passes against a `Build` that writes
    /// nothing at all, which would look identical on the marked bone.
    /// </remarks>
    [Test]
    public void Build_AfterARagdollHasWrittenABone_LeavesItAndStillFillsTheOthers()
    {
        BoneAccessor into = new(2) { WritableBones = -1, ReadableBones = -1 };
        BoneBitList written = new(2);

        // A recognisable value no animation would produce.
        into.BoneForWrite(0)[3] = 1234f;
        written.Mark(0);

        // An animation that poses every bone at the origin, so "the animation ran" is a value the
        // ragdoll's 1234 could not be confused with.
        SkeletonPose pose = new(
            [
                new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), Identity()),
                new StudioBone("child", 0, (0f, 0f, 0f), (0f, 0f, 0f, 1f), Identity()),
            ],
            (_, _, _, _) => []);

        pose.Build(-1, 0d, into, written);

        into.Bone(0)[3].ShouldBe(1234f, "the ragdoll's bone survived the animation pass");
        into.Bone(1)[3].ShouldBe(0f, "and an unmarked bone was still built");
    }

    /// <remarks>
    /// **The wiring assertion.** `PoseInto` and `Build` are each covered above, and both pass
    /// whether or not `SetupBones` ever calls the ragdoll — the shape that has shipped three no-ops
    /// in this project with a green suite.
    ///
    /// **It asserts the widen, not just the call**, because that is the part a plausible
    /// implementation drops: the ragdoll is invoked with both masks at
    /// `BONE_USED_BY_ANYTHING` and they are restored afterwards. A version that called the hook
    /// without widening would pass a "was it called" test and then silently refuse the writes on
    /// any entity whose draw asked for a narrower mask.
    /// </remarks>
    [Test]
    public void SetupBones_WithARagdollAttached_RunsItWidenedAndRestoresTheMasks()
    {
        SkeletonPose pose = new(
            [new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), Identity())],
            (_, _, _, _) => []);

        AnimatingEntity entity = new(pose, new BoneFrameCounter());

        int sawWritable = 0;
        int sawReadable = 0;
        int calls = 0;

        entity.Ragdoll = (into, written) =>
        {
            calls++;
            sawWritable = into.WritableBones;
            sawReadable = into.ReadableBones;
            into.BoneForWrite(0)[3] = 99f;
            written.Mark(0);
        };

        entity.SetupBones(StudioBoneFlags.UsedByHitbox, 0d).ShouldBeTrue();

        calls.ShouldBe(1, "SetupBones ran the ragdoll");
        sawWritable.ShouldBe(StudioBoneFlags.UsedByAnything, "widened for the ragdoll");
        sawReadable.ShouldBe(StudioBoneFlags.UsedByAnything);

        entity.Bones.Bone(0)[3].ShouldBe(99f, Tolerance, "and its bone survived the animation");

        entity.Bones.WritableBones.ShouldNotBe(
            StudioBoneFlags.UsedByAnything, "the masks were restored, not left wide");
    }

    private static float[] Identity() =>
        [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    private static (Vector3, Quaternion)[] Rest() =>
        [(Vector3.Zero, Quaternion.Identity), (Vector3.Zero, Quaternion.Identity)];

    private static readonly ConstraintAxis Axis = new(-30f, 30f, 0f);

    private static PhysicsModel Physics() =>
        PhysicsModel.From(
            [
                new PhysicsSolid(0, "bip_root", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f),
                new PhysicsSolid(1, "bip_child", "bip_root", "flesh", 2f, 1f, 0f, 0f, 20f, 0f),
            ],
            [new RagdollConstraint(0, 1, Axis, Axis, Axis)],
            2,
            checksum: 0);

    /// <summary>Four bones, of which the ragdoll drives the first two.</summary>
    private static IReadOnlyList<StudioBone> Skeleton() =>
        [
            Bone("bip_root", -1, 1f, 2f, 3f),
            Bone("bip_child", 0, 4f, 6f, 3f),
            Bone("hat", 1, 4f, 6f, 9f),
            Bone("hat_tip", 2, 4f, 6f, 12f),
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
