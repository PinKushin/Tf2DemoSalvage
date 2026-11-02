using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which of the light cache's callers make the per-light visibility trace, and from which point (B666).</summary>
/// <remarks>
/// *Read in disassembly*, `engine.dll` (TF2 x64): `AddWorldLightToLightingState` (`0x1801b60a0`) takes a `skipTrace`
/// argument that becomes `FUN_1801b9570`'s flags bit 0. Traced: the style-0 world lights (`0x1801b5a50`, from
/// `ComputeStaticLightingForCacheEntry`), the cache's styled lights (`0x1801ba322`), its stored lists (`0x1801b5890`) and a
/// static prop handle's styled list (`0x1801b5400`). Not traced: live dlights (`0x1801b7a10`, `0x1801bb040`) and the
/// handle list's admission (`FUN_1801b6bf0`, flags `9|2`). The ray runs from the caller's point — the cache cell's, or a
/// static prop's own lighting origin — to the light, and a light blocked more than eight units short of it is dropped
/// (`LocalLightVisibilityConformanceTests`).
///
/// Every lamp here is 20 units above the lit point (0 0 100). <see cref="Wall"/> stops the ray from that point to that
/// lamp half way, 10 units short, and leaves every other ray clear — so a trace cast from another point, or the other
/// way, finds nothing and the test fails.
/// </remarks>
public sealed class LightVisibilityTraceConformanceTests
{
    private const int Style = 5;

    [Test]
    public void StaticPropLightingAt_AStyleZeroLampBehindAWall_TakesNoSlot()
    {
        StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)]).StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals.Count
            .ShouldBe(1, "the control: untraced, the lamp reaches");

        StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)], Wall).StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals
            .ShouldBeEmpty();
    }

    [Test]
    public void ModelLightingAt_AWorldLampBehindAWall_TakesNoSlotAndIsNotFolded()
    {
        LevelLighting open = StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)]);
        LevelLighting walled = StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)], Wall);

        open.ModelLightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(1, "the control");

        PointLighting lit = walled.ModelLightingAt(0f, 0f, 100f);

        lit.Locals.ShouldBeEmpty();
        lit.Cube.ShouldBe(
            StaticPlusDynamicLightingConformanceTests.Map([]).ModelLightingAt(0f, 0f, 100f).Cube, "the bounce alone: the lamp is not folded");
    }

    /// <summary>A dlight at the lamp's own spot, behind the same wall, still lights: `0x1801b7a10` skips the trace.</summary>
    [Test]
    public void ModelLightingAt_ADlightBehindTheWall_IsNotTraced()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)], Wall);
        DynamicLights lights = new() { Time = 1f };
        lighting.Dynamic = lights;
        DynamicLight light = lights.AllocDlight(1);
        (light.X, light.Y, light.Z, light.Radius, light.Red) = (0f, 0f, 120f, 255f, 255);

        // The dlight's red is 255 · 2⁰ / 255 = 1; the lamp's is 400, and the wall drops it.
        lighting.ModelLightingAt(0f, 0f, 100f).Locals.ShouldHaveSingleItem().Red.ShouldBe(1f);
    }

    /// <summary>
    /// The handle's list admits a styled lamp behind the wall (flags `9|2` skip the trace), and bit 4 then drops it when it
    /// ranks the list (`FUN_1801b5400`, flags 0).
    /// </summary>
    [Test]
    public void StaticPlusDynamicLights_AStyledLampBehindAWall_IsListedButNotRanked()
    {
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([Lamp(Style)], Wall);

        lighting.StyledLights(0f, 0f, 100f).ShouldBe([0]);
        lighting.StaticPlusDynamicLights(1000, 0f, 0f, 100f).ShouldBeEmpty();
    }

    /// <summary>Flags 7 with a styled lamp ranked: the style-0 lamp behind the wall is not added beside it.</summary>
    [Test]
    public void StaticPropFullLightingAt_AStyleZeroLampBehindAWallBesideAStyledOne_TakesOnlyTheStyledOne()
    {
        BspWorldLight styled = Lamp(Style) with { Origin = (0f, 20f, 100f) };
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map([styled, Lamp(0)], Wall);

        lighting.StaticPropFullLightingAt(1000, 0f, 0f, 100f).Lighting.Locals.ShouldHaveSingleItem().Y.ShouldBe(20f);
    }

    /// <summary>Half of the ray from (0 0 100) to (0 0 120) is blocked; every other ray is clear.</summary>
    private static float Wall((float X, float Y, float Z) from, (float X, float Y, float Z) to) =>
        from == (0f, 0f, 100f) && to == (0f, 0f, 120f) ? 0.5f : 1f;

    private static BspWorldLight Lamp(int style) => StaticPlusDynamicLightingConformanceTests.Lamp(style, cluster: 0);
}
