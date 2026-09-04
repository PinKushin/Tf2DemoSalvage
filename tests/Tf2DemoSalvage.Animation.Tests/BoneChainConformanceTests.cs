using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <c>BuildBoneChain</c>: one bone's model-space matrix, concatenated from a LOCAL pose.
/// </summary>
/// <remarks>
/// **The piece the lock bracket needs and the pose composition does not have** (B311).
/// `AddSequenceLocks` and `SolveLock` both call `BuildBoneChain( pos, q, bone, boneToWorld,
/// boneComputed )` (`bone_setup.cpp:3423`), which walks a bone's ancestry and concatenates it —
/// where this project only ever concatenates the WHOLE skeleton, once, after every layer has been
/// composed.
///
/// <code>
///   void CIKContext::BuildBoneChain( const Vector pos[], const Quaternion q[], int iBone,
///                                    matrix3x4_t *pBoneToWorld, CBoneBitList &amp;boneComputed )
///   {
///       ::BuildBoneChain( m_pStudioHdr, m_rootxform, pos, q, iBone, pBoneToWorld, boneComputed );
///   }
/// </code>
///
/// **`m_rootxform` is IDENTITY for a sequence lock.** The engine builds a throwaway context for
/// them with `vec3_angle, vec3_origin` under the comment *"local space relative so absolute
/// position doesn't mater"*, so the chain comes out in the MODEL's space and needs no entity
/// placement — which is what makes a capture and a restore comparable without knowing where the
/// player is standing.
///
/// **The memo is not an optimisation to skip.** `boneComputed` stops a shared ancestor being
/// concatenated once per chain; with two locks on one sequence — which is what every locking TF2
/// sequence declares — the spine is walked twice without it.
/// </remarks>
public sealed class BoneChainConformanceTests
{
    private const double Tolerance = 1e-4;

    /// <remarks>
    /// **A root bone's model matrix IS its local one**, because there is no parent to concatenate
    /// through. The engine's recursion terminates on <c>pbone->parent == -1</c>.
    /// </remarks>
    [Test]
    public void Build_ForARootBone_IsItsOwnLocalTransform()
    {
        BoneChain chain = new(Parents([-1]), 1);

        chain.Build(0, [Posed(0, (3f, 4f, 5f))]);

        Position(chain, 0).ShouldBe((3f, 4f, 5f));
    }

    /// <remarks>
    /// **A child is its parent's transform applied to its own**, so a translation down a chain of
    /// three sums. What this pins is the DEPTH — that the walk reaches a grandparent — and a
    /// sabotage reversing the loop reddens it.
    ///
    /// **It cannot see the concatenation ORDER, despite its name, and a sabotage said so.**
    /// Translations commute, so `T(a)·T(b)` and `T(b)·T(a)` agree on every input this test can
    /// give. Order is pinned by <see cref="Build_WithARotatedParent_TurnsTheChildsOffset"/>, which
    /// is the only test here with a rotation in it — kept as two tests rather than one because the
    /// three-deep sum and the turned offset fail for different reasons.
    /// </remarks>
    [Test]
    public void Build_ForAChildBone_ConcatenatesThroughItsParents()
    {
        BoneChain chain = new(Parents([-1, 0, 1]), 3);

        chain.Build(
            2,
            [Posed(0, (1f, 0f, 0f)), Posed(1, (2f, 0f, 0f)), Posed(2, (4f, 0f, 0f))]);

        Position(chain, 2).ShouldBe((7f, 0f, 0f), "1 + 2 + 4 down the chain");
    }

    /// <remarks>
    /// **The parent's ROTATION turns the child's offset**, which a concatenation that only added
    /// positions would get right in the test above and wrong here. A quarter turn about Z takes a
    /// child offset along +X onto +Y.
    /// </remarks>
    [Test]
    public void Build_WithARotatedParent_TurnsTheChildsOffset()
    {
        BoneChain chain = new(Parents([-1, 0]), 2);

        // A quarter turn about Z: (0, 0, sin 45°, cos 45°).
        float half = MathF.Sqrt(0.5f);

        chain.Build(
            1,
            [
                new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, half, half)),
                Posed(1, (5f, 0f, 0f)),
            ]);

        (float x, float y, float z) = Position(chain, 1);

        x.ShouldBe(0f, Tolerance, "the child's +X offset is turned onto +Y");
        y.ShouldBe(5f, Tolerance);
        z.ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The memo, and the control that says it is a memo rather than a skip.** Building a second
    /// bone that shares an ancestor must not rebuild that ancestor — and must still produce the
    /// right answer for the second bone, which a memo that wrongly considered the CHILD computed
    /// would not.
    /// </remarks>
    [Test]
    public void Build_ForTwoBonesSharingAnAncestor_ComputesTheAncestorOnce()
    {
        BoneChain chain = new(Parents([-1, 0, 0]), 3);

        IReadOnlyList<StudioBonePose> pose =
            [Posed(0, (1f, 0f, 0f)), Posed(1, (2f, 0f, 0f)), Posed(2, (0f, 3f, 0f))];

        chain.Build(1, pose);

        int afterFirst = chain.Concatenations;

        chain.Build(2, pose);

        Position(chain, 1).ShouldBe((3f, 0f, 0f));
        Position(chain, 2).ShouldBe((1f, 3f, 0f), "the shared root still counts");

        (chain.Concatenations - afterFirst).ShouldBe(
            1, "only bone 2 was new; the root was already computed");
    }

    /// <remarks>
    /// **A pose that omits a bone falls back to nothing rather than to stale memory.** The engine
    /// indexes `pos[]`/`q[]` directly and they are always dense; here the array is reused between
    /// entities, so a short pose must not read whatever the last one left.
    /// </remarks>
    [Test]
    public void Build_WithABoneThePoseOmits_TreatsItAsIdentity()
    {
        BoneChain chain = new(Parents([-1, 0]), 2);

        chain.Build(1, [Posed(0, (1f, 0f, 0f))]);

        Position(chain, 1).ShouldBe((1f, 0f, 0f), "bone 1 contributes nothing it did not state");
    }

    private static StudioBonePose Posed(int bone, (float X, float Y, float Z) position) =>
        new(bone, position, (0f, 0f, 0f, 1f));

    private static int[] Parents(int[] parents) => parents;

    private static (float X, float Y, float Z) Position(BoneChain chain, int bone)
    {
        ReadOnlySpan<float> matrix = chain.Matrix(bone);

        return (matrix[3], matrix[7], matrix[11]);
    }
}
