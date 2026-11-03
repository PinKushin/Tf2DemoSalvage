using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The oracle for <see cref="DisplacementCollision.SweepSurface(float, float, float, float, float, float, ValueTuple{float, float, float}, int, float)"/>:
/// every triangle of every displacement swept, in the set's own order, with nothing culled.
/// </summary>
/// <remarks>
/// Same order and the same strict `&lt;` as the set, so the two agree exactly — fraction, texdata, `DISPSURF_FLAG_SURFPROP2`,
/// and plane — unless the set skipped a triangle that would have stopped the sweep shorter.
///
/// **The second-surfaceprop threshold and its "alpha sum above it" rule are a deliberate independent copy** of
/// `DisplacementCollision`'s, taken from the SDK rather than read from production. Shared, a wrong threshold would agree with
/// itself; copied, a change to the production rule reddens the comparison, which is the point of an oracle.
/// </remarks>
internal static class DisplacementBruteForce
{
    /// <summary>`DISP_ALPHA_PROP_DELTA` (`builddisp.h:25`) in alpha scaled to one: 382.5 over three alphas of 0–255.</summary>
    private const float SecondPropAlpha = 382.5f / 255f;

    /// <summary>The unculled sweep.</summary>
    public static (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance) Sweep(
        IReadOnlyList<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> displacements,
        (float X, float Y, float Z) from,
        (float X, float Y, float Z) to,
        (float X, float Y, float Z) extents,
        float limit)
    {
        (float X, float Y, float Z) delta = (to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance) best = (limit, -1, false, default, 0f);

        foreach ((int texdata, IReadOnlyList<SurfaceVertex> corners) in displacements)
        {
            for (int at = 0; at + 2 < corners.Count; at += 3)
            {
                SurfaceVertex a = corners[at];
                SurfaceVertex b = corners[at + 1];
                SurfaceVertex c = corners[at + 2];
                DisplacementTriangle triangle = DisplacementTriangle.From((a.X, a.Y, a.Z), (b.X, b.Y, b.Z), (c.X, c.Y, c.Z));
                float against = DisplacementSweep.Against(from, delta, extents, triangle, best.Fraction);

                if (against < best.Fraction)
                {
                    best = (against, texdata, a.Alpha + b.Alpha + c.Alpha > SecondPropAlpha, triangle.Normal, triangle.Distance);
                }
            }
        }

        return best;
    }

    /// <summary>A sweep's answer as exact bits, so two answers compare exactly equal rather than within a tolerance.</summary>
    public static (int Fraction, int Texdata, bool SurfaceProp2, int NormalX, int NormalY, int NormalZ, int Distance) Bits(
        (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance) swept) =>
        (BitConverter.SingleToInt32Bits(swept.Fraction), swept.Texdata, swept.SurfaceProp2,
            BitConverter.SingleToInt32Bits(swept.Normal.X), BitConverter.SingleToInt32Bits(swept.Normal.Y),
            BitConverter.SingleToInt32Bits(swept.Normal.Z), BitConverter.SingleToInt32Bits(swept.Distance));
}
