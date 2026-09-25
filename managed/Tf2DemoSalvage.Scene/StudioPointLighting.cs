using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>`IStudioRender::ComputeLighting`: the light at one point along one normal, on the CPU (B415).</summary>
/// <remarks>
/// **Read from `studiorender.dll`.** `0x180020bd0` hands the ambient cube and up to sixteen lights to `0x180020c80`, which
/// takes each light's direction as `Δ · rsqrt( |Δ|² + 1e−10 )` and its falloff from a table by which of its three terms
/// are set (`0x18004f800` and neighbours: `1 / ( c + l·|Δ| + q·|Δ|² )`, `FLT_EPSILON` standing in for an absent constant,
/// zero once `|Δ|² &gt; range²`), then adds the cube as each normal component squared times the face on its side — a
/// component of zero takes the negative face. A function picked by the first four lights' types then adds
/// `colour · max( n · direction, 0 ) · falloff` for each (`0x180021c80`).
///
/// **The lights are <see cref="LevelLighting.LightingAt"/>'s**, the ones a model is drawn with, and the sun is added
/// as a directional light where <see cref="LevelLighting.SunAt"/> gives one — the same inputs the model draw takes.
/// A spotlight also takes its cone (`0x180021b20`).
/// </remarks>
public static class StudioPointLighting
{
    /// <summary>`0x180080410`: added under the reciprocal square root that normalises a light's direction.</summary>
    private const float DirectionEpsilon = 1e-10f;

    /// <summary>`0x180080aac`, `FLT_EPSILON`: the constant term of a falloff that has none.</summary>
    private const float AbsentConstant = 1.1920929e-7f;

    /// <summary>The light at a point along a normal.</summary>
    /// <param name="lighting">The cube and the local lights.</param>
    /// <param name="sun">The sun where it reaches the point, or null.</param>
    /// <param name="point">Where.</param>
    /// <param name="normal">The unit normal.</param>
    /// <returns>The linear light.</returns>
    public static Vector3 At(PointLighting lighting, SunLight? sun, Vector3 point, Vector3 normal)
    {
        AmbientCube cube = lighting.Cube;
        Vector3 light =
            (Face(normal.X > 0f ? cube.PositiveX : cube.NegativeX) * normal.X * normal.X) +
            (Face(normal.Y > 0f ? cube.PositiveY : cube.NegativeY) * normal.Y * normal.Y) +
            (Face(normal.Z > 0f ? cube.PositiveZ : cube.NegativeZ) * normal.Z * normal.Z);

        foreach (LocalLight local in lighting.Locals ?? [])
        {
            Vector3 delta = new Vector3(local.X, local.Y, local.Z) - point;
            float squared = delta.LengthSquared();

            if (local.Range != 0f && squared > local.Range * local.Range)
            {
                continue;
            }

            float falloff = 1f / ((local.Constant == 0f ? AbsentConstant : local.Constant) +
                                  (local.Linear * MathF.Sqrt(squared)) + (local.Quadratic * squared));
            Vector3 direction = delta / MathF.Sqrt(squared + DirectionEpsilon);
            float dot = MathF.Max(Vector3.Dot(normal, direction), 0f);

            if (local.Spot)
            {
                dot *= Cone(local, direction);
            }

            light += new Vector3(local.Red, local.Green, local.Blue) * (dot * falloff);
        }

        if (sun is { } directional)
        {
            float dot = MathF.Max(
                -Vector3.Dot(normal, new Vector3(directional.DirectionX, directional.DirectionY, directional.DirectionZ)), 0f);

            light += new Vector3(directional.Red, directional.Green, directional.Blue) * dot;
        }

        return light;

        static Vector3 Face((float Red, float Green, float Blue) face) => new(face.Red, face.Green, face.Blue);
    }

    /// <summary>
    /// `0x180021b20`'s cone: full inside the inner cosine, dark at or outside the outer, and between them the fringe
    /// `( cos − outer ) / ( inner − outer )`, raised to the exponent unless that is 0 or 1.
    /// </summary>
    private static float Cone(LocalLight spot, Vector3 toLight)
    {
        float along = -Vector3.Dot(toLight, new Vector3(spot.Direction.X, spot.Direction.Y, spot.Direction.Z));

        if (along <= spot.SpotOuter)
        {
            return 0f;
        }

        if (along >= spot.SpotInner)
        {
            return 1f;
        }

        float fringe = (along - spot.SpotOuter) / (spot.SpotInner - spot.SpotOuter);

        return spot.SpotExponent is 0f or 1f ? fringe : MathF.Pow(fringe, spot.SpotExponent);
    }
}
