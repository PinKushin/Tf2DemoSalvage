using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>B425 step 2: a dlight on the world's lightmaps — which faces it marks, what it adds to each luxel, and the restore.</summary>
/// <remarks>
/// **Read from `engine.dll`; the account is `docs/findings/66-tf2-barely-uses-dynamic-lights.md`.**
/// - `R_PushDlights` `0x1800d48b0`: each dlight with `time &lt;= die`, `radius &gt; 0` and no `DLIGHT_NO_WORLD_ILLUMINATION`
///   walks the world's nodes (`0x1800d4520`): plane distance above `radius` goes front, below `−radius` back, otherwise
///   the node's faces are tried and both sides walked; a leaf tries its faces that are not `SURFDRAW_NODE` within
///   `±radius` of their plane (`0x1800d4680`).
/// - `R_TryLightMarkSurface` `0x1800d4970`: `d = n·o − dist`; needs `d ≥ −15` and `r² − d² &gt; 0`, then a circle of
///   `luxelsPerWorldUnit · √(r² − d²)` luxels round the light's lightmap coordinate must reach the face's luxel
///   rectangle (`0x1801735f0`). A hit ORs the light's bit into the face and stamps the frame.
/// - `R_RenderDynamicLightmaps` `0x1800d09a0`: with `r_dynamic` (default "1", `0x180007c90`) a face is rebuilt when it
///   was marked this frame OR still carries bits — which is the restore. `0x1800d0ba0` ANDs the bits with the active
///   mask, drops every bit when the face was not marked this frame, and per light keeps it only for an active light
///   with no bit in `flags &amp; 0xd` and the same plane test.
/// - `R_BuildLightMap` `0x1800cfca0` → `R_AddDynamicLights` `0x1800ceb00` (flat) / `0x1800cf1b0` (bumped): per luxel
///   `dist² = ((s − col)·wupl)² + ((t − row)·wupl)² + d²`, and below `r²` it adds
///   `colour · 2^e/255 · style/264 · min( (d²==0 ? 1 : minlight·r²/dist²) · (1 − dist²/r²), 2 )`, minlight floored at
///   1/256. The port's samples are 255 times the engine's (`byte · 2^e` against `TexLightToLinear`'s `/255`,
///   `mathlib.h:975`, `color_conversion.cpp:38`), so the colour term here is `colour · 2^e · style`.
/// - Bumped: the flat page takes the same, and page `k` takes it `· max(dir·bump_k, 0) / max(dir·n, 0.001)`, the
///   direction from the luxel ROW's position only (`origin + t_vec · row · wupl²`; no column term, read in the
///   disassembly at `0x1800cf460`), the basis from `GetBumpNormals` (`bumpvects.cpp`) on the normalised lightmap vectors.
/// - `luxelsPerWorldUnit = |lightmapVecs[0].xyz|`, `worldUnitsPerLuxel` its reciprocal: `Mod_LoadTexinfo` `0x180103da0`.
/// </remarks>
public sealed class WorldDynamicLightConformanceTests
{
    /// <summary>Luxels across and down the test floor.</summary>
    private const int Size = 5;

    [Test]
    public void Falloff_AtTwentyFourUnitsBelowARadiusOfOneHundred_IsTheEnginesCurve()
    {
        // (1/256 · 10000 / 576) · (1 − 576/10000)
        WorldDynamicLights.Falloff(576f, 10000f, 0f).ShouldBe(0.06391059f, 1e-7f);
    }

    [Test]
    public void Falloff_AtTheLightItself_IsOne()
    {
        WorldDynamicLights.Falloff(0f, 10000f, 0f).ShouldBe(1f);
    }

    [Test]
    public void Falloff_CloseToABrightMinlight_IsClampedAtTwo()
    {
        // (1 · 10000 / 100) · 0.99 = 99, clamped.
        WorldDynamicLights.Falloff(100f, 10000f, 1f).ShouldBe(2f);
    }

    [Test]
    public void Falloff_AtOrBeyondTheRadius_IsZero()
    {
        WorldDynamicLights.Falloff(10000f, 10000f, 0f).ShouldBe(0f);
        WorldDynamicLights.Falloff(10001f, 10000f, 0f).ShouldBe(0f);
    }

