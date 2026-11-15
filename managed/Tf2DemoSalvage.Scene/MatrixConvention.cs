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

        Span<float> twelve = stackalloc float[12];

        Copy(transform, twelve);

        float[] model = new float[16];
        ToModelMatrix(twelve, model);
        return model;
    }

    /// <summary><see cref="ToModelMatrix(IReadOnlyList{float})"/> into sixteen floats the caller owns (B599).</summary>
    /// <param name="transform">Twelve floats, translation in column three.</param>
    /// <param name="into">Sixteen floats, translation in row three; must not overlap <paramref name="transform"/>.</param>
    /// <exception cref="ArgumentException">A span is short, or the two overlap.</exception>
    public static void ToModelMatrix(ReadOnlySpan<float> transform, Span<float> into)
    {
        if (transform.Length < 12 || into.Length < 16 || into.Overlaps(transform))
        {
            throw new ArgumentException("Twelve floats into sixteen, apart.", nameof(into));
        }

        into[0] = transform[0]; into[1] = transform[4]; into[2] = transform[8]; into[3] = 0f;
        into[4] = transform[1]; into[5] = transform[5]; into[6] = transform[9]; into[7] = 0f;
        into[8] = transform[2]; into[9] = transform[6]; into[10] = transform[10]; into[11] = 0f;
        into[12] = transform[3]; into[13] = transform[7]; into[14] = transform[11]; into[15] = 1f;
    }

    /// <summary>The first <paramref name="into"/>.Length floats of a list, into a span.</summary>
    private static void Copy(IReadOnlyList<float> from, Span<float> into)
    {
        for (int index = 0; index < into.Length; index++)
        {
            into[index] = from[index];
        }
    }

    /// <summary>A row-vector 4×4 back to Valve's <c>matrix3x4_t</c>.</summary>
    /// <param name="model">Sixteen floats, as <see cref="ToModelMatrix(IReadOnlyList{float})"/> produces.</param>
    /// <returns>Twelve floats, row-major, translation in column 3.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ArgumentException">It is not sixteen floats.</exception>
    /// <remarks>
    /// **The inverse of <see cref="ToModelMatrix(IReadOnlyList{float})"/>, added because a caller crossed the boundary by
    /// hand and got it wrong.** D88's entity placement fed a sixteen-float model matrix into
    /// something expecting a <c>matrix3x4_t</c>; what came out was
    /// <c>ArgumentException: Destination array was not long enough</c> at the first frame of
    /// playback, and the size was the LUCKY half of the mistake. Had the two been the same length
    /// the transpose would have gone unnoticed and produced a model rotated into a plausible wrong
    /// pose.
    ///
    /// That is the whole reason this type exists — one implementation of the boundary, because two
    /// is how they come to disagree.
    /// </remarks>
    public static float[] ToBoneMatrix(IReadOnlyList<float> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Count < 16)
        {
            throw new ArgumentException("A model matrix is sixteen floats.", nameof(model));
        }

        Span<float> sixteen = stackalloc float[16];

        Copy(model, sixteen);

        return BoneMatrixOf(sixteen);
    }

    /// <summary>A placement straight into a <c>matrix3x4_t</c> the caller owns — <see cref="ToBoneMatrix(IReadOnlyList{float})"/> of its <see cref="Content.Bsp.PropTransform.ToMatrix"/>, without either array (B574).</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="into">Twelve floats, row-major, translation in column 3.</param>
    /// <exception cref="ArgumentException"><paramref name="into"/> is shorter than twelve floats.</exception>
    public static void ToBoneMatrix(Content.Bsp.PropTransform placement, Span<float> into)
    {
        if (into.Length < 12)
        {
            throw new ArgumentException("A matrix3x4_t is twelve floats.", nameof(into));
        }

        Span<float> model = stackalloc float[16];
        placement.WriteMatrix(model);
        Transpose(model, into);
    }

    /// <summary><see cref="ToBoneMatrix(IReadOnlyList{float})"/> from sixteen floats on the stack (B599).</summary>
    /// <param name="model">Sixteen floats.</param>
    /// <returns>Twelve floats, row-major, translation in column 3.</returns>
    /// <remarks>Named apart from <see cref="ToBoneMatrix(IReadOnlyList{float})"/> so an array argument cannot bind here and skip its length check.</remarks>
    public static float[] BoneMatrixOf(ReadOnlySpan<float> model)
    {
        float[] bone = new float[12];
        Transpose(model, bone);
        return bone;
    }

    /// <summary>The one transpose across the boundary: a row-vector 4×4 into a <c>matrix3x4_t</c>.</summary>
    private static void Transpose(ReadOnlySpan<float> model, Span<float> into)
    {
        into[0] = model[0]; into[1] = model[4]; into[2] = model[8]; into[3] = model[12];
        into[4] = model[1]; into[5] = model[5]; into[6] = model[9]; into[7] = model[13];
        into[8] = model[2]; into[9] = model[6]; into[10] = model[10]; into[11] = model[14];
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

        Span<float> a = stackalloc float[16];
        Span<float> b = stackalloc float[16];

        Copy(first, a);
        Copy(second, b);

        float[] result = new float[16];
        Multiply(a, b, result);
        return result;
    }

    /// <summary><see cref="Multiply(IReadOnlyList{float}, IReadOnlyList{float})"/> into sixteen floats the caller owns (B599).</summary>
    /// <param name="first">Applied first.</param>
    /// <param name="second">Applied second.</param>
    /// <param name="result">Their product; may be either operand.</param>
    /// <exception cref="ArgumentException">An operand or the result is shorter than sixteen floats.</exception>
    /// <remarks>not-parity: this renderer's own row-vector 4×4, which the engine does not have. Checked and copied for the
    /// same reasons as <see cref="Concatenate(ReadOnlySpan{float}, ReadOnlySpan{float}, Span{float})"/> (B612).</remarks>
    public static void Multiply(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> result)
    {
        if (first.Length < 16 || second.Length < 16 || result.Length < 16)
        {
            throw new ArgumentException("A model matrix is sixteen floats.", nameof(result));
        }

        if (result.Overlaps(first) || result.Overlaps(second))
        {
            Span<float> copy = stackalloc float[32];

            first[..16].CopyTo(copy);
            second[..16].CopyTo(copy[16..]);
            Multiply(copy[..16], copy[16..], result);
            return;
        }

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

        Span<float> a = stackalloc float[12];
        Span<float> b = stackalloc float[12];

        Copy(first, a);
        Copy(second, b);

        float[] result = new float[12];
        Concatenate(a, b, result);
        return result;
    }

    /// <summary><see cref="Concatenate(IReadOnlyList{float}, IReadOnlyList{float})"/> into twelve floats the caller owns (B599).</summary>
    /// <param name="first">The outer transform.</param>
    /// <param name="second">The inner one.</param>
    /// <param name="result">Twelve floats in Valve's layout; may be either operand, as the engine's may.</param>
    /// <exception cref="ArgumentException">An operand or the result is shorter than twelve floats.</exception>
    /// <remarks>
    /// **`ConcatTransforms` (`mathlib_base.cpp:658-718`), in its own order.** Both operands are loaded before anything is
    /// stored (`:670-676`), so the output may alias an input. Each output row is `A0·B0 + (A1·B1 + A2·B2)` (`:686`), and
    /// the masked add puts row A's translation on the w lane and **+0.0 on the other three** (`:707-712`), which turns a
    /// −0.0 sum into +0.0 there. Float addition does not associate, so the grouping is part of the answer. The operands are
    /// copied only when the result overlaps one, which is when loading first matters.
    /// </remarks>
    public static void Concatenate(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> result)
    {
        if (first.Length < 12 || second.Length < 12 || result.Length < 12)
        {
            throw new ArgumentException("A matrix3x4_t is twelve floats.", nameof(result));
        }

        Span<float> copies = stackalloc float[24];
        scoped ReadOnlySpan<float> a = first;
        scoped ReadOnlySpan<float> b = second;

        if (result.Overlaps(first) || result.Overlaps(second))
        {
            first[..12].CopyTo(copies);
            second[..12].CopyTo(copies[12..]);
            a = copies[..12];
            b = copies[12..];
        }

        for (int row = 0; row < 3; row++)
        {
            int at = row * 4;

            for (int lane = 0; lane < 4; lane++)
            {
                float sum = (a[at] * b[lane]) + ((a[at + 1] * b[4 + lane]) + (a[at + 2] * b[8 + lane]));

                // `AndSIMD( rowA, lastMask )`: the translation on the w lane, +0.0 on the others.
                result[at + lane] = sum + (lane == 3 ? a[at + 3] : 0f);
            }
        }
    }
}
