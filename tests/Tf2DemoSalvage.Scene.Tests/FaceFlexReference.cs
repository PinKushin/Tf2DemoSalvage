using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The face's vertex work exactly as it stood at <c>5ff7e1ac</c>, before the flex-cost rewrite
/// (docs/measurements/2026-10-30-flex-for-cost.md): the whole-scratch clear, <c>StudioFlexRules.Accumulate</c> and
/// <c>FaceFlex.IntoFlexedSlots</c>, copied verbatim. **Kept only here, never in production**: it is the reference the
/// rewrite is held byte-identical to.
/// </summary>
internal static class FaceFlexReference
{
    private const float ByteScale = 0.003921569f;

    /// <summary><c>FlexFor</c>'s miss, as it was: clear the whole scratch, accumulate, write the flexed slots.</summary>
    /// <returns>How many vertex animations applied; the stream is written only when that is above zero.</returns>
    public static int Face(
        StudioFlexData data, float[] weights, float[] delayed, float[] positions, float[] normals, FlexedCorners corners,
        float[] stream)
    {
        Array.Clear(positions);
        Array.Clear(normals);
        int applied = Accumulate(data, weights, delayed, positions, normals);

        if (applied > 0)
        {
            IntoFlexedSlots((positions, normals), corners, stream);
        }

        return applied;
    }

    public static int Accumulate(
        StudioFlexData data,
        ReadOnlySpan<float> weights,
        ReadOnlySpan<float> delayed,
        Span<float> positions,
        Span<float> normals)
    {
        int applied = 0;

        foreach (StudioMeshFlex flex in data.Flexes)
        {
            float w1 = StudioFlexRules.Ramp(flex, At(weights, flex.FlexDesc));
            float w2 = StudioFlexRules.Ramp(flex, At(delayed, flex.FlexDesc));
            float w3 = w1;
            float w4 = w2;

            if (flex.FlexPair != 0)
            {
                w3 = StudioFlexRules.Ramp(flex, At(weights, flex.FlexPair));
                w4 = StudioFlexRules.Ramp(flex, At(delayed, flex.FlexPair));
            }

            if (Off(w1) && Off(w2) && Off(w3) && Off(w4))
            {
                continue;
            }

            foreach (StudioVertAnim anim in flex.Vertices)
            {
                int at = anim.Vertex * 3;

                if (at < 0 || at + 2 >= positions.Length || at + 2 >= normals.Length)
                {
                    continue;
                }

                float speed = anim.Speed * ByteScale;
                float side = anim.Side * ByteScale;
                float weight = ((((1f - speed) * w2) + (w1 * speed)) * (1f - side)) +
                    ((((1f - speed) * w4) + (speed * w3)) * side);

                positions[at] += anim.Delta.X * weight;
                positions[at + 1] += anim.Delta.Y * weight;
                positions[at + 2] += anim.Delta.Z * weight;
                normals[at] += anim.NormalDelta.X * weight;
                normals[at + 1] += anim.NormalDelta.Y * weight;
                normals[at + 2] += anim.NormalDelta.Z * weight;
                applied++;
            }
        }

        return applied;
    }

    public static float[] IntoFlexedSlots((float[] Positions, float[] Normals) deltas, FlexedCorners corners, float[] stream)
    {
        ReadOnlySpan<float> positions = deltas.Positions;
        ReadOnlySpan<float> normals = deltas.Normals;
        ReadOnlySpan<int> at = corners.At;
        ReadOnlySpan<int> from = corners.From;

        for (int index = 0; index < at.Length; index++)
        {
            int source = from[index];

            if (source + 2 >= positions.Length || source + 2 >= normals.Length)
            {
                continue;
            }

            int to = at[index] * 6;
            stream[to] = positions[source];
            stream[to + 1] = positions[source + 1];
            stream[to + 2] = positions[source + 2];
            stream[to + 3] = normals[source];
            stream[to + 4] = normals[source + 1];
            stream[to + 5] = normals[source + 2];
        }

        return stream;
    }

    private static bool Off(float weight) => weight > -0.001d && weight < 0.001d;

    private static float At(ReadOnlySpan<float> values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0f;

    /// <summary>A list that is not an array, so the production path's non-array branch is exercised too.</summary>
    public static IReadOnlyList<StudioVertAnim> AsList(StudioVertAnim[] anims) => new List<StudioVertAnim>(anims);
}
