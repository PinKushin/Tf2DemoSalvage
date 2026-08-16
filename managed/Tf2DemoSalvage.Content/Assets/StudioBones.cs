using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One bone's rest position in a model's skeleton.</summary>
/// <param name="Name">Its name, which is how one model's bones are matched to another's.</param>
/// <param name="Parent">The bone this hangs off, or −1 for the root.</param>
/// <param name="Position">Where it sits relative to its parent.</param>
/// <param name="Rotation">How it is turned relative to its parent, as a quaternion.</param>
/// <param name="PoseToBone">Model space into this bone's space, as a 3×4 row-major matrix.</param>
/// <param name="Euler">The same rest rotation as radians, which animation adds its angles to.</param>
/// <param name="PositionScale">What an animation's compressed position values are multiplied by.</param>
/// <param name="RotationScale">What an animation's compressed rotation values are multiplied by.</param>
/// <remarks>
/// **The rotation is stored twice, and both are needed.** <c>quat</c> is the rest pose the renderer
/// uses directly; <c>rot</c> is the same rotation as Euler radians, and an animation's compressed
/// channels are added to THAT before being turned back into a quaternion
/// (<c>bone_setup.cpp:417</c>). Adding them to the quaternion instead is meaningless.
/// </remarks>
public readonly record struct StudioBone(
    string Name,
    int Parent,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z, float W) Rotation,
    ReadOnlyMemory<float> PoseToBone,
    (float X, float Y, float Z) Euler = default,
    (float X, float Y, float Z) PositionScale = default,
    (float X, float Y, float Z) RotationScale = default);

/// <summary>A model's skeleton, resolved to the matrices that move its vertices.</summary>
/// <remarks>
/// **A type rather than an array of arrays**, so the matrices and the multiply that uses them stay
/// together. A caller holding raw matrices has to know the row-major layout to use them, and that
/// knowledge belongs next to the code that built them.
/// </remarks>
public sealed class StudioSkeleton
{
    private readonly float[][] _skinning;
    private readonly float[][] _boneToWorld;

    internal StudioSkeleton(float[][] skinning)
        : this(skinning, skinning)
    {
    }

    internal StudioSkeleton(float[][] skinning, float[][] boneToWorld)
    {
        _skinning = skinning;
        _boneToWorld = boneToWorld;
    }

    /// <summary>Where each bone itself is, before the bind pose is undone.</summary>
    /// <remarks>
    /// **Bone merging needs this and skinning matrices cannot supply it.** A skinning matrix is
    /// <c>boneToWorld * poseToBone</c> — the bind pose is already folded in, and it is the WEARER's
    /// bind pose. Copying one into a worn item's slot is right only where the two models were bound
    /// identically, which is why a fully-matched hat looks fine and a partly-matched one tears: an
    /// unmatched bone has to be built from its parent's position, and a skinning matrix does not
    /// say where its bone is.
    /// </remarks>
    public IReadOnlyList<float[]> BoneToWorld => _boneToWorld;

    /// <summary>How many bones the model has.</summary>
    public int Count => _skinning.Length;

    /// <summary>The matrices themselves, for a renderer that skins on the GPU.</summary>
    /// <remarks>
    /// **Exposed because the transform can happen in two places.** <see cref="Skin"/> applies them
    /// here, which is what a model with its frames baked wants — the work happens once at load. A
    /// model too large to bake is skinned per draw instead, and then the matrices themselves are
    /// what the shader needs, as constants.
    ///
    /// Row-major three-by-four, twelve floats each, which is the studio format's own layout and
    /// the one the shader reads.
    /// </remarks>
    public IReadOnlyList<float[]> Matrices => _skinning;

    /// <summary>Whether the model has a skeleton at all.</summary>
    /// <remarks>
    /// A model compiled with <c>$staticprop</c> still declares one bone, so this being false is
    /// rare; what distinguishes a static prop is that its vertices name no bone weights.
    /// </remarks>
    public bool IsEmpty => _skinning.Length == 0;

