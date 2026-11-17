using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// `WorldSpaceSlerp` (bone_setup.cpp:1249-1365): a `STUDIO_WORLD` sequence blended onto a pose in world space, the result
/// written back as locals (B741).
/// </summary>
/// <remarks>
/// **Ported line for line, its oddities with it.**
/// - The pose's chain is built lazily, bone by bone, from the pose as it stands when first asked
///   (`BuildBoneChain` with its computed bits): an ancestor already back-solved is read as blended, one never asked for is
///   read as it is now.
/// - A root's position is not blended. It keeps the pose's, while its rotation is solved from the slerped world matrix.
/// - A bone whose own weight and its parent's are both exactly 1 is copied outright.
/// - The parent's target matrix is read whether or not that parent was blended, and the engine's pool memory there is
///   undefined when it was not. *Interpolated:* the parent as the pose now stands, its chain built from the live pose.
/// - The target is built by `AngleMatrix( targetQ, destPos, … )`, which converts through `RadianEuler` and back
///   (`vector.h:1736`, `mathlib_base.cpp:1174-1185`). *Arithmetic:* the same rotation as the quaternion's own matrix,
///   apart from rounding, so the matrix is built directly.
/// - *Interpolated:* a bone the sequence's sample does not name is the bind pose (the identity for a delta animation),
///   where the engine's `q2` holds whatever the pool held.
/// </remarks>
public static class WorldSpaceBlend
{
    /// <summary>The three matrix pools and their computed bits, kept between calls.</summary>
    internal sealed class Workspace
    {
        internal float[] Destination { get; private set; } = [];

        internal float[] Source { get; private set; } = [];

        internal float[] Target { get; private set; } = [];

        internal bool[] DestinationBuilt { get; private set; } = [];

        internal bool[] SourceBuilt { get; private set; } = [];

        internal bool[] TargetBuilt { get; private set; } = [];

        /// <summary>Sizes the pools for <paramref name="count"/> bones and clears every computed bit.</summary>
        internal void Reset(int count)
        {
            if (DestinationBuilt.Length < count)
            {
                Destination = new float[count * 12];
                Source = new float[count * 12];
                Target = new float[count * 12];
                DestinationBuilt = new bool[count];
                SourceBuilt = new bool[count];
                TargetBuilt = new bool[count];
            }

            Array.Clear(DestinationBuilt);
            Array.Clear(SourceBuilt);
            Array.Clear(TargetBuilt);
        }
    }

