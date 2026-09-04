using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// TF2's three per-bone scales: head, torso and hands.
/// </summary>
/// <remarks>
/// **`C_TFPlayer::BuildTransformations` ends by running all three, unconditionally**
/// (`c_tf_player.cpp:8815`), and `C_TFRagdoll` repeats them (`:8831`):
///
/// <code>
///   float flHeadScale = m_Shared.InCond( TF_COND_HALLOWEEN_GHOST_MODE ) ? 1.5 : m_flHeadScale;
///   BuildBigHeadTransformations( …, flHeadScale );
///   BuildTorsoScaleTransformations( …, m_flTorsoScale, GetPlayerClass()->GetClassIndex() );
///   BuildHandScaleTransformations( …, m_flHandScale );
/// </code>
///
/// **All three fields are networked and this project read none of them** (B312) —
/// `m_flHeadScale`, `m_flTorsoScale` and `m_flHandScale` are `RecvPropFloat` on `DT_TFPlayer`
/// (`c_tf_player.cpp:539`) and appear in the send tables of every demo checked.
///
/// **Their absence was invisible because each defaults to 1**, and every one of these functions
/// opens with `if ( !pObject || flScale == 1.f ) return;`. A field that multiplies a scale and
/// defaults to one draws an identical picture when ignored, so no rendering comparison and no count
/// could ever have caught it — the same shape as `m_flPlaybackRate`, which was decoded, retained,
/// unit-tested and read by nothing while every animation played at rate 1.
///
/// **Measured: 440 of 440 values on `z1800` are exactly 1** for all three, so this changes nothing
/// on an ordinary match. It changes a Halloween recording, an MvM one, or any server whose plugin
/// sets them — and the corpus has none of those, which is why the number is recorded with the
/// command that produced it rather than as a conclusion.
///
/// **A post-pass over finished MODEL-SPACE matrices**, not part of the blend: the engine runs it
/// after the pose is composed, on `GetBoneForWrite`.
/// </remarks>
public static class PlayerBoneScales
{
    /// <summary>Scales the head, and pushes any hat or helmet out to sit on it.</summary>
    /// <param name="bones">The finished model-space matrices, written in place.</param>
    /// <param name="skeleton">The bones, for their names.</param>
    /// <param name="scale">`m_flHeadScale`; 1 does nothing.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **`MatrixScaleBy` scales the 3×3 basis and leaves the translation** (`mathlib_base.cpp:430`),
    /// so the head grows where it stands. The hat is then moved by hand —
    /// `MatrixSetTranslation( ( offset * flScale ) + head_position, … )` — because scaling its basis
    /// alone would leave it buried inside a skull twice the size, which reads as a missing hat
    /// rather than as a misplaced one.
    ///
    /// **Two hat bones, and Valve checks both**: `prp_helmet` and `prp_hat`.
    /// </remarks>
    public static void Head(BoneAccessor bones, IReadOnlyList<StudioBone> skeleton, float scale)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(skeleton);

        if (Unscaled(scale) || Lookup(skeleton, "bip_head") is not { } head)
        {
            return;
        }

        (float X, float Y, float Z) at = Position(bones.Bone(head));

        ScaleBasis(bones.BoneForWrite(head), scale);

