using System;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>The tangent frame the engine gives a brush face's vertex.</summary>
/// <remarks>
/// `engine.dll` x64 `FUN_1800f83c0` over `FUN_1800f8590`, called per corner by the surface's vertex build
/// (`FUN_1800f4d40`) for a surface flagged as wanting one. `lightmappedgeneric_vs20.fxc:127-129` hands it to the pixel
/// shader as `tangentSpaceTranspose`, where a bumped face's reflection turns through it (`ps2_3_x.h:479`).
/// </remarks>
public static class SurfaceTangent
{
    /// <summary>A corner's tangent S and the sign that rebuilds its T.</summary>
    /// <param name="normal">The corner's vertex normal.</param>
    /// <param name="textureS">`textureVecs[0]`.</param>
    /// <param name="textureT">`textureVecs[1]`.</param>
    /// <param name="plane">The face's plane normal, unflipped: `FUN_1800f8590` reads the pointer, not the side.</param>
    /// <returns>
    /// S, and w such that T is `cross( normal, S ) * w`, the form the shader's `TangentToWorld` takes. All zero for a face
    /// with no projection, which the shader reads as no frame.
    /// </returns>
    /// <remarks>
    /// The engine stores T; this carries a sign instead, since it is `cross( S, normal )` before the negation and a
    /// negation is the only way that differs from the rebuilt vector.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Of(
        (float X, float Y, float Z) normal,
        (float X, float Y, float Z, float Offset) textureS,
        (float X, float Y, float Z, float Offset) textureT,
        (float X, float Y, float Z) plane)
    {
        (float X, float Y, float Z) s = Normalized((textureS.X, textureS.Y, textureS.Z));
        (float X, float Y, float Z) t = Normalized((textureT.X, textureT.Y, textureT.Z));

        (float X, float Y, float Z) across = Normalized(Cross(normal, t));

        if (across is { X: 0f, Y: 0f, Z: 0f })
        {
            return default;
        }

        (float X, float Y, float Z) sCrossT = Cross(s, t);
        bool negate = (sCrossT.X * plane.X) + (sCrossT.Y * plane.Y) + (sCrossT.Z * plane.Z) > 0f;

        return negate
            ? (Tidy(-across.X), Tidy(-across.Y), Tidy(-across.Z), 1f)
            : (across.X, across.Y, across.Z, -1f);
    }

    /// <summary>Negating an exact zero gives minus zero, which a record's equality reads as a different number; adding zero clears it.</summary>
    private static float Tidy(float value) => value + 0f;

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        ((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    /// <summary>A unit vector, or zero for one too short to have a direction; the engine's 1e-10 serves the same purpose.</summary>
    private static (float X, float Y, float Z) Normalized((float X, float Y, float Z) v)
    {
        float squared = (v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z);

        if (squared < 1e-10f)
        {
            return default;
        }

        float length = MathF.Sqrt(squared);

        return (v.X / length, v.Y / length, v.Z / length);
    }
}