    /// <summary>Blends <paramref name="layer"/> onto <paramref name="pose"/> in world space, in place.</summary>
    /// <param name="bones">The skeleton: each bone's parent and flags.</param>
    /// <param name="boneMask">The bones being built; others are left alone.</param>
    /// <param name="pose">`q1` and `pos1`, one entry per bone, written in place.</param>
    /// <param name="layer">`q2` and `pos2`, one entry per bone.</param>
    /// <param name="weights">`seqdesc.weight( i )` per bone; a bone past the list weighs 0.</param>
    /// <param name="weight">`s`, already clamped to one by `SlerpBones`.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Slerp(
        IReadOnlyList<StudioBone> bones,
        int boneMask,
        StudioBonePose[] pose,
        IReadOnlyList<StudioBonePose> layer,
        IReadOnlyList<float> weights,
        float weight) =>
        Slerp(bones, boneMask, pose, layer, weights, weight, new Workspace());

    /// <summary>The same, with the pools kept by the caller.</summary>
    internal static void Slerp(
        IReadOnlyList<StudioBone> bones,
        int boneMask,
        StudioBonePose[] pose,
        IReadOnlyList<StudioBonePose> layer,
        IReadOnlyList<float> weights,
        float weight,
        Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(weights);

        int count = Math.Min(bones.Count, Math.Min(pose.Length, layer.Count));
        workspace.Reset(count);

        float[] destination = workspace.Destination;
        float[] source = workspace.Source;
        float[] target = workspace.Target;
        bool[] destinationBuilt = workspace.DestinationBuilt;
        bool[] sourceBuilt = workspace.SourceBuilt;
        bool[] targetBuilt = workspace.TargetBuilt;
        Span<float> worldToBone = stackalloc float[12];
        Span<float> local = stackalloc float[12];

        for (int bone = 0; bone < count; bone++)
        {
            if ((bones[bone].Flags & boneMask) == 0)
            {
                continue;
            }

            int parent = bones[bone].Parent;
            float s2 = weight * WeightOf(weights, bone);
            float s1 = parent != -1 ? weight * WeightOf(weights, parent) : 0f;

            // `if (s1 == 1.0 && s2 == 1.0)`, exactly, as `IsCopy` reads a weight of one.
            if (s1 is >= 1f and <= 1f && s2 is >= 1f and <= 1f)
            {
                pose[bone] = new StudioBonePose(bone, layer[bone].Position, layer[bone].Rotation);
                continue;
            }

            if (s2 <= 0f)
            {
                continue;
            }

            Chain(bones, pose, bone, destination, destinationBuilt);
            Chain(bones, layer, bone, source, sourceBuilt);

            ReadOnlySpan<float> destinationMatrix = destination.AsSpan(bone * 12, 12);
            (float X, float Y, float Z, float W) destinationRotation = StudioBones.ToQuaternion(destinationMatrix);
            (float X, float Y, float Z) destinationPosition = (destinationMatrix[3], destinationMatrix[7], destinationMatrix[11]);
            (float X, float Y, float Z, float W) sourceRotation = StudioBones.ToQuaternion(source.AsSpan(bone * 12, 12));

            (float X, float Y, float Z, float W) blended = StudioBones.Slerp(destinationRotation, sourceRotation, s2);
            StudioBones.FromQuaternion(blended, destinationPosition, target.AsSpan(bone * 12, 12));
            targetBuilt[bone] = true;

            // "back solve"
            if (parent == -1)
            {
                pose[bone] = new StudioBonePose(bone, pose[bone].Position, StudioBones.ToQuaternion(target.AsSpan(bone * 12, 12)));
                continue;
            }

            Span<float> parentToWorld;

            if (targetBuilt[parent])
            {
                parentToWorld = target.AsSpan(parent * 12, 12);
            }
            else
            {
                Chain(bones, pose, parent, destination, destinationBuilt);
                parentToWorld = destination.AsSpan(parent * 12, 12);
            }

            StudioBones.Invert(parentToWorld, worldToBone);
            StudioBones.Concatenate(worldToBone, target.AsSpan(bone * 12, 12), local);

            // "blend bone lengths (local space)": `Lerp( s2, pos1, pos2 )`, `a + ( b - a ) * f`.
            (float X, float Y, float Z) from = pose[bone].Position;
            (float X, float Y, float Z) to = layer[bone].Position;

            pose[bone] = new StudioBonePose(
                bone,
                (from.X + ((to.X - from.X) * s2), from.Y + ((to.Y - from.Y) * s2), from.Z + ((to.Z - from.Z) * s2)),
                StudioBones.ToQuaternion(local));
        }
    }

    /// <summary>`seqdesc.weight( i )`, 0 past the list.</summary>
    private static float WeightOf(IReadOnlyList<float> weights, int bone) => bone < weights.Count ? weights[bone] : 0f;

    /// <summary>
    /// `BuildBoneChain` (bone_setup.cpp:250) with an identity root: a bone's matrix in the model's space, its ancestors
    /// first, each built once.
    /// </summary>
    private static void Chain(IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose, int bone, float[] built, bool[] done)
    {
        if (done[bone])
        {
            return;
        }

        Span<float> matrix = stackalloc float[12];
        StudioBones.FromQuaternion(pose[bone].Rotation, pose[bone].Position, matrix);

        int parent = bones[bone].Parent;
        Span<float> into = built.AsSpan(bone * 12, 12);

        if (parent < 0)
        {
            // `ConcatTransforms( rootxform, bonematrix, … )` with the identity: the bone's own matrix.
            matrix.CopyTo(into);
        }
        else
        {
            Chain(bones, pose, parent, built, done);
            StudioBones.Concatenate(built.AsSpan(parent * 12, 12), matrix, into);
        }

        done[bone] = true;
    }
}
