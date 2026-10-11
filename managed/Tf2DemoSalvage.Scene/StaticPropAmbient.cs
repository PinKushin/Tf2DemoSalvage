using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The ambient cube the light cache computes for a static prop: `ComputeAmbientFromSphericalSamples`,
/// `engine.dll` `FUN_1801b74f0` (B514).
/// </summary>
/// <remarks>
/// **A static prop does not take its leaf's ambient samples.** *Read in disassembly:* a static prop's handle builds its
/// static state as `FUN_1801b8270( cache, origin, leaf, 1 )`, and the ambient routine `FUN_1801bb4b0`, under
/// `r_radiosity` 4 (the default), gives a static caller this — 162 rays cast from the lighting origin at the world's
/// lightmaps — and only a dynamic one the leaf lump (`FUN_180100f60`). So a prop high in open air is lit by the sky
/// ambient light and the roofs below it, where its leaf's samples are whatever vrad averaged over the leaf.
///
/// The arithmetic is in <see cref="Compute"/>; the rays are <see cref="SurfaceColour.LightVec"/> with no light styles.
/// </remarks>
public static class StaticPropAmbient
{
    /// <summary>`COORD_EXTENT * 1.74`, the length of each ray.</summary>
    public const float RayLength = 57016.32f;

    /// <summary>The cube at a point.</summary>
    /// <param name="origin">The prop's lighting origin.</param>
    /// <param name="lightVec">`R_LightVec( start, end, false, colour )`: the surface struck, or null, and its light.</param>
    /// <param name="reflectivity">A texdata's reflectivity — the struck surface's material's.</param>
    /// <param name="skyAmbient">The map's first `emit_skyambient` light's intensity, or null when it has none.</param>
    /// <returns>Six faces, each the cosine-weighted mean of the samples on its side.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public static AmbientCube Compute(
        Vector3 origin,
        Func<Vector3, Vector3, (DecalFace? Face, Vector3 Light)> lightVec,
        Func<int, Vector3> reflectivity,
        Vector3? skyAmbient)
    {
        ArgumentNullException.ThrowIfNull(lightVec);
        ArgumentNullException.ThrowIfNull(reflectivity);

        IReadOnlyList<Vector3> normals = SphericalSampleNormals.All;
        Span<Vector3> samples = stackalloc Vector3[normals.Count];

        for (int index = 0; index < normals.Count; index++)
        {
            (DecalFace? face, Vector3 light) = lightVec(origin, origin + (normals[index] * RayLength));

            if (face is null)
            {
                continue;
            }

            if ((face.Flags & SurfaceProperties.Sky) != 0)
            {
                // "if we hit the sky, use the sky ambient" — and with no such light, what the ray gathered.
                samples[index] = skyAmbient ?? light;
            }
            else
            {
                samples[index] = light * reflectivity(face.Texdata);
            }
        }

        return new AmbientCube(
            Side(samples, normals, Vector3.UnitX),
            Side(samples, normals, -Vector3.UnitX),
            Side(samples, normals, Vector3.UnitY),
            Side(samples, normals, -Vector3.UnitY),
            Side(samples, normals, Vector3.UnitZ),
            Side(samples, normals, -Vector3.UnitZ));
    }

    /// <summary>One box direction: `sum( c * sample ) / sum( c )` over the normals with `c = dot( normal, box ) &gt; 0`.</summary>
    private static (float Red, float Green, float Blue) Side(ReadOnlySpan<Vector3> samples, IReadOnlyList<Vector3> normals, Vector3 box)
    {
        Vector3 sum = Vector3.Zero;
        float total = 0f;

        for (int index = 0; index < normals.Count; index++)
        {
            float cosine = Vector3.Dot(normals[index], box);

            if (cosine > 0f)
            {
                total += cosine;
                sum += samples[index] * cosine;
            }
        }

        sum *= 1f / total;

        return (sum.X, sum.Y, sum.Z);
    }
}