    [Test]
    public void Frame_ALightAboveAFloorLuxel_AddsTheFalloffToEachLuxel()
    {
        LightmapAtlas atlas = Atlas();
        WorldDynamicLights world = new(World());
        DynamicLights lights = Lights((32f, 32f, 24f));

        world.Frame(lights, atlas, _ => 1f, []);

        // 1020 · 0.06391059 = 65.19, halved into the byte: 32.
        Texel(atlas, 0, 2, 2).ShouldBe((byte)32);

        // dist² = 32² + 32² + 24² = 2624: 1020 · 0.0109804 = 11.20, halved: 5.
        Texel(atlas, 0, 0, 0).ShouldBe((byte)5);
        world.Bits(0).ShouldBe(1u);
    }

    [Test]
    public void Frame_ALightStyleOnTheLight_ScalesItsColour()
    {
        LightmapAtlas atlas = Atlas();
        WorldDynamicLights world = new(World());
        DynamicLights lights = Lights((32f, 32f, 24f));
        lights.Dlights[0].Style = 5;

        world.Frame(lights, atlas, style => style == 5 ? 0.5f : 1f, []);

        // 510 · 0.06391059 = 32.59, halved: 16.
        Texel(atlas, 0, 2, 2).ShouldBe((byte)16);
    }

    [TestCase(-14f, 1u)]
    [TestCase(-16f, 0u)]
    [TestCase(99f, 1u)]
    [TestCase(100f, 0u)]
    public void Frame_ALightAtAPlaneDistance_MarksTheFaceOnlyWithinTheEnginesBounds(float height, uint bits)
    {
        WorldDynamicLights world = new(World());

        world.Frame(Lights((32f, 32f, height)), Atlas(), _ => 1f, []);

        world.Bits(0).ShouldBe(bits);
    }

    /// <remarks>
    /// The root splits on x = 1000, far behind the light, so the walk takes the back child into leaf 0 (`0x1800d4520`),
    /// whose faces are tried unless `SURFDRAW_NODE` says a node pass owns them (`0x1800d4680`: `*surf &amp; 2`).
    /// </remarks>
    [TestCase(false, 1u)]
    [TestCase(true, 0u)]
    public void Frame_AFaceReachedThroughALeaf_IsMarkedUnlessItLiesOnANode(bool onNode, uint bits)
    {
        DecalWorld leafy = new(
            [new DecalNode(-1, -1, Vector3.UnitX, 1000f, 0, 0)], [[0]], [Floor() with { OnNode = onNode }]);
        WorldDynamicLights world = new(leafy);

        world.Frame(Lights((32f, 32f, 24f)), Atlas(), _ => 1f, []);

        world.Bits(0).ShouldBe(bits);
    }

    [Test]
    public void Frame_ALightWhoseCircleMissesTheLuxelRectangle_MarksNothing()
    {
        WorldDynamicLights world = new(World());

        // 99 above: a circle of √199 / 16 = 0.88 luxels round (s, t) = (6, 2), which is 2 luxels past the last column.
        world.Frame(Lights((96f, 32f, 99f)), Atlas(), _ => 1f, []);

        world.Bits(0).ShouldBe(0u);
    }

    [TestCase(DynamicLights.NoWorldIllumination)]
    [TestCase(0x4)]
    [TestCase(0x8)]
    public void Frame_ALightWithAWorldRefusingFlag_LeavesTheFaceBaked(int flags)
    {
        LightmapAtlas atlas = Atlas();
        WorldDynamicLights world = new(World());
        DynamicLights lights = Lights((32f, 32f, 24f));
        lights.Dlights[0].Flags = flags;

        world.Frame(lights, atlas, _ => 1f, []);

        Texel(atlas, 0, 2, 2).ShouldBe((byte)0);
        world.Bits(0).ShouldBe(0u);
    }

    [Test]
    public void Frame_ModelOnlyFlag_StillLightsTheWorld()
    {
        LightmapAtlas atlas = Atlas();
        WorldDynamicLights world = new(World());
        DynamicLights lights = Lights((32f, 32f, 24f));
        lights.Dlights[0].Flags = DynamicLights.NoModelIllumination;

        world.Frame(lights, atlas, _ => 1f, []);

        Texel(atlas, 0, 2, 2).ShouldBe((byte)32);
    }