    /// <summary>Moves one vertex by its bones, as the renderer would.</summary>
    /// <param name="bones">Which bones move this vertex.</param>
    /// <param name="weights">How much each moves it.</param>
    /// <param name="x">Model-space position.</param>
    /// <param name="y">Model-space position.</param>
    /// <param name="z">Model-space position.</param>
    /// <returns>The vertex in the model's rest pose.</returns>
    /// <remarks>
    /// **A vertex naming no bone is left where it is**, which is the right answer for a model
    /// compiled with <c>$staticprop</c>: its transform is already baked in, and moving it by a
    /// skeleton it does not have would be inventing a pose.
    ///
    /// Weights summing to nothing are treated the same way rather than divided by, since a single
    /// vertex sent to infinity stretches a triangle across the whole map.
    /// </remarks>
    public (float X, float Y, float Z) Skin(
        (byte First, byte Second, byte Third) bones,
        (float First, float Second, float Third) weights,
        float x,
        float y,
        float z)
    {
        if (_skinning.Length == 0)
        {
            return (x, y, z);
        }

        Span<byte> indices = [bones.First, bones.Second, bones.Third];
        Span<float> amounts = [weights.First, weights.Second, weights.Third];

        float total = amounts[0] + amounts[1] + amounts[2];

        if (total <= 0f)
        {
            return (x, y, z);
        }

        float outX = 0f, outY = 0f, outZ = 0f;

        for (int slot = 0; slot < 3; slot++)
        {
            if (amounts[slot] <= 0f || indices[slot] >= _skinning.Length)
            {
                continue;
            }

            float[] matrix = _skinning[indices[slot]];
            float share = amounts[slot] / total;

            outX += share * ((matrix[0] * x) + (matrix[1] * y) + (matrix[2] * z) + matrix[3]);
            outY += share * ((matrix[4] * x) + (matrix[5] * y) + (matrix[6] * z) + matrix[7]);
            outZ += share * ((matrix[8] * x) + (matrix[9] * y) + (matrix[10] * z) + matrix[11]);
        }

        return (outX, outY, outZ);
    }
}

/// <summary>
/// A model's skeleton, and the matrices that put its vertices where they belong.
/// </summary>
/// <remarks>
/// **Why this exists: without it an animated model draws lying on its side.** A model compiled
/// with <c>$staticprop</c> has its bone transform baked into the vertices, so drawing them raw is
/// correct — which is every prop in a map, and is why this went unnoticed for as long as props
/// were the only models. An animated model does not, and its vertices sit in whatever pose the
/// artist built the skeleton around.
///
/// Measured on cp_process_f12, model-space extents before any skinning:
///
/// <code>
///   resupply_locker.mdl      x 63.8   y 35.4    z 113.2    upright, $staticprop
///   cappoint_hologram.mdl    x 39.2   y 64.9    z 171.5    upright, $staticprop
///   player/soldier.mdl       x 47.9   y 84.5    z 24.8     on its side
///   player/scout.mdl         x 30.8   y 81.7    z 22.2     on its side
/// </code>
///
/// A TF2 player is about 83 units tall, and there is the 83 — on Y. The props are the control:
/// same reader, same code path, standing up correctly.
///
/// **The transform is Valve's, from <c>bone_setup.cpp</c>:** a vertex is moved by
/// <c>BoneToWorld × poseToBone</c> per bone, weighted. <c>poseToBone</c> takes model space into a
/// bone's own space (it is inverted at <c>bone_setup.cpp:1775</c> to go the other way), and
/// <c>BoneToWorld</c> is built by walking the hierarchy from each bone's rest
/// <c>pos</c>/<c>quat</c>. In the rest pose the two are inverses for a model whose vertices were
/// stored in that same pose, and the product is the identity — which is exactly why static props
/// look right without this and cost nothing when it is applied.
///
/// This is the REST pose only. Animation replaces <c>BoneToWorld</c> with a posed one; the
/// <c>poseToBone</c> half never changes.
/// </remarks>
public static class StudioBones
{
    /// <summary>Most bones a model may declare, as a guard against a malformed header.</summary>
    private const int MaximumBones = 1024;

