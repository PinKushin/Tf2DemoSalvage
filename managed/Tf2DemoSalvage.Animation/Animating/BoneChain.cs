using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One bone's model-space matrix, concatenated from a LOCAL pose — <c>BuildBoneChain</c>.
/// </summary>
/// <remarks>
/// **The engine can ask where a bone IS part way through composing a pose, and this project could
/// not** (B311). `AddSequenceLocks` and `SolveLock` both call
/// <c>BuildBoneChain( pos, q, iBone, pBoneToWorld, boneComputed )</c> (`bone_setup.cpp:3423`) to
/// find a chain's end effector between one sequence and the next; everything here concatenated the
/// whole skeleton once, after every layer had been composed, and had no way to answer the question
/// earlier.
///
/// <code>
///   void BuildBoneChain( const CStudioHdr *pStudioHdr, const matrix3x4_t &amp;rootxform,
///                        const Vector pos[], const Quaternion q[], int iBone,
///                        matrix3x4_t *pBoneToWorld, CBoneBitList &amp;boneComputed )
///   {
///       if ( boneComputed.IsBoneMarked(iBone) ) return;
///       matrix3x4_t bonematrix;
///       QuaternionMatrix( q[iBone], pos[iBone], bonematrix );
///       int parent = pStudioHdr->boneParent( iBone );
///       if (parent == -1) ConcatTransforms( rootxform, bonematrix, pBoneToWorld[iBone] );
///       else
///       {
///           BuildBoneChain( pStudioHdr, rootxform, pos, q, parent, pBoneToWorld, boneComputed );
///           ConcatTransforms( pBoneToWorld[parent], bonematrix, pBoneToWorld[iBone] );
///       }
///       boneComputed.MarkBone(iBone);
///   }
/// </code>
///
/// **The root transform is IDENTITY here, and that is a fact about sequence locks rather than a
/// simplification.** Valve builds a throwaway context for them with <c>vec3_angle, vec3_origin</c>
/// under the comment *"local space relative so absolute position doesn't mater"*, so a lock's
/// capture and its restore are comparable without knowing where the entity is standing. A different
/// caller wanting world space would concatenate the placement afterwards rather than passing it in.
///
/// **The memo is not an optimisation to drop.** Every locking TF2 sequence declares TWO locks, and
/// both chains hang off the same spine — without <c>boneComputed</c> that spine is walked twice per
/// sequence, per entity, per frame.
/// </remarks>
public sealed class BoneChain
{
    private readonly int[] _parents;
    private readonly bool[] _computed;
    private readonly int _count;

    /// <summary>Prepares a chain builder for one skeleton.</summary>
    /// <param name="parents">Each bone's parent, or −1 for a root.</param>
    /// <param name="bones">How many bones the skeleton has.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parents"/> is null.</exception>
    public BoneChain(IReadOnlyList<int> parents, int bones)
    {
        ArgumentNullException.ThrowIfNull(parents);

        _parents = [.. parents];
        _count = Math.Max(0, bones);
        _computed = new bool[_count];

        Bones = new BoneAccessor(_count);
    }

    /// <summary>The matrices, in the shape the IK solver already reads.</summary>
    /// <remarks>
    /// **A <see cref="BoneAccessor"/> rather than arrays of this type's own**, so the lock solve
    /// can call the same chain solver the IK rules do instead of carrying a second copy of
    /// `Studio_SolveIK`'s knee logic. Sixty lines of preference arithmetic duplicated between two
    /// call sites is the drift this project has been bitten by, and the two would diverge on the
    /// next reading of the engine rather than at the moment of copying.
    /// </remarks>
    public BoneAccessor Bones { get; }

    /// <summary>How many bones have been concatenated since the last <see cref="Reset"/>.</summary>
    /// <remarks>
    /// **So a test can tell a memo from a skip.** Both produce the right matrix for the first bone
    /// asked; only a count says whether a shared ancestor was walked twice.
    /// </remarks>
    public int Concatenations { get; private set; }

    /// <summary>Forgets what has been computed, for a new pose.</summary>
    /// <remarks>
    /// **Called between sequences, not between frames.** The memo is only valid while the pose it
    /// was built from is unchanged, and the whole point of a lock bracket is that the pose changes
    /// in between — so a capture and the restore that follows it are two different builds.
    /// </remarks>
    public void Reset()
    {
        Array.Clear(_computed);

        Concatenations = 0;
    }

