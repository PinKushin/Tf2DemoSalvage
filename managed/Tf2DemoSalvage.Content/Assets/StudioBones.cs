using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One bone's rest position in a model's skeleton.</summary>
/// <param name="Parent">The bone this hangs off, or −1 for the root.</param>
/// <param name="Position">Where it sits relative to its parent.</param>
/// <param name="Rotation">How it is turned relative to its parent, as a quaternion.</param>
/// <param name="PoseToBone">Model space into this bone's space, as a 3×4 row-major matrix.</param>
public readonly record struct StudioBone(
    int Parent,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z, float W) Rotation,
    ReadOnlyMemory<float> PoseToBone);

/// <summary>A model's skeleton, resolved to the matrices that move its vertices.</summary>
/// <remarks>
/// **A type rather than an array of arrays**, so the matrices and the multiply that uses them stay
/// together. A caller holding raw matrices has to know the row-major layout to use them, and that
/// knowledge belongs next to the code that built them.
/// </remarks>
public sealed class StudioSkeleton
{
    private readonly float[][] _skinning;

    internal StudioSkeleton(float[][] skinning) => _skinning = skinning;

    /// <summary>How many bones the model has.</summary>
    public int Count => _skinning.Length;

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
    /// <summary><c>studiohdr_t.numbones</c>.</summary>
    /// <remarks>
    /// Pinned by its neighbours rather than counted by hand: <c>numtextures</c> at 204 is already
    /// read by <see cref="StudioModel"/> and verified against real files, and the fields between
    /// are all four bytes, so 156 follows from the published order in <c>studio.h</c>.
    /// </remarks>
    private const int BoneCountOffset = 156;

    /// <summary><c>studiohdr_t.boneindex</c>.</summary>
    private const int BoneIndexOffset = 160;

    /// <summary>
    /// Bytes per <c>mstudiobone_t</c>: sznameindex, parent, bonecontroller[6], pos, quat, rot,
    /// posscale, rotscale, poseToBone, qAlignment, six ints and unused[8].
    /// </summary>
    private const int BoneStride = 216;

    private const int ParentOffset = 4;
    private const int PositionOffset = 32;
    private const int RotationOffset = 44;
    private const int PoseToBoneOffset = 96;

    /// <summary>Most bones a model may declare, as a guard against a malformed header.</summary>
    private const int MaximumBones = 1024;

    /// <summary>Reads a model's skeleton.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The bones in file order, so a parent index addresses this list directly.</returns>
    /// <exception cref="InvalidDataException">The header names more bones than it holds.</exception>
    public static IReadOnlyList<StudioBone> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < BoneIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[BoneCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[BoneIndexOffset..]);

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
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(PoseToBoneOffset + (cell * 4))..]);
            }

            bones.Add(new StudioBone(
                BinaryPrimitives.ReadInt32LittleEndian(bone[ParentOffset..]),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[PositionOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(PositionOffset + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(PositionOffset + 8)..])),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[RotationOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(RotationOffset + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(RotationOffset + 8)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(RotationOffset + 12)..])),
                poseToBone));
        }

        return bones;
    }

    /// <summary>The matrix that moves each bone's vertices into the model's rest pose.</summary>
    /// <param name="bones">The skeleton, as <see cref="Read"/> returned it.</param>
    /// <returns>One 3×4 row-major matrix per bone.</returns>
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

        return new StudioSkeleton(skinning);
    }

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