    /// <summary>Reads a model's skeleton.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The bones in file order, so a parent index addresses this list directly.</returns>
    /// <exception cref="InvalidDataException">The header names more bones than it holds.</exception>
    public static IReadOnlyList<StudioBone> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBoneIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneIndexOffset..]);

        if (count <= 0)
        {
            return [];
        }

        if (count > MaximumBones)
        {
            throw new InvalidDataException($"A model declares {count} bones.");
        }

        if (at < 0 || (long)at + ((long)count * BoneStride) > bytes.Length)
        {
            throw new InvalidDataException(
                $"A model's {count} bones at {at} run past its own length of {bytes.Length}.");
        }

        List<StudioBone> bones = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> bone = bytes.Slice(at + (index * BoneStride), BoneStride);

            float[] poseToBone = new float[12];

            for (int cell = 0; cell < 12; cell++)
            {
                poseToBone[cell] =
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BonePoseToBoneOffset + (cell * 4))..]);
            }

            bones.Add(new StudioBone(
                StudioStrings.At(
                    bytes,
                    at + (index * BoneStride) +
                        BinaryPrimitives.ReadInt32LittleEndian(bone[BoneNameOffset..])),
                BinaryPrimitives.ReadInt32LittleEndian(bone[BoneParentOffset..]),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[BonePositionOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BonePositionOffset +4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BonePositionOffset +8)..])),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[BoneRotationOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneRotationOffset +4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneRotationOffset +8)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneRotationOffset +12)..])),
                poseToBone,
                Vector(bone, BoneEulerOffset),
                Vector(bone, BonePositionScaleOffset),
                Vector(bone, BoneRotationScaleOffset)));
        }

        return bones;
    }

    /// <summary>The matrices for a skeleton with an animation applied.</summary>
    /// <param name="bones">The skeleton, as <see cref="Read"/> returned it.</param>
    /// <param name="pose">Bone poses from an animation frame; bones it omits keep their rest value.</param>
    /// <returns>One matrix per bone.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **This is the half that changes.** <c>poseToBone</c> is fixed by the model; what an
    /// animation replaces is the other factor, the bone's transform relative to its parent. So
    /// posing is the rest computation with different local transforms, not a different
    /// computation.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="bones"/> is null.</exception>
    /// <remarks>
    /// **<c>BoneToWorld × poseToBone</c>, which is Valve's own composition.** Each bone's world
    /// transform is its rest <c>pos</c>/<c>quat</c> concatenated onto its parent's, walking down
    /// from the root; <c>poseToBone</c> then brings a model-space vertex into that bone's space
    /// first.
    ///
    /// **Parents always precede children in the file**, which is what makes a single forward pass
    /// correct rather than needing a recursive walk. A model that violated it would produce a
    /// child built on an unfinished parent, so the parent index is bounds-checked against the
    /// bones already computed rather than against the whole list — a malformed skeleton then draws
    /// its own bone unmoved instead of reading a matrix that is still zeroes.
    /// </remarks>
    public static StudioSkeleton Posed(
        IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(pose);

        if (pose.Count == 0)
        {
            return RestPose(bones);
        }

        // **A bone the animation does not mention keeps its rest value**, which is most of the
        // skeleton for most animations: an animation that only turns an elbow names one bone.
        StudioBone[] posed = [.. bones];

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone >= 0 && moved.Bone < posed.Length)
            {
                posed[moved.Bone] = posed[moved.Bone] with
                {
                    Position = moved.Position,
                    Rotation = moved.Rotation,
                };
            }
        }

        return RestPose(posed);
    }

    /// <summary>The matrix that moves each bone's vertices into the model's rest pose.</summary>
    /// <param name="bones">The skeleton, as <see cref="Read"/> returned it.</param>
    /// <returns>One matrix per bone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bones"/> is null.</exception>
    public static StudioSkeleton RestPose(IReadOnlyList<StudioBone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);

        float[][] boneToWorld = new float[bones.Count][];
        float[][] skinning = new float[bones.Count][];

        for (int index = 0; index < bones.Count; index++)
        {
            StudioBone bone = bones[index];

            float[] local = FromQuaternion(bone.Rotation, bone.Position);

            boneToWorld[index] = bone.Parent >= 0 && bone.Parent < index
                ? Concatenate(boneToWorld[bone.Parent], local)
                : local;

            skinning[index] = Concatenate(boneToWorld[index], bone.PoseToBone.Span);
        }

        return new StudioSkeleton(skinning, boneToWorld);
    }

    /// <summary>Poses one model's bones from another's, the way a bone merge does.</summary>
    /// <param name="bones">The worn model's skeleton, which decides the numbering of the result.</param>
    /// <param name="wearer">Where the wearer's bones are, as <see cref="StudioSkeleton.BoneToWorld"/>.</param>
    /// <param name="map">
    /// For each of <paramref name="bones"/>, the wearer bone it matches, or −1. <see cref="Remap"/>
    /// produces it.
    /// </param>
    /// <returns>Skinning matrices in the worn model's bone order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Valve copies only the bones that match, and the rest are NOT left alone.** <c>
    /// CBoneMergeCache::MergeMatchingBones</c> runs after the worn model has done its own full
    /// <c>SetupBones</c>, so an unmatched bone already holds a position built by walking the worn
    /// model's own hierarchy — from its parent, which may itself have been merged. Copying the
    /// matches and leaving everything else at its rest position in the model's OWN space is a
    /// different thing entirely, and it tears the model apart: measured on a scout, a
    /// <c>ghostly_gibus</c> matched 1 bone of 8 and the other seven stayed at the model origin
    /// while the matched one sat at head height, so the triangles between them stretched from the
    /// player's head to their feet as a large flat sheet.
    ///
    /// So the chain is walked here, in bone-to-world space, and only then folded with the WORN
    /// model's own <c>poseToBone</c> — its bind pose, not the wearer's.
    ///
    /// **Bones are in hierarchy order and a parent's index is below its child's.** The studio
    /// format guarantees it, and <see cref="RestPose"/> already relies on the same thing, so one
    /// pass suffices.
    /// </remarks>
    public static IReadOnlyList<float[]> MergeOnto(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<float[]> wearer,
        IReadOnlyList<int> map)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(wearer);
        ArgumentNullException.ThrowIfNull(map);

        float[][] boneToWorld = new float[bones.Count][];
        float[][] skinning = new float[bones.Count][];

        for (int index = 0; index < bones.Count; index++)
        {
            StudioBone bone = bones[index];
            int matched = index < map.Count ? map[index] : -1;

            if (matched >= 0 && matched < wearer.Count)
            {
                boneToWorld[index] = wearer[matched];
            }
            else
            {
                float[] local = FromQuaternion(bone.Rotation, bone.Position);

                boneToWorld[index] = bone.Parent >= 0 && bone.Parent < index
                    ? Concatenate(boneToWorld[bone.Parent], local)
                    : local;
            }

            skinning[index] = Concatenate(boneToWorld[index], bone.PoseToBone.Span);
        }

        return skinning;
    }

    /// <summary>Maps one model's bone numbering onto another's, by name.</summary>
    /// <param name="from">The bones an animation's indices refer to.</param>
    /// <param name="to">The bones a pose is to be applied to.</param>
    /// <returns>An index into <paramref name="to"/> for each bone in <paramref name="from"/>, or −1.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **This is Valve's <c>masterBone</c>**, which <c>studio.h</c> describes as mapping a local
    /// bone to a global one and which <c>bone_setup.cpp:966</c> applies to every animation it
    /// reads: <c>int j = pAnimGroup-&gt;masterBone[panim-&gt;bone];</c>.
    ///
    /// **Without it a pose is scrambled rather than absent, which is worse.** An animation model
    /// numbers its own bones, and applying those numbers to the base model's skeleton moves the
    /// wrong joints by the right amounts — measured on a soldier as extents of 56 by 66 by 65,
    /// roughly cubical, where a standing player is about 25 by 48 by 83. It looked like a model
    /// sitting up rather than one lying down, which is exactly what a partly-correct skeleton is.
    ///
    /// Matched by name because that is what makes an animation model shareable in the first place.
    /// </remarks>
    public static int[] Remap(IReadOnlyList<StudioBone> from, IReadOnlyList<StudioBone> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < to.Count; index++)
        {
            byName.TryAdd(to[index].Name, index);
        }

        int[] remap = new int[from.Count];

        for (int index = 0; index < from.Count; index++)
        {
            remap[index] = byName.TryGetValue(from[index].Name, out int found) ? found : -1;
        }

        return remap;
    }

    private static (float X, float Y, float Z) Vector(ReadOnlySpan<byte> bone, int at) =>
        (
            BinaryPrimitives.ReadSingleLittleEndian(bone[at..]),
            BinaryPrimitives.ReadSingleLittleEndian(bone[(at + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bone[(at + 8)..]));

    /// <summary>A quaternion and a position as a 3×4 row-major matrix.</summary>
    /// <remarks>
    /// Valve's <c>QuaternionMatrix</c>, which is the standard expansion. Written out rather than
    /// called through a vector library so the row-major layout is visible beside the multiply in
    /// <see cref="StudioSkeleton.Skin"/> — a transposed rotation is the classic silent failure here, and it looks
    /// like a model turned inside out rather than like an error.
    /// </remarks>
    private static float[] FromQuaternion(
        (float X, float Y, float Z, float W) rotation, (float X, float Y, float Z) position)
    {
        float x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;

        return
        [
            1f - (2f * ((y * y) + (z * z))), 2f * ((x * y) - (z * w)), 2f * ((x * z) + (y * w)), position.X,
            2f * ((x * y) + (z * w)), 1f - (2f * ((x * x) + (z * z))), 2f * ((y * z) - (x * w)), position.Y,
            2f * ((x * z) - (y * w)), 2f * ((y * z) + (x * w)), 1f - (2f * ((x * x) + (y * y))), position.Z,
        ];
    }

    /// <summary>One 3×4 transform applied after another.</summary>
    private static float[] Concatenate(ReadOnlySpan<float> first, ReadOnlySpan<float> second)
    {
        float[] result = new float[12];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                result[(row * 4) + column] =
                    (first[(row * 4) + 0] * second[column]) +
                    (first[(row * 4) + 1] * second[4 + column]) +
                    (first[(row * 4) + 2] * second[8 + column]);
            }

            result[(row * 4) + 3] =
                (first[(row * 4) + 0] * second[3]) +
                (first[(row * 4) + 1] * second[7]) +
                (first[(row * 4) + 2] * second[11]) +
                first[(row * 4) + 3];
        }

        return result;
    }
}