    /// <summary>Concatenates one bone and its ancestors into model space.</summary>
    /// <param name="bone">Which bone.</param>
    /// <param name="pose">The local pose, as a sparse list of bones it states.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pose"/> is null.</exception>
    /// <remarks>
    /// **Iterative rather than recursive, because the depth is data.** A skeleton comes from a file
    /// and a malformed one can name a parent chain as long as it likes; the engine recurses and is
    /// protected by its own asserts, which are compiled out of a release build.
    ///
    /// **A cycle is stopped by the memo itself** — a bone already marked is not walked again — so a
    /// parent loop terminates with a wrong matrix rather than a stack overflow. That is the same
    /// trade the rest of this reader makes: a malformed model draws oddly instead of crashing.
    /// </remarks>
    public void Build(int bone, IReadOnlyList<StudioBonePose> pose)
    {
        ArgumentNullException.ThrowIfNull(pose);

        // **`_computed[bone]` here is Valve's `if (boneComputed.IsBoneMarked(iBone)) return;` and
        // is provably redundant given the walk below** — which starts at this same bone under
        // `!_computed[at]`, so a marked bone leaves `depth` at zero and concatenates nothing
        // either way. A sabotage removing it reddened nothing and could not, by construction.
        // Kept because it is the engine's line and because it skips a `stackalloc`, not because
        // anything depends on it.
        if (bone < 0 || bone >= _count || _computed[bone])
        {
            return;
        }

        // Walk up to the highest uncomputed ancestor, then concatenate downwards. The stack is the
        // list itself rather than the call stack.
        int depth = 0;
        Span<int> upwards = stackalloc int[MaximumDepth];

        for (int at = bone; at >= 0 && at < _count && !_computed[at]; )
        {
            if (depth == MaximumDepth)
            {
                break;
            }

            upwards[depth++] = at;
            _computed[at] = true;

            at = _parents[at];
        }

        Span<float> local = stackalloc float[12];

        for (int step = depth - 1; step >= 0; step--)
        {
            int at = upwards[step];

            StudioBonePose stated = Stated(pose, at);

            StudioBones.FromQuaternion(stated.Rotation, stated.Position, local);

            int parent = _parents[at];

            if (parent < 0 || parent >= _count)
            {
                local.CopyTo(Bones.BoneForWrite(at));
            }
            else
            {
                StudioBones.Concatenate(Bones.Bone(parent), local, Bones.BoneForWrite(at));
            }

            Concatenations++;
        }
    }

    /// <summary>The model-space matrix of a bone <see cref="Build"/> has reached.</summary>
    /// <param name="bone">Which bone.</param>
    /// <returns>Its twelve floats, or empty when the bone is not one of this skeleton's.</returns>
    public ReadOnlySpan<float> Matrix(int bone) =>
        bone >= 0 && bone < _count ? Bones.Bone(bone) : [];

    /// <summary>How deep a parent chain this will walk before giving up.</summary>
    /// <remarks>
    /// <c>MAXSTUDIOBONES</c> is 128 in the SDK, and a chain cannot be longer than the skeleton. The
    /// cap exists because the parent list comes from a file: a cycle would otherwise walk for ever.
    /// </remarks>
    private const int MaximumDepth = 128;

    /// <summary>What a pose says about one bone, or a no-op transform when it says nothing.</summary>
    /// <remarks>
    /// **Identity rather than the bind pose, deliberately.** The engine's <c>pos[]</c>/<c>q[]</c>
    /// are dense and always hold something, because `InitPose` seeded them; a caller here that has
    /// already densified passes a full list and this never fires. It exists so a sparse list cannot
    /// read a neighbouring bone's entry by index.
    /// </remarks>
    private static StudioBonePose Stated(IReadOnlyList<StudioBonePose> pose, int bone)
    {
        if (bone < pose.Count && pose[bone].Bone == bone)
        {
            return pose[bone];
        }

        for (int at = 0; at < pose.Count; at++)
        {
            if (pose[at].Bone == bone)
            {
                return pose[at];
            }
        }

        return new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
    }
}
