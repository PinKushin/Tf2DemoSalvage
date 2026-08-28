using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// A model's bounds once its placement is applied — Valve's <c>TransformAABB</c>.
/// </summary>
/// <remarks>
/// **`mathlib_base.cpp:2910`, transcribed:**
///
/// <code>
/// localCenter  = (mins + maxs) * 0.5
/// localExtents = maxs - localCenter
/// worldCenter  = VectorTransform( localCenter, transform )
/// worldExtents.x = DotProductAbs( localExtents, transform[0] )
/// worldExtents.y = DotProductAbs( localExtents, transform[1] )
/// worldExtents.z = DotProductAbs( localExtents, transform[2] )
/// minsOut = worldCenter - worldExtents;  maxsOut = worldCenter + worldExtents
/// </code>
///
/// **The absolute values are the whole trick.** A rotated box is not axis-aligned, so the enclosing
/// axis-aligned box takes each output axis's extent as the sum of the input extents projected onto
/// it, signs discarded — <c>DotProductAbs</c> is
/// <c>|v.x*m[0]| + |v.y*m[1]| + |v.z*m[2]|</c>. Doing it without the absolutes lets opposite
/// contributions cancel and produces a box smaller than the thing inside it.
///
/// **Rotation does not simply enlarge, and a comment in this project claimed it did.** The claim was
/// that a long prop turned forty-five degrees buckets larger. Worked through: a 100 × 10 box rotated
/// forty-five degrees about Z has world extents of 77.8 × 77.8, so its LONGEST axis shrinks from 100
/// to 77.8 while its shortest grows. Which way it moves depends on the shape. That is exactly why
/// the world box has to be computed rather than approximated from the model's own — not because the
/// approximation is always small, but because it is wrong in a direction nobody can predict.
/// </remarks>
public static class WorldSpaceBounds
{
    /// <summary>The longest axis of the world-space box — Valve's <c>fDimension</c>.</summary>
    /// <param name="local">The model-space render bounds.</param>
    /// <param name="matrix">The instance's model matrix: row-vector, translation in the last row.</param>
    /// <returns>The longest of the three world-space extents.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **Only the extents are needed, so the centre is not computed.** `fDimension` is
    /// `MAX(MAX(|dims.x|, |dims.y|), |dims.z|)` of `absMaxs - absMins`, and that difference is
    /// exactly twice the world extents — the centre cancels. Valve computes the full box because
    /// the caller also culls and places with it; here the only consumer is the size bucket.
    ///
    /// **The convention crossing is the hazard.** This project's matrices are row-vector with the
    /// translation in the last ROW, so the coefficients producing output x are the first COLUMN —
    /// `m[0]`, `m[4]`, `m[8]` — where Valve's `matrix3x4_t` has them as a row. Getting it the other
    /// way round transposes the rotation, which for a symmetric box is invisible and for anything
    /// else is a plausible wrong number. See `docs/memory/two-matrix-conventions-on-purpose.md`.
    /// </remarks>
    public static float LongestAxis(StudioBox local, float[] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A model matrix is sixteen floats.", nameof(matrix));
        }

        float extentX = (local.MaxX - local.MinX) * 0.5f;
        float extentY = (local.MaxY - local.MinY) * 0.5f;
        float extentZ = (local.MaxZ - local.MinZ) * 0.5f;

        float worldX = Abs(extentX, matrix[0]) + Abs(extentY, matrix[4]) + Abs(extentZ, matrix[8]);
        float worldY = Abs(extentX, matrix[1]) + Abs(extentY, matrix[5]) + Abs(extentZ, matrix[9]);
        float worldZ = Abs(extentX, matrix[2]) + Abs(extentY, matrix[6]) + Abs(extentZ, matrix[10]);

        // Twice, because an extent is a half-size and fDimension is the full span.
        return 2f * Math.Max(Math.Max(worldX, worldY), worldZ);
    }

    private static float Abs(float extent, float coefficient) => Math.Abs(extent * coefficient);
}
