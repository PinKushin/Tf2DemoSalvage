using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which of the light cache's states take an `emit_surface` light, and which fold what loses a slot (B669).</summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll` (TF2 x64). `ComputeStaticLightingForCacheEntry` (`0x1801b8270`) asks
/// `FUN_1801bb4b0` for the cube, which sets its out-flag only where it read the LEAF's samples (`FUN_180100f60`, a dynamic
/// caller under `r_radiosity` 4), and hands that flag to `FUN_1801b5a50`. There, `param_5 == 0 || (flags &amp; 1) == 0`:
/// a light is skipped only when the cube came from the leaf AND vrad put the light in the leaf cubes
/// (`DWL_FLAGS_INAMBIENTCUBE`). A static prop's 162-ray cube holds no surface light (`FUN_1801b74f0`), so it takes every one.
/// `FUN_1801b5a50` adds through `FUN_1801b60a0`, whose loser goes to the cube (`FUN_1801b5db0`).
///
/// The surface light sits 20 units above the lit point (0 0 100), facing down, so it contributes `1 · 1 / 400 · I`.
/// </remarks>
public sealed class SurfaceLightCacheConformanceTests
{
    [Test]
    public void StaticPropLightingAt_ASurfaceLightVradPutInTheLeafCube_TakesASlot()
    {
        StaticPlusDynamicLightingConformanceTests.Map([Surface(flags: 1)]).StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals
            .ShouldHaveSingleItem().Red.ShouldBe(4000f);
    }

    [Test]
    public void ModelLightingAt_ASurfaceLightVradPutInTheLeafCube_IsSkipped()
    {
        StaticPlusDynamicLightingConformanceTests.Map([Surface(flags: 1)]).ModelLightingAt(0f, 0f, 100f).Locals.ShouldBeEmpty();
    }

    [Test]
    public void ModelLightingAt_ASurfaceLightVradLeftOutOfTheLeafCube_TakesASlot()
    {
        StaticPlusDynamicLightingConformanceTests.Map([Surface(flags: 0)]).ModelLightingAt(0f, 0f, 100f).Locals
            .ShouldHaveSingleItem().Red.ShouldBe(4000f);
    }

    /// <summary>The flag is the leaf path's only skip, so a dlight merged over the same list does not bring the light back.</summary>
    [Test]
    public void ModelLightingAt_ADlightBesideASurfaceLightInTheLeafCube_StillSkipsIt()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([Surface(flags: 1)]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red) = (0f, 0f, 120f, 255f, 255);

        lighting.ModelLightingAt(0f, 0f, 100f).Locals.ShouldHaveSingleItem().Red.ShouldBe(1f);
    }

    /// <summary>
    /// **The static state folds** (`FUN_1801b60a0` to `FUN_1801b5db0`): with four lamps in the slots, a fifth light adds to
    /// the cube. It adds `n·d (1) · dot2 (1) · 1/400 · 4000` = 10 to the +Z face and nothing to the -Z one.
    /// </summary>
    [Test]
    public void StaticPropLightingAt_AFifthLight_IsFoldedIntoTheCube()
    {
        List<BspWorldLight> four =
        [
            .. new (float X, float Y, float Z)[] { (40f, 0f, 100f), (-40f, 0f, 100f), (0f, 40f, 100f), (0f, -40f, 100f) }
                .Select(static at => new BspWorldLight(at, (900000f, 900000f, 900000f), default, WorldLightKind.Point, QuadraticAttenuation: 1f)),
        ];

        AmbientCube without = StaticPlusDynamicLightingConformanceTests.Map(four).StaticPropLightingAt(0f, 0f, 100f).Lighting.Cube;
        PointLighting with = StaticPlusDynamicLightingConformanceTests.Map([.. four, Surface(flags: 1)]).StaticPropLightingAt(0f, 0f, 100f).Lighting;

        with.Locals.Count.ShouldBe(4);
        (with.Cube.PositiveZ.Red - without.PositiveZ.Red).ShouldBe(10f, 0.0001f);
        with.Cube.NegativeZ.Red.ShouldBe(without.NegativeZ.Red);
    }

    /// <summary>
    /// `FUN_1801b60a0` drops a light whose cluster is negative or out of the PVS `FUN_1801b8270` built, before it traces (the
    /// test map's point is in cluster 0, which sees only itself).
    /// </summary>
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    public void StaticPropLightingAt_ALightsCluster_MustBeInThePvs(int cluster, int expected)
    {
        StaticPlusDynamicLightingConformanceTests.Map([Surface(flags: 0) with { Cluster = cluster }], Clear)
            .StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals.Count.ShouldBe(expected);
    }

    /// <summary>The same gate on a moving model's entry, for a point lamp too: it is not a surface light's own rule.</summary>
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    public void ModelLightingAt_ALampsCluster_MustBeInThePvs(int cluster, int expected)
    {
        StaticPlusDynamicLightingConformanceTests.Map([StaticPlusDynamicLightingConformanceTests.Lamp(0, cluster)], Clear)
            .ModelLightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(expected);
    }

    /// <summary>A light out of the PVS is not folded either: the gate returns before the rank.</summary>
    [Test]
    public void StaticPropLightingAt_AFifthLightOutOfThePvs_IsNotFolded()
    {
        List<BspWorldLight> four =
        [
            .. new (float X, float Y, float Z)[] { (40f, 0f, 100f), (-40f, 0f, 100f), (0f, 40f, 100f), (0f, -40f, 100f) }
                .Select(static at => new BspWorldLight(at, (900000f, 900000f, 900000f), default, WorldLightKind.Point, QuadraticAttenuation: 1f, Cluster: 0)),
        ];

        AmbientCube without = StaticPlusDynamicLightingConformanceTests.Map(four, Clear).StaticPropLightingAt(0f, 0f, 100f).Lighting.Cube;

        StaticPlusDynamicLightingConformanceTests.Map([.. four, Surface(flags: 0) with { Cluster = 1 }], Clear)
            .StaticPropLightingAt(0f, 0f, 100f).Lighting.Cube.ShouldBe(without);
    }

    /// <summary>Every ray clear, so only the PVS gate can drop a light.</summary>
    private static float Clear((float X, float Y, float Z) from, (float X, float Y, float Z) to) => 1f;

    private static BspWorldLight Surface(int flags) =>
        new((0f, 0f, 120f), (4000f, 4000f, 4000f), (0f, 0f, -1f), WorldLightKind.Surface, Flags: flags);
}
