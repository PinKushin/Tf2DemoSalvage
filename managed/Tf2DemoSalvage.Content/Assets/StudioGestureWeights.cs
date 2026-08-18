using System;
using System.Buffers.Binary;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How much a sequence moves each bone when it is layered as a gesture rather than played alone.
/// </summary>
/// <remarks>
/// **The first piece of B112.** Composing a gesture over a body pose is not "blend the layer in by
/// its own weight" — <c>SlerpBones</c> (<c>bone_setup.cpp:1373</c>) computes each bone's factor as
///
/// <code>
/// pS2[i] = s * seqdesc.weight( i );	// blend in based on this bone's weight
/// </code>
///
/// where <c>s</c> is the layer's own weight and <c>seqdesc.weight(i)</c> is THIS, a value the
/// sequence itself authored per bone. A jump-land gesture that only moves the legs has zero here for
/// every bone above the hips; multiplying by the layer weight alone would drag the whole skeleton
/// toward the gesture's pose regardless of what it was authored to touch.
///
/// Used identically whether the sequence is <c>STUDIO_DELTA</c> (additive) or not — the weight list
/// gates both branches of <c>SlerpBones</c> the same way, only the composition after it differs.
/// </remarks>
public static class StudioGestureWeights
{
    /// <summary>One sequence's per-bone weight, for a model with a known bone count.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <param name="sequence">Which sequence, by the same index <c>StudioSequences.Read</c> uses.</param>
    /// <param name="boneCount">How many bones the model declares.</param>
    /// <returns>One weight per bone, or an empty array when the sequence cannot be read.</returns>
    /// <remarks>
    /// **The count is the caller's to supply rather than this reader's to find**, because the
    /// weight list has no length of its own in the file — <c>pBoneweight(i)</c> is a raw pointer
    /// walk with no bound. The only correct extent is however many bones the model has, which
    /// <c>StudioBones.Read(file).Count</c> already answers; asking twice would be two readings of
    /// one fact that could drift apart.
    /// </remarks>
    public static float[] ForSequence(ReadOnlyMemory<byte> file, int sequence, int boneCount)
    {
        if (boneCount <= 0 || sequence < 0)
        {
            return [];
        }

        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderSequenceIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceCountOffset..]);

        if (sequence >= count)
        {
            return [];
        }

        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSequenceIndexOffset..]);
        int start = at + (sequence * SequenceStride);

        if (start < 0 || start + SequenceStride > bytes.Length)
        {
            return [];
        }

        // Relative to the sequence descriptor itself, like every other index field in it —
        // movementindex, animindexindex, the label — never relative to the file.
        int listAt = start + BinaryPrimitives.ReadInt32LittleEndian(
            bytes[(start + SequenceWeightListIndexOffset)..]);

        if (listAt < 0 || (long)listAt + ((long)boneCount * sizeof(float)) > bytes.Length)
        {
            return [];
        }

        float[] weights = new float[boneCount];

        for (int bone = 0; bone < boneCount; bone++)
        {
            weights[bone] = BinaryPrimitives.ReadSingleLittleEndian(
                bytes[(listAt + (bone * sizeof(float)))..]);
        }

        return weights;
    }
}
