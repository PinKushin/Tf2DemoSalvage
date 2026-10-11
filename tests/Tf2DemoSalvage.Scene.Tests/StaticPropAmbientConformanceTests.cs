using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A static prop's ambient cube is not its leaf's: the light cache COMPUTES it, from 162 rays cast at the lightmaps (B514).
/// </summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll`. A static prop's handle builds its static state through
/// `FUN_1801b8270( cache, origin, leaf, 1 )` (from `FUN_1801ba590`), whose ambient is `FUN_1801bb4b0( leaf, origin,
/// state, 1, … )`: under `r_radiosity` 4 — its default, the string at `0x1803933f8` — a static caller takes
/// `FUN_1801b74f0` and only a dynamic one the leaf's samples (`FUN_180100f60`). `FUN_1801b74f0` is
/// `ComputeAmbientFromSphericalSamples`:
///
/// <code>
/// skyAmbient = the first world light of type 5 (emit_skyambient), if any
/// for each of the 162 g_anorms (mathlib/anorms.cpp):
///     surface = R_LightVec( origin, origin + normal * 57016.32, false, colour )      // colour starts at zero
///     no surface             -> colour stays zero
///     surface is SURFDRAW_SKY -> colour = skyAmbient.intensity when there is one
///     otherwise               -> colour *= the surface material's reflectivity
/// for each of the six box directions (+X -X +Y -Y +Z -Z):
///     cube[box] = sum( c * colour ) / sum( c )   over the normals with c = dot( normal, box ) > 0
/// </code>
///
/// `57016.32` is `COORD_EXTENT * 1.74`. *Interpolated*: the material's reflectivity is taken as the map's texdata
/// reflectivity, which vbsp copies from it.
/// </remarks>
public sealed class StaticPropAmbientConformanceTests
{
    private static readonly DecalFace Wall = Face(SurfaceProperties.None, texdata: 3);
    private static readonly DecalFace SkyFace = Face(SurfaceProperties.Sky, texdata: 9);

    [Test]
    public void Compute_FromAnyPoint_Casts162RaysOfTheEnginesLengthAlongTheVertexNormals()
    {
        List<(Vector3 Start, Vector3 End)> rays = [];
        Vector3 origin = new(10f, 20f, 30f);

        StaticPropAmbient.Compute(
            origin,
            (start, end) =>
            {
                rays.Add((start, end));
                return (null, Vector3.Zero);
            },
            static _ => Vector3.One,
            null);

        rays.Count.ShouldBe(162);
        rays.ShouldAllBe(ray => ray.Start == origin);

        // g_anorms[5] is ( 0, 0, 1 ) and g_anorms[0] ( -0.525731, 0, 0.850651 ).
        rays[5].End.ShouldBe(origin + new Vector3(0f, 0f, 57016.32f));
        (rays[0].End - origin).X.ShouldBe(-0.525731f * 57016.32f, 0.01f);
        (rays[0].End - origin).Z.ShouldBe(0.850651f * 57016.32f, 0.01f);
    }

    [Test]
    public void Compute_EveryRayStrikingSky_IsTheSkyAmbientLightOnEveryFace()
    {
        AmbientCube cube = StaticPropAmbient.Compute(
            Vector3.Zero, static (_, _) => (SkyFace, Vector3.Zero), static _ => new Vector3(0.5f), new Vector3(0.2f, 0.3f, 0.4f));

        foreach ((float red, float green, float blue) in Faces(cube))
        {
            red.ShouldBe(0.2f, 1e-5f);
            green.ShouldBe(0.3f, 1e-5f);
            blue.ShouldBe(0.4f, 1e-5f);
        }
    }

    /// <summary>With no `emit_skyambient` on the map a sky hit keeps what `R_LightVec` gave it, which for sky is nothing.</summary>
    [Test]
    public void Compute_SkyOnAMapWithNoSkyAmbientLight_AddsNothing()
    {
        StaticPropAmbient.Compute(Vector3.Zero, static (_, _) => (SkyFace, Vector3.Zero), static _ => Vector3.One, null)
            .ShouldBe(default(AmbientCube));
    }

    /// <summary>A surface's lightmap light times its material's reflectivity: ( 0.8, 0.8, 0.8 ) by ( 0.5, 0.25, 0.125 ).</summary>
    [Test]
    public void Compute_EveryRayStrikingALitWall_IsItsLightTimesItsReflectivity()
    {
        AmbientCube cube = StaticPropAmbient.Compute(
            Vector3.Zero,
            static (_, _) => (Wall, new Vector3(0.8f)),
            static texdata => texdata == 3 ? new Vector3(0.5f, 0.25f, 0.125f) : Vector3.Zero,
            new Vector3(9f));

        foreach ((float red, float green, float blue) in Faces(cube))
        {
            red.ShouldBe(0.4f, 1e-5f);
            green.ShouldBe(0.2f, 1e-5f);
            blue.ShouldBe(0.1f, 1e-5f);
        }
    }

    /// <remarks>
    /// The weighting: a face takes the normals on its side, each by its cosine, and divides by their sum. Light from
    /// above only (every ray with z &gt; 0 returns 1) gives the +Z face exactly 1 and the −Z face exactly 0; a side
    /// face sees half its hemisphere lit, and by the table's mirror symmetry in z that is 0.5 — once the rays lying
    /// exactly on the horizon, which belong to neither half, return a half. (Without that the side faces read 0.4295,
    /// which is how this test's first prediction was wrong.)
    /// </remarks>
    [Test]
    public void Compute_LightFromAboveOnly_FillsTheTopFaceHalfOfEachSideAndNoneOfTheBottom()
    {
        AmbientCube cube = StaticPropAmbient.Compute(
            Vector3.Zero,
            static (start, end) =>
            {
                float up = end.Z - start.Z;

                // 1 above the horizon, 0 below it, a half on it.
                return (Wall, new Vector3((MathF.Sign(up) + 1f) * 0.5f));
            },
            static _ => Vector3.One,
            null);

        cube.PositiveZ.Red.ShouldBe(1f, 1e-5f);
        cube.NegativeZ.Red.ShouldBe(0f, 1e-5f);
        cube.PositiveX.Red.ShouldBe(0.5f, 1e-3f);
        cube.NegativeX.Red.ShouldBe(0.5f, 1e-3f);
        cube.PositiveY.Red.ShouldBe(0.5f, 1e-3f);
        cube.NegativeY.Red.ShouldBe(0.5f, 1e-3f);
    }

    private static IEnumerable<(float Red, float Green, float Blue)> Faces(AmbientCube cube) =>
        [cube.PositiveX, cube.NegativeX, cube.PositiveY, cube.NegativeY, cube.PositiveZ, cube.NegativeZ];

    private static DecalFace Face(SurfaceProperties flags, int texdata) =>
        new(
            0, [], Vector3.UnitX, 0f, default, default, (0, 0), (0, 0), true, false, false,
            new LuxelMapping(default, default, 0, 0, 1, 1), flags, texdata);
}
