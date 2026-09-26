using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where the engine's light cache evaluates a model's lighting: the centre of the point's cell, when it can.</summary>
/// <remarks>
/// **Read from `engine.dll`.** `LightcacheGet` (`0x1801b9cd0`) keys its entries on a 32 × 32 × 128 cell and the point's
/// leaf. On a miss with `r_lightcachecenter` at its default of 1 (`0x18000ff90`), `0x1801b6860` places the entry: the
/// cell's centre, if it is not in solid (`MASK_OPAQUE`, `0x4081`) and a world trace from the point reaches it; else the
/// centre at the point's own height, if a trace reaches that; else the point. Everything the entry holds — the ambient
/// cube, the chosen lights, the sun — is evaluated there, so models in one cell share one light.
/// </remarks>
public static class LightCacheCell
{
    /// <summary>A cell's width across x and y.</summary>
    private const int Across = 32;

    /// <summary>Its height.</summary>
    private const int Up = 128;

    /// <summary>Where the light cache would light a point.</summary>
    /// <param name="point">The model's lighting origin.</param>
    /// <param name="solidAt">Whether a point is inside `MASK_OPAQUE` contents.</param>
    /// <param name="reaches">Whether a world trace from the first point reaches the second.</param>
    /// <returns>The position the entry is lit at.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public static Vector3 Position(Vector3 point, Func<Vector3, bool> solidAt, Func<Vector3, Vector3, bool> reaches)
    {
        ArgumentNullException.ThrowIfNull(solidAt);
        ArgumentNullException.ThrowIfNull(reaches);

        Vector3 centre = new(Middle(point.X, Across), Middle(point.Y, Across), Middle(point.Z, Up));

        if (!solidAt(centre) && reaches(point, centre))
        {
            return centre;
        }

        Vector3 level = centre with { Z = point.Z };

        return reaches(point, level) ? level : point;
    }

    /// <summary>
    /// The middle of a coordinate's cell: `|c|` shifted by the cell's bits, complemented for a negative, which floors.
    /// </summary>
    private static float Middle(float coordinate, int size)
    {
        int shift = size == Up ? 7 : 5;
        int cell = (int)MathF.Abs(coordinate) >> shift;

        if (coordinate < 0f)
        {
            cell = ~cell;
        }

        return (cell * size) + (size / 2f);
    }
}
