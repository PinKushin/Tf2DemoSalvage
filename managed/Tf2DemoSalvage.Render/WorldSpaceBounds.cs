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
    /// **A convenience over <see cref="Of"/>, which is where the arithmetic lives.** Both answers
    /// come from one box because the engine computes one box: `CollateRenderablesInLeaf` culls with
    /// it and then buckets with it, and computing it twice would be two chances to disagree about
    /// where a model is.
    /// </remarks>
    public static float LongestAxis(StudioBox local, float[] matrix) =>
        LongestAxisOf(Of(local, matrix));

    /// <summary>The world-space box a placed model occupies — Valve's <c>TransformAABB</c>.</summary>
    /// <param name="local">The model-space render bounds.</param>
    /// <param name="matrix">The instance's model matrix: row-vector, translation in the last row.</param>
    /// <returns>The enclosing axis-aligned box, in world coordinates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not sixteen floats.</exception>
    /// <remarks>
    /// **The centre is computed here where the older `LongestAxis` skipped it**, because a cull
    /// needs to know WHERE the box is and a size bucket only needs how big. `fDimension` is a
    /// difference of two corners, so the centre cancels out of it — which made dropping the centre
    /// safe for bucketing and would make it silently wrong for culling.
    ///
    /// **The convention crossing is the hazard.** This project's matrices are row-vector with the
    /// translation in the last ROW, so the coefficients producing output x are the first COLUMN —
    /// `m[0]`, `m[4]`, `m[8]` — where Valve's `matrix3x4_t` has them as a row, and the translation
    /// is `m[12..14]` rather than `m[0][3]`. Getting it the other way round transposes the rotation,
    /// which for a symmetric box is invisible and for anything else is a plausible wrong number. See
    /// `docs/memory/two-matrix-conventions-on-purpose.md`.
    /// </remarks>
    public static (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Of(
        StudioBox local, float[] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Length != 16)
        {
            throw new ArgumentException("A model matrix is sixteen floats.", nameof(matrix));
        }

        float centreX = (local.MinX + local.MaxX) * 0.5f;
        float centreY = (local.MinY + local.MaxY) * 0.5f;
        float centreZ = (local.MinZ + local.MaxZ) * 0.5f;

        float extentX = local.MaxX - centreX;
        float extentY = local.MaxY - centreY;
        float extentZ = local.MaxZ - centreZ;

        // VectorTransform: the local centre placed by the matrix, translation included.
        float worldCentreX =
            (centreX * matrix[0]) + (centreY * matrix[4]) + (centreZ * matrix[8]) + matrix[12];
        float worldCentreY =
            (centreX * matrix[1]) + (centreY * matrix[5]) + (centreZ * matrix[9]) + matrix[13];
        float worldCentreZ =
            (centreX * matrix[2]) + (centreY * matrix[6]) + (centreZ * matrix[10]) + matrix[14];

        // DotProductAbs, per output axis.
        float worldX = Abs(extentX, matrix[0]) + Abs(extentY, matrix[4]) + Abs(extentZ, matrix[8]);
        float worldY = Abs(extentX, matrix[1]) + Abs(extentY, matrix[5]) + Abs(extentZ, matrix[9]);
        float worldZ = Abs(extentX, matrix[2]) + Abs(extentY, matrix[6]) + Abs(extentZ, matrix[10]);

        return (
            worldCentreX - worldX, worldCentreY - worldY, worldCentreZ - worldZ,
            worldCentreX + worldX, worldCentreY + worldY, worldCentreZ + worldZ);
    }

    /// <summary>The longest span of an already-placed box — Valve's <c>fDimension</c>.</summary>
    /// <param name="box">A world-space box, as <see cref="Of"/> returns.</param>
    /// <returns>The longest of its three spans.</returns>
    /// <remarks>
    /// `DetectBucketedRenderGroup` takes `absMaxs - absMins` and then
    /// `MAX(MAX(|dims.x|, |dims.y|), |dims.z|)`. The absolute values are Valve's and are inert for a
    /// well-formed box, whose maximum is never below its minimum; kept because dropping them would
    /// make a degenerate box report a negative size and bucket as the smallest rather than being
    /// obviously wrong.
    /// </remarks>
    public static float LongestAxisOf(
        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box) =>
        Math.Max(
            Math.Max(Math.Abs(box.MaxX - box.MinX), Math.Abs(box.MaxY - box.MinY)),
            Math.Abs(box.MaxZ - box.MinZ));

    private static float Abs(float extent, float coefficient) => Math.Abs(extent * coefficient);
}