    [Test]
    public void Frame_TwoLightsInSlotsZeroAndThree_SetsBothBits()
    {
        DynamicLights lights = new() { Time = 1f };

        for (int key = 1; key <= 4; key++)
        {
            DynamicLight light = lights.AllocDlight(key);
            light.Die = 2f;
            (light.X, light.Y, light.Z) = (32f, 32f, 24f);
            light.Radius = key is 1 or 4 ? 100f : 0f;
        }

        WorldDynamicLights world = new(World());

        world.Frame(lights, Atlas(), _ => 1f, []);

        world.Bits(0).ShouldBe(0b1001u);
    }

    [Test]
    public void Frame_AfterTheLightDies_RebuildsTheFaceBakedOnceThenLeavesIt()
    {
        LightmapAtlas atlas = Atlas();
        WorldDynamicLights world = new(World());
        DynamicLights lights = Lights((32f, 32f, 24f));
        List<AtlasRegion> dirty = [];

        world.Frame(lights, atlas, _ => 1f, dirty);
        dirty.Count.ShouldBe(1);
        Texel(atlas, 0, 2, 2).ShouldBe((byte)32);

        // Past `die`: R_PushDlights skips it, and the face still carries its bit, so it is rebuilt without it.
        lights.Time = 3f;
        dirty.Clear();
        world.Frame(lights, atlas, _ => 1f, dirty);

        dirty.Count.ShouldBe(1);
        Texel(atlas, 0, 2, 2).ShouldBe((byte)0);
        world.Bits(0).ShouldBe(0u);

        dirty.Clear();
        world.Frame(lights, atlas, _ => 1f, dirty);

        dirty.ShouldBeEmpty();
    }

    /// <remarks>
    /// A face's bits are ORed, never reset, by the marking pass, so light 0's bit from the first frame is still on the
    /// face when light 1 marks it in the second. `0x1800d0ba0` then drops light 0 on its own plane test: it has moved
    /// 50 units behind the floor.
    /// </remarks>
    [Test]
    public void Frame_AStaleBitWhoseLightMovedBehindThePlane_IsDroppedByTheBuildsOwnPlaneTest()
    {
        LightmapAtlas atlas = Atlas();
        WorldDynamicLights world = new(World());
        DynamicLights lights = Lights((32f, 32f, 24f));

        world.Frame(lights, atlas, _ => 1f, []);

        (lights.Dlights[0].X, lights.Dlights[0].Y, lights.Dlights[0].Z) = (32f, 32f, -50f);
        DynamicLight second = lights.AllocDlight(2);
        (second.X, second.Y, second.Z) = (32f, 32f, 24f);
        second.Radius = 100f;
        (second.Red, second.Green, second.Blue, second.Exponent) = (255, 255, 255, 2);
        second.Die = 2f;

        world.Frame(lights, atlas, _ => 1f, []);

        world.Bits(0).ShouldBe(0b10u);
        Texel(atlas, 0, 2, 2).ShouldBe((byte)32);
    }

    [Test]
    public void Frame_ABumpedFaceUnderALight_AddsEachBasisVectorsShare()
    {
        LightmapAtlas atlas = Atlas(bumped: true);
        WorldDynamicLights world = new(World());

        world.Frame(Lights((0f, 0f, 24f)), atlas, _ => 1f, []);

        // Flat: 1020 · 0.06391059 = 65.19 → 32. Each basis vector is 1/√3 up the normal: 37.64 → 18.
        Texel(atlas, 0, 0, 0).ShouldBe((byte)32);
        Texel(atlas, 1, 0, 0).ShouldBe((byte)18);
        Texel(atlas, 2, 0, 0).ShouldBe((byte)18);
        Texel(atlas, 3, 0, 0).ShouldBe((byte)18);
    }

    [Test]
    public void Frame_ABumpedLuxelAlongTheRow_TakesItsDirectionFromTheRowAlone()
    {
        LightmapAtlas atlas = Atlas(bumped: true);
        WorldDynamicLights world = new(World());

        world.Frame(Lights((0f, 0f, 24f)), atlas, _ => 1f, []);

        // Column 2, row 0: dist² = 1600, falloff 0.0205078. The engine's direction is still straight up (it has no
        // column term), so every page takes 1020 · 0.0205078 / √3 = 12.08 → 6; a true direction would lean away
        // from basis vector 0 and give it nothing.
        Texel(atlas, 0, 2, 0).ShouldBe((byte)10);
        Texel(atlas, 1, 2, 0).ShouldBe((byte)6);
        Texel(atlas, 2, 2, 0).ShouldBe((byte)6);
        Texel(atlas, 3, 2, 0).ShouldBe((byte)6);
    }

