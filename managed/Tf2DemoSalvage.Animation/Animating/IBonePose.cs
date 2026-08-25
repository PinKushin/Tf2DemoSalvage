using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>Which bones a stage has already written, so a later stage leaves them alone.</summary>
/// <remarks>
/// **This is <c>CBoneBitList boneComputed</c>**, threaded through <c>BuildTransformations</c> so it
/// can skip bones the IK solver has already placed (<c>c_baseanimating.cpp:1533</c>). The bone merge
/// uses the same idea through a different member, <c>IsBoneMerged</c> at <c>:1519</c>.
///
/// **One list rather than two, because the two questions are the same question.** A bone is either
/// still to be built from its animation or it is not, and what put it there — an IK solve, a merge
/// from a wearer — does not change what the transform stage must do about it.
/// </remarks>
public sealed class BoneBitList
{
    private readonly bool[] _marked;

    /// <summary>Creates an empty list for a model of this size.</summary>
    /// <param name="count">How many bones.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public BoneBitList(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _marked = new bool[count];
    }

    /// <summary>Whether a bone has already been written this build.</summary>
    /// <param name="bone">Which bone.</param>
    /// <returns>Whether some earlier stage placed it.</returns>
    public bool IsMarked(int bone) => bone >= 0 && bone < _marked.Length && _marked[bone];

    /// <summary>Records that a bone has been written.</summary>
    /// <param name="bone">Which bone.</param>
    public void Mark(int bone)
    {
        if (bone >= 0 && bone < _marked.Length)
        {
            _marked[bone] = true;
        }
    }

    /// <summary>Forgets everything, so the list can be reused next frame.</summary>
    /// <remarks>
    /// Cleared rather than reallocated, per D87: the size is known when the model is, and a frame
    /// has a deadline that RAM does not.
    /// </remarks>
    public void Clear() => Array.Clear(_marked);
}

/// <summary>
/// The half of the bone pipeline that knows about a specific model.
/// </summary>
/// <remarks>
/// **The seam is where the SDK's own is.** <c>C_BaseAnimating::SetupBones</c> owns the caching, the
/// masks and the recursion; it delegates the actual blending to <c>IBoneSetup</c> and to its own
/// <c>BuildTransformations</c>. Splitting at the same place means <see cref="AnimatingEntity"/> can
/// be tested for the properties that make it Valve's — posed once per frame, parent before child,
/// a wider mask rebuilding and a narrower one not — without loading a model at all.
///
/// **It is an interface rather than a base class**, so the scene layer supplies its own
/// implementation over <c>StudioSkeleton</c> and the tests supply a counting fake. Inheritance here
/// would put model loading inside the assembly that must not depend on it.
/// </remarks>
public interface IBonePose
{
    /// <summary>How many bones the model has.</summary>
    public int BoneCount { get; }

    /// <summary>One bone's <c>BONE_USED_BY_*</c> flags, which decide whether a mask covers it.</summary>
    /// <param name="bone">Which bone.</param>
    /// <returns>The flags word from the model.</returns>
    public int FlagsOf(int bone);

    /// <summary>One bone's name, which is how two skeletons are paired.</summary>
    /// <param name="bone">Which bone.</param>
    /// <returns>The name as the model spells it.</returns>
    /// <remarks>
    /// **Bone merging is a name match and nothing else.** <c>CBoneMergeCache::UpdateCache</c> pairs
    /// with <c>Studio_BoneIndexByName( m_pFollowHdr, pOwnerBones[i].pszName() )</c>
    /// (<c>bone_merge_cache.cpp:83</c>) — there is no index correspondence between a hat's skeleton
    /// and a scout's, and there could not be, since the same hat is worn by nine classes.
    /// </remarks>
    public string NameOf(int bone);

    /// <summary>Builds the bones a mask covers, into the accessor.</summary>
    /// <param name="boneMask">Which bones are wanted.</param>
    /// <param name="currentTime">Demo time, for advancing cycles.</param>
    /// <param name="into">Where the finished bone-to-world matrices go.</param>
    /// <param name="alreadyWritten">
    /// Bones an earlier stage placed — merged from a wearer, or solved by IK — which must be read
    /// rather than rebuilt, because their children concatenate onto them.
    /// </param>
    /// <remarks>
    /// **Covers <c>StandardBlendingRules</c> and <c>BuildTransformations</c> together**, which is
    /// the pair that turns an animation into world-space matrices. They are separate functions in
    /// the engine because IK runs between them; that stage is not implemented here yet (B182), and
    /// when it is, this splits at the same seam rather than growing a parameter.
    /// </remarks>
    public void Build(int boneMask, double currentTime, BoneAccessor into, BoneBitList alreadyWritten);
}
