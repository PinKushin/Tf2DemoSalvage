using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The one place that crosses between Valve's matrices and this renderer's.
/// </summary>
/// <remarks>
/// **This renderer deliberately speaks two conventions, and the boundary between them is here.**
/// Naming it is the point: before this, the conversion existed in two places with two pieces of code
/// and no statement anywhere of which layout was which.
///
/// **Valve's <c>matrix3x4_t</c> transforms a COLUMN vector.** Twelve floats, row major, translation
/// in column three, so a point moves as <c>p'_i = m[i][0..2] · p + m[i][3]</c>. Bones and
/// <c>mstudioattachment_t.local</c> are both this, and the skinning path uses them RAW — the shader
/// does <c>dot(boneRows[row], float4(position, 1))</c>, which is that formula exactly. Nothing is
/// converted for skinning and nothing should be.
///
/// **The model matrix transforms a ROW vector.** Sixteen floats, translation in row three, declared
/// <c>row_major float4x4</c> in the shader so <c>p' = p · M</c>. <c>PropTransform.ToMatrix</c>
/// already produces this from <c>AngleMatrix</c>'s output, transposing as it goes.
///
/// So a Valve transform used as a MODEL matrix — an attachment point, say — has to be transposed and
/// its translation moved. That is not a workaround for a wrong convention; it is the cost of having
/// two, which the shader's declaration and the raw bone upload both make deliberate.
/// </remarks>
internal static class MatrixConvention
{
    /// <summary>Valve's column-vector 3×4 as this renderer's row-vector 4×4.</summary>
    /// <param name="transform">Twelve floats, translation in column three.</param>
    /// <returns>Sixteen floats, translation in row three.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transform"/> is null.</exception>
    /// <exception cref="ArgumentException">It is not twelve floats.</exception>
    /// <remarks>
    /// The 3×3 is transposed and the translation moves. Skipping either half produces a placement
    /// that is somewhere plausible and wrong rather than an error, which is why
    /// <c>MatrixConventionTests</c> predicts exact positions and turns its subject ninety degrees —
    /// a rotation is the only case where a missing transpose shows.
    /// </remarks>
    public static float[] ToModelMatrix(IReadOnlyList<float> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        if (transform.Count < 12)
        {
            throw new ArgumentException("A matrix3x4_t is twelve floats.", nameof(transform));
        }

        return
        [
            transform[0], transform[4], transform[8], 0f,
            transform[1], transform[5], transform[9], 0f,
            transform[2], transform[6], transform[10], 0f,
            transform[3], transform[7], transform[11], 1f,
        ];
    }

    /// <summary>Two row-vector 4×4s, applied left to right.</summary>
    /// <param name="first">Applied first.</param>
    /// <param name="second">Applied second.</param>
    /// <returns>Their product, in the same layout.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// Left to right because the vectors are rows: <c>p · A · B</c> applies <paramref name="first"/>
    /// before <paramref name="second"/>. Under the column convention the order would read the other
    /// way, which is the second half of the same trap.
    /// </remarks>
    public static float[] Multiply(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        float[] result = new float[16];

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float total = 0f;

                for (int step = 0; step < 4; step++)
                {
                    total += first[(row * 4) + step] * second[(step * 4) + column];
                }

                result[(row * 4) + column] = total;
            }
        }

        return result;
    }

    /// <summary>Valve's <c>ConcatTransforms</c>: one 3×4 applied after another.</summary>
    /// <param name="first">The outer transform, such as a bone.</param>
    /// <param name="second">The inner one, such as an attachment's offset within that bone.</param>
    /// <returns>Twelve floats in Valve's own layout.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// Kept in Valve's convention rather than converted first, because that is the form the engine
    /// composes in and the form its inputs arrive in — converting early would mean transposing
    /// twice and reasoning about the order in a convention neither operand uses.
    /// </remarks>
    public static float[] Concatenate(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

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