    [Test]
    public void Frame_ABumpedLuxelLitAtASlant_DividesByTheNormalsShareAndSkipsABasisFacingAway()
    {
        LightmapAtlas atlas = Atlas(bumped: true);
        WorldDynamicLights world = new(World());

        world.Frame(Lights((0f, 32f, 24f)), atlas, _ => 1f, []);

        // Luxel (0, 0): dist² 1600, falloff 0.0205078; direction (0, 0.8, 0.6), so the share is 0.0205078 / 0.6.
        // Basis 0 takes 1020 · 0.34641 · 0.0341797 = 12.08 → 6, basis 1 · 0.91210 = 31.80 → 15, basis 2 faces away.
        Texel(atlas, 0, 0, 0).ShouldBe((byte)10);
        Texel(atlas, 1, 0, 0).ShouldBe((byte)6);
        Texel(atlas, 2, 0, 0).ShouldBe((byte)15);
        Texel(atlas, 3, 0, 0).ShouldBe((byte)0);
    }

    [Test]
    public void Pushes_ALiveLight_IsWalkedAndADeadOrWorldRefusingOneIsNot()
    {
        DynamicLights lights = Lights((0f, 0f, 24f));

        WorldDynamicLights.Pushes(lights.Dlights[0], 1f).ShouldBeTrue();
        WorldDynamicLights.Pushes(lights.Dlights[0], 2.5f).ShouldBeFalse();

        lights.Dlights[0].Flags = DynamicLights.NoWorldIllumination;
        WorldDynamicLights.Pushes(lights.Dlights[0], 1f).ShouldBeFalse();
    }

    /// <summary>A white-coloured dlight of radius 100 at 2^2, key 1 (slot 0), alive until 2 at time 1.</summary>
    private static DynamicLights Lights((float X, float Y, float Z) at)
    {
        DynamicLights lights = new() { Time = 1f };
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z) = at;
        light.Radius = 100f;
        (light.Red, light.Green, light.Blue, light.Exponent) = (255, 255, 255, 2);
        light.Die = 2f;

        return lights;
    }

    /// <summary>A 5×5-luxel floor on z = 0, a luxel every 16 units, its lightmap origin at the world origin.</summary>
    internal static DecalFace Floor(int size = Size) =>
        new(
            0,
            [new(0f, 0f, 0f), new(size * 16f, 0f, 0f), new(size * 16f, size * 16f, 0f), new(0f, size * 16f, 0f)],
            Vector3.UnitZ,
            0f,
            Vector4.Zero,
            Vector4.Zero,
            (0, 0),
            (0, 0),
            OnNode: true,
            Displacement: false,
            RefusesDecals: false,
            new LuxelMapping((1f / 16f, 0f, 0f, 0f), (0f, 1f / 16f, 0f, 0f), 0, 0, size, size));

    /// <summary>One node on the floor's plane holding the floor, both children the one empty leaf.</summary>
    internal static DecalWorld World(int size = Size) =>
        new([new DecalNode(-1, -1, Vector3.UnitZ, 0f, 0, 1)], [Array.Empty<int>()], [Floor(size)]);

    /// <summary>The floor's baked lighting, black, with three directional sets when bumped.</summary>
    internal static LightmapAtlas Atlas(bool bumped = false, int size = Size)
    {
        byte[] black = new byte[size * size * 4];

        for (int luxel = 0; luxel < size * size; luxel++)
        {
            black[(luxel * 4) + 3] = 255;
        }

        BspLightmap flat = new(size, size, black);

        return LightmapAtlas.PackAll([new BspFaceLighting(flat, bumped ? [flat, flat, flat] : [])]);
    }

    /// <summary>The red byte of face 0's luxel in a set.</summary>
    internal static byte Texel(LightmapAtlas atlas, int set, int column, int row, int size = Size)
    {
        AtlasRect rect = atlas.Rectangles[0];
        int x = (int)MathF.Round((rect.U * atlas.Width) - 0.5f);
        int y = (int)MathF.Round((rect.V * atlas.Height) - 0.5f);

        return atlas.Pixels[((((y + row) * atlas.Width) + x + (set * size) + column) * 4)];
    }
}
