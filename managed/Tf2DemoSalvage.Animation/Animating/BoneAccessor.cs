using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One entity's bone matrices, with a record of which of them are current.
/// </summary>
/// <remarks>
/// **This is <c>CBoneAccessor</c> (<c>public/bone_accessor.h</c>), and the two masks are the whole
/// point of it.** They are not per-bone bitsets: they are <c>BONE_USED_BY_*</c> masks, matched
/// against each bone's own <c>flags</c> field. "Readable" means every bone whose flags intersect
/// this mask has been built and may be read.
///
/// **Why that indirection instead of a bit per bone.** A caller does not ask for bones 0, 4 and 17;
/// it asks for the bones needed to draw, or to place an attachment, or to test a hitbox. The mask
/// expresses the QUESTION, and the model's own flags answer which bones that question covers — so a
/// shadow pass and a render pass are two different masks over one skeleton rather than two lists
/// somebody has to keep in step.
///
/// It is also what makes <see cref="AnimatingEntity.SetupBones(int, double)"/> idempotent within a frame: the
/// early-out is <c>(readable &amp; wanted) == wanted</c>, one integer comparison
/// (<c>c_baseanimating.cpp:2911</c>). Without it, a player worn by six items is posed six times.
///
/// **Matrices are <c>float[12]</c>, row-major 3×4**, which is what the rest of this solution already
/// passes around — <c>StudioSkeleton</c>, the merge, and the renderer's model constant all use it.
/// A different convention here would need converting at every boundary.
/// </remarks>
public sealed class BoneAccessor
{
    private readonly float[][] _bones;

    /// <summary>Creates an accessor over a fresh set of identity matrices.</summary>
    /// <param name="count">How many bones the model has.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <remarks>
    /// **Allocated once per entity and reused every frame**, which is the arrangement D87 asks for:
    /// the set of bones is known when the model is, and a frame has a deadline that RAM does not.
    /// </remarks>
    public BoneAccessor(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _bones = new float[count][];

        for (int index = 0; index < count; index++)
        {
            _bones[index] = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        }
    }

    /// <summary>How many bones this holds.</summary>
    public int Count => _bones.Length;

    /// <summary>Which bones have been built and may be read, as a <c>BONE_USED_BY_*</c> mask.</summary>
    public int ReadableBones { get; set; }

    /// <summary>Which bones may currently be written.</summary>
    /// <remarks>
    /// **Tracked separately from readable, because the engine widens them at different moments.**
    /// <c>SetupBones</c> opens writing for the mask it is about to build and opens reading for the
    /// same set at once, so a stage part-way through can read a bone an earlier stage wrote. The
    /// pair is what lets the assertions in Valve's debug build catch a stage reading a bone nobody
    /// has computed.
    /// </remarks>
    public int WritableBones { get; set; }

    /// <summary>One bone's matrix, for reading.</summary>
    /// <param name="bone">Which bone.</param>
    /// <returns>Twelve floats, row-major 3×4.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such bone.</exception>
    public ReadOnlySpan<float> Bone(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _bones.Length);

        return _bones[bone];
    }

    /// <summary>One bone's matrix, for writing.</summary>
    /// <param name="bone">Which bone.</param>
    /// <returns>The live array, so a caller writes through it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such bone.</exception>
    /// <remarks>
    /// Returns the array itself rather than a copy, exactly as <c>GetBoneForWrite</c> returns a
    /// reference — a stage writes into the accessor and the next stage reads what it wrote. That is
    /// how the merge and the parent chain compose (<c>c_baseanimating.cpp:1595</c>).
    /// </remarks>
    public float[] BoneForWrite(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _bones.Length);

        return _bones[bone];
    }

    /// <summary>Every bone, for a consumer that takes the whole set.</summary>
    public IReadOnlyList<float[]> Bones => _bones;
}
