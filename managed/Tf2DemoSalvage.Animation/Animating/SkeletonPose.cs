using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A real studio skeleton, driven by whatever the animation says its bones are doing.
/// </summary>
/// <remarks>
/// **The adapter between the architecture and a model.** <see cref="AnimatingEntity"/> owns the
/// caching, the masks and the recursion and knows nothing about <c>.mdl</c> files; this knows about
/// one model and nothing about when it is asked. That is where the SDK splits too —
/// <c>C_BaseAnimating::SetupBones</c> against <c>IBoneSetup</c> and <c>BuildTransformations</c>.
///
/// **The parent transform is read out of the ACCESSOR, not from a private array**, which is the
/// whole reason this composes with the merge. <c>c_baseanimating.cpp:1595</c> is
/// <c>ConcatTransforms( GetBone( hdr-&gt;boneParent(i) ), bonematrix, GetBoneForWrite( i ) )</c> —
/// the same array the merge has already written into — so a bone whose parent came from a wearer
/// rides the wearer's position without anything here knowing a merge happened.
/// </remarks>
public sealed class SkeletonPose : IBonePose
{
    private readonly IReadOnlyList<StudioBone> _bones;
    private readonly Func<double, IReadOnlyList<StudioBonePose>> _animation;

    /// <summary>Creates a pose source over one model's skeleton.</summary>
    /// <param name="bones">The skeleton, as <see cref="StudioBones.Read"/> returned it.</param>
    /// <param name="animation">
    /// Given demo time, the local positions and rotations the animation overrides. Bones it omits
    /// keep their rest values, which is most of the skeleton for most animations.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SkeletonPose(
        IReadOnlyList<StudioBone> bones,
        Func<double, IReadOnlyList<StudioBonePose>> animation)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(animation);

        _bones = bones;
        _animation = animation;
        _local = new float[bones.Count][];

        for (int bone = 0; bone < bones.Count; bone++)
        {
            _local[bone] = new float[12];
        }
    }

    /// <summary>Scratch space for one bone's local transform, reused across frames.</summary>
    /// <remarks>
    /// Allocated once per entity, per D87. This runs once per bone per entity per frame, and a
    /// fresh twelve-float array each time is kilobytes a frame through the collector for nothing.
    /// </remarks>
    private readonly float[][] _local;

    /// <inheritdoc/>
    public int BoneCount => _bones.Count;

    /// <inheritdoc/>
    public int FlagsOf(int bone) => _bones[bone].Flags;

    /// <inheritdoc/>
    public string NameOf(int bone) => _bones[bone].Name;

    /// <inheritdoc/>
    /// <remarks>
    /// **Three reasons a bone is skipped, and they are not the same reason.**
    ///
    /// <list type="bullet">
    /// <item><b>Already written</b> — the merge or, later, the IK solver put it there, and its
    /// children concatenate onto it. Rebuilding it from this model's own animation would undo the
    /// merge (<c>c_baseanimating.cpp:1519</c>).</item>
    /// <item><b>Outside the mask</b> — the caller does not need it. Valve's own first line in the
    /// loop (<c>:1516</c>). This assumes a bone's parents carry at least its own use bits, which
    /// studiomdl guarantees when it compiles the flags; a model that violated it would build a
    /// child on a stale parent.</item>
    /// <item><b>No parent yet built</b> — a malformed skeleton whose parent index points forward.
    /// The bone is written from its local transform alone rather than from a matrix that is still
    /// identity, so it draws unmoved instead of somewhere arbitrary.</item>
    /// </list>
    ///
    /// **Procedural bones are NOT handled here and that is a filed gap, not an oversight** (B182).
    /// A bone with <c>BONE_ALWAYS_PROCEDURAL</c> and a rule falls through to the ordinary path, so
    /// it holds its animated position instead of the rule's — which for a jiggle bone is a hat that
    /// does not sway rather than a hat in the wrong place.
    /// </remarks>
    public void Build(int boneMask, double currentTime, BoneAccessor into, BoneBitList alreadyWritten)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(alreadyWritten);

        IReadOnlyList<StudioBonePose> animated = _animation(currentTime);

        // A sparse override list, indexed once rather than searched per bone: an animation naming
        // one elbow would otherwise cost a scan of the whole list for every bone in the skeleton.
        Span<bool> overridden = _bones.Count <= 256 ? stackalloc bool[_bones.Count] : new bool[_bones.Count];

        foreach (StudioBonePose moved in animated)
        {
            if (moved.Bone >= 0 && moved.Bone < _bones.Count)
            {
                overridden[moved.Bone] = true;
            }
        }

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            if (alreadyWritten.IsMarked(bone) || (_bones[bone].Flags & boneMask) == 0)
            {
                continue;
            }

            StudioBone rest = _bones[bone];

            (float X, float Y, float Z, float W) rotation = rest.Rotation;
            (float X, float Y, float Z) position = rest.Position;

            if (overridden[bone])
            {
                foreach (StudioBonePose moved in animated)
                {
                    if (moved.Bone == bone)
                    {
                        rotation = moved.Rotation;
                        position = moved.Position;
                        break;
                    }
                }
            }

            StudioBones.FromQuaternion(rotation, position).CopyTo(_local[bone], 0);

            float[] destination = into.BoneForWrite(bone);

            if (rest.Parent >= 0 && rest.Parent < bone)
            {
                StudioBones.Concatenate(into.Bone(rest.Parent), _local[bone], destination);
            }
            else
            {
                _local[bone].CopyTo(destination, 0);
            }

            alreadyWritten.Mark(bone);
        }
    }
}