        foreach (string name in (string[])["prp_helmet", "prp_hat"])
        {
            if (Lookup(skeleton, name) is not { } hat)
            {
                continue;
            }

            float[] matrix = bones.BoneForWrite(hat);
            (float X, float Y, float Z) worn = Position(matrix);

            ScaleBasis(matrix, scale);

            matrix[3] = ((worn.X - at.X) * scale) + at.X;
            matrix[7] = ((worn.Y - at.Y) * scale) + at.Y;
            matrix[11] = ((worn.Z - at.Z) * scale) + at.Z;
        }
    }

    /// <summary>Compresses the spine toward the pelvis, carrying everything hanging off it.</summary>
    /// <param name="bones">The finished model-space matrices, written in place.</param>
    /// <param name="skeleton">The bones, for their names and parents.</param>
    /// <param name="scale">`m_flTorsoScale`; 1 does nothing.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A COMPRESSION, not a scale — no basis is touched.** Each spine bone is pulled toward the
    /// one below it, `vTargetBonePos + flScale * ( vMoveBonePos - vTargetBonePos )`, with the pelvis
    /// as the first target and **each moved bone becoming the next target**, so the run is
    /// cumulative and its order is load-bearing. Valve's own comment says *"must be in this order"*.
    ///
    /// **A missing bone stops the run where it stands, and does NOT undo what it already did.** The
    /// `return` is INSIDE the loop, so every spine bone before the gap stays moved and the model is
    /// left half compressed. This comment first claimed the opposite — that Valve must bail before
    /// touching anything, because half a compression is worse than none — which is an argument from
    /// taste about what the engine ought to do. It does the other thing, and a test written from
    /// the reasoning rather than the branch encoded the mistake.
    ///
    /// **Each moved bone drags its descendants by the offset it moved**, which is what keeps the
    /// arms and head attached instead of floating where the spine used to be.
    /// </remarks>
    public static void Torso(BoneAccessor bones, IReadOnlyList<StudioBone> skeleton, float scale)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(skeleton);

        if (Unscaled(scale) || Lookup(skeleton, "bip_pelvis") is not { } pelvis)
        {
            return;
        }

        int target = pelvis;

        foreach (string name in Spine)
        {
            if (Lookup(skeleton, name) is not { } move)
            {
                return;
            }

            (float X, float Y, float Z) to = Position(bones.Bone(target));

            float[] matrix = bones.BoneForWrite(move);
            (float X, float Y, float Z) from = Position(matrix);

            (float X, float Y, float Z) moved = (
                to.X + (scale * (from.X - to.X)),
                to.Y + (scale * (from.Y - to.Y)),
                to.Z + (scale * (from.Z - to.Z)));

            matrix[3] = moved.X;
            matrix[7] = moved.Y;
            matrix[11] = moved.Z;

            target = move;

            (float X, float Y, float Z) offset =
                (moved.X - from.X, moved.Y - from.Y, moved.Z - from.Z);

            foreach (int child in Descendants(skeleton, move))
            {
                float[] hanging = bones.BoneForWrite(child);

                hanging[3] += offset.X;
                hanging[7] += offset.Y;
                hanging[11] += offset.Z;
            }
        }
    }

    /// <summary>Scales each hand and everything hanging off it.</summary>
    /// <param name="bones">The finished model-space matrices, written in place.</param>
    /// <param name="skeleton">The bones, for their names and parents.</param>
    /// <param name="scale">`m_flHandScale`; 1 does nothing.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A missing hand is skipped rather than abandoning the pass — `continue`, not `return`**,
    /// which is the opposite of the torso's choice and for a reason: the two hands are independent,
    /// where the spine is a chain whose later steps depend on the earlier ones.
    ///
    /// **Descendants are scaled too**, because a finger is a child of the hand and would otherwise
    /// keep its original size on a giant palm.
    /// </remarks>
    public static void Hands(BoneAccessor bones, IReadOnlyList<StudioBone> skeleton, float scale)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(skeleton);

        if (Unscaled(scale))
        {
            return;
        }

        foreach (string name in (string[])["bip_hand_L", "bip_hand_R"])
        {
            if (Lookup(skeleton, name) is not { } hand)
            {
                continue;
            }

            ScaleBasis(bones.BoneForWrite(hand), scale);

            foreach (int child in Descendants(skeleton, hand))
            {
                ScaleBasis(bones.BoneForWrite(child), scale);
            }
        }
    }

    /// <summary>The spine bones the torso pass walks, in Valve's stated order.</summary>
    private static readonly string[] Spine =
        ["bip_spine_0", "bip_spine_1", "bip_spine_2", "bip_spine_3", "bip_neck"];

    /// <summary>Valve's <c>flScale == 1.f</c> early-out, and it is an EXACT comparison.</summary>
    /// <remarks>
    /// **Exact because the engine's is** — `if ( !pObject || flScale == 1.f ) return;`. A tolerance
    /// here would skip a scale of 0.9999 the engine applies, and the value arrives off the wire as
    /// a float the server chose rather than as the result of a computation.
    /// </remarks>
#pragma warning disable S1244
    private static bool Unscaled(float scale) => scale == 1f;
#pragma warning restore S1244

    /// <summary>A bone by name, or null when the model has none — <c>LookupBone</c>.</summary>
    private static int? Lookup(IReadOnlyList<StudioBone> skeleton, string name)
    {
        for (int bone = 0; bone < skeleton.Count; bone++)
        {
            if (string.Equals(skeleton[bone].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bone;
            }
        }

        return null;
    }

    /// <summary>Every bone below one, at any depth — <c>AppendChildren_R</c>.</summary>
    /// <remarks>
    /// **Bones are ordered parents-before-children in a studio model**, so one forward pass finds
    /// every descendant: a bone is below the subject when its parent is the subject or is itself
    /// already known to be below it. The engine recurses instead; the result is the same set and
    /// this cannot be made to recurse on a malformed parent list.
    /// </remarks>
    private static List<int> Descendants(IReadOnlyList<StudioBone> skeleton, int of)
    {
        List<int> below = [];
        HashSet<int> known = [of];

        for (int bone = 0; bone < skeleton.Count; bone++)
        {
            if (bone != of && known.Contains(skeleton[bone].Parent))
            {
                known.Add(bone);
                below.Add(bone);
            }
        }

        return below;
    }

    /// <summary>Multiplies the 3×3 and leaves the translation — <c>MatrixScaleBy</c>.</summary>
    private static void ScaleBasis(float[] matrix, float scale)
    {
        matrix[0] *= scale;
        matrix[1] *= scale;
        matrix[2] *= scale;
        matrix[4] *= scale;
        matrix[5] *= scale;
        matrix[6] *= scale;
        matrix[8] *= scale;
        matrix[9] *= scale;
        matrix[10] *= scale;
    }

    private static (float X, float Y, float Z) Position(ReadOnlySpan<float> matrix) =>
        (matrix[3], matrix[7], matrix[11]);
}
