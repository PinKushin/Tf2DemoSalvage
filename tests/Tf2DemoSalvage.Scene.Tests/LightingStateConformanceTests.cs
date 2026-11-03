using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What the engine does past the four slots, and what `IVEngineClient::ComputeLighting` reads (B668, B669).</summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll` (TF2 x64). `FUN_1801b7fd0` is ComputeLighting: `FUN_1801b9cd0` (LightcacheGet) with flags
/// `0xf`, the model's path, trace and gate included; with no normal, each world light in a slot is then folded into the cube at the asked
/// point, flags 1. `FUN_1801ba590` flags 7 and 6: `FUN_1801b5400` adds each listed lamp to the state; `FUN_1801b6550` each dlight.
/// Both append while a slot is free, then evict the weakest below a threshold (`FUN_1801b89a0`): the light's own strength, or 100000 for a dlight.
/// What is evicted or loses goes to the cube (`FUN_1801b5db0`).
///
/// Every point is (0 0 100), in the map's cluster 0, and the map's bounce cube is zero, so a cube's value is only what was folded.
/// </remarks>
public sealed class LightingStateConformanceTests
{
    private const int Style = 5;

    [Test]
    public void ComputeLighting_ALampInASlot_IsFoldedAtThePoint()
    {
        Map([Lamp((0f, 0f, 120f), 400f)]).ComputeLighting(0f, 0f, 100f).PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void ComputeLighting_ALampBehindAWall_AddsNothing()
    {
        Map([Lamp((0f, 0f, 120f), 400f)], static (_, _) => 0f).ComputeLighting(0f, 0f, 100f).PositiveZ.Red.ShouldBe(0f);
    }

    [Test]
    public void ComputeLighting_ALampOutOfThePvs_AddsNothing()
    {
        Map([Lamp((0f, 0f, 120f), 400f) with { Cluster = 1 }]).ComputeLighting(0f, 0f, 100f).PositiveZ.Red.ShouldBe(0f);
    }

    [TestCase(1, 0f)]
    [TestCase(0, 10f)]
    public void ComputeLighting_ASurfaceLight_FollowsVradsFlag(int flags, float expected)
    {
        BspWorldLight surface = new((0f, 0f, 120f), (4000f, 4000f, 4000f), (0f, 0f, -1f), WorldLightKind.Surface, Cluster: 0, Flags: flags);

        Map([surface]).ComputeLighting(0f, 0f, 100f).PositiveZ.Red.ShouldBe(expected, 0.001f);
    }

    /// <summary>A styled lamp at 4 evicts the weakest of the four, at 1, whose light then arrives through the +Z face.</summary>
    [Test]
    public void StaticPropFullLightingAt_AStrongerStyledLamp_EvictsAndFoldsTheWeakest()
    {
        PointLighting state = Computed([.. Bright, Lamp((0f, 0f, 120f), 400f), Lamp((0f, 0f, 80f), 1600f) with { Style = Style }])
            .StaticPropFullLightingAt(1000, 0f, 0f, 100f).Lighting;

        state.Locals.Count.ShouldBe(4);
        state.Locals.Select(static local => local.Red).ShouldContain(1600f);
        state.Locals.Select(static local => local.Red).ShouldNotContain(400f);
        state.Cube.PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    /// <summary>A styled lamp weaker than all four does not vanish: it lands in the cube.</summary>
    [Test]
    public void StaticPropFullLightingAt_AWeakerStyledLamp_IsFolded()
    {
        PointLighting state = Computed([.. Bright, Lamp((0f, 0f, 120f), 800f), Lamp((0f, 0f, 120f), 100f) with { Style = Style }])
            .StaticPropFullLightingAt(1000, 0f, 0f, 100f).Lighting;

        state.Locals.Select(static local => local.Red).ShouldNotContain(100f);
        state.Cube.PositiveZ.Red.ShouldBe(0.25f, 0.001f);
    }

    /// <summary>A dlight far weaker than the slots still takes one: 100000 is its threshold. What it evicts is folded.</summary>
    [Test]
    public void StaticPropFullLightingAt_ALiveDlight_EvictsTheWeakestWhateverItsStrength()
    {
        LevelLighting lighting = Computed([.. Bright, Lamp((0f, 0f, 120f), 800f)]);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red) = (0f, 0f, 120f, 255f, 255);

        PointLighting state = lighting.StaticPropFullLightingAt(1000, 0f, 0f, 100f).Lighting;

        state.Locals.Count.ShouldBe(4);
        state.Locals.Select(static local => local.Red).ShouldContain(1f);
        state.Locals.Select(static local => local.Red).ShouldNotContain(800f);
        state.Cube.PositiveZ.Red.ShouldBe(2f, 0.001f);
    }

    /// <summary>Flags 6 starts from no cube, and the fifth listed lamp lands in it.</summary>
    [Test]
    public void StaticPlusDynamicLights_AFifthStyledLamp_IsFolded()
    {
        PointLighting state = Map([.. Bright.Select(static lamp => lamp with { Style = Style }), Lamp((0f, 0f, 120f), 800f) with { Style = Style }, Lamp((0f, 0f, 120f), 100f) with { Style = Style }])
            .StaticPlusDynamicLights(1000, 0f, 0f, 100f);

        state.Locals.Count.ShouldBe(4);
        state.Cube.PositiveZ.Red.ShouldBe(0.25f, 0.001f);
    }

    /// <summary>`FUN_1801b6bf0` tests the row: not listed from a point in −1 or for a lamp out of the PVS. With no vis data the row is all ones.</summary>
    [TestCase(true, 0, 1)]
    [TestCase(true, 1, 0)]
    [TestCase(false, 1, 1)]
    [TestCase(false, -1, 0)]
    public void StaticPlusDynamicLights_ALampsCluster_FollowsCmVis(bool vis, int cluster, int expected)
    {
        StaticPlusDynamicLightingConformanceTests.Map([StaticPlusDynamicLightingConformanceTests.Lamp(Style, cluster)], Clear, withVis: vis)
            .StaticPlusDynamicLights(1000, 0f, 0f, 100f).Locals.Count.ShouldBe(expected);
    }

    [Test]
    public void StyledLights_APointInClusterMinusOneWithNoVisData_ListsNothing()
    {
        StaticPlusDynamicLightingConformanceTests.Map(
                [StaticPlusDynamicLightingConformanceTests.Lamp(Style, 0)], Clear, withVis: false, backCluster: -1)
            .StyledLights(0f, 0f, -100f).ShouldBeEmpty();
    }

    /// <summary>`FUN_1801b7a10`: a dlight's cluster must be 0 or more and in the row, and a −1 point's row is all zeros, with vis data or not.</summary>
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void ModelLightingAt_ADlightInClusterMinusOneWithNoVisData_IsOut(bool above, int expected)
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([], withVis: false, backCluster: -1);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red) = (0f, 0f, above ? 120f : -120f, 255f, 255);

        lighting.ModelLightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(expected);
    }

    /// <summary>A trace that throws must not leave the shared delegate claimed, or the next ranking asserts and reads the wrong list.</summary>
    [Test]
    public void StaticPropLightingAt_ATraceThatThrows_LeavesTheNextRankingWorking()
    {
        bool thrown = false;
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map(
            [Lamp((0f, 0f, 120f), 400f)],
            (_, _) =>
            {
                if (!thrown)
                {
                    thrown = true;
                    throw new InvalidOperationException("the trace failed");
                }

                return 1f;
            });

        Should.Throw<InvalidOperationException>(() => lighting.StaticPropLightingAt(0f, 0f, 100f));

        lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals.Count.ShouldBe(1);
    }

    /// <summary>B243: the instrument reports what the ranking used, so a clear ray to a light out of the PVS is not "kept".</summary>
    [TestCase(0, true, true)]
    [TestCase(1, false, false)]
    public void Occlusion_ALightsCluster_IsTheGatesAnswer(int cluster, bool inPvs, bool kept)
    {
        BspWorldLight lamp = Lamp((0f, 0f, 120f), 400f) with { Cluster = cluster };

        Map([lamp], static (_, _) => 1f).Occlusion(lamp, 0f, 0f, 100f).ShouldBe((0f, inPvs, kept));
    }

    private static readonly BspWorldLight[] Bright =
    [
        Lamp((20f, 0f, 100f), 90000f),
        Lamp((-20f, 0f, 100f), 90000f),
        Lamp((0f, 20f, 100f), 90000f),
    ];

    private static float Clear((float X, float Y, float Z) from, (float X, float Y, float Z) to) => 1f;

    private static LevelLighting Map(
        IReadOnlyList<BspWorldLight> lights, Func<(float X, float Y, float Z), (float X, float Y, float Z), float>? trace = null) =>
        StaticPlusDynamicLightingConformanceTests.Map(lights, trace ?? Clear);

    private static LevelLighting Computed(IReadOnlyList<BspWorldLight> lights) =>
        StaticPlusDynamicLightingConformanceTests.Map(lights, Clear, staticPropAmbient: static (_, _, _) => default);

    private static BspWorldLight Lamp((float X, float Y, float Z) origin, float bright) => LevelLightingTests.Lamp(origin, bright);
}
