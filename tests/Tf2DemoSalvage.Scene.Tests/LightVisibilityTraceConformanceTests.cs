using System;
using System.Collections.Generic;
using System.Linq;

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
    public void ModelLightingAt_AWorldLampBehindAWall_TakesNoSlot()
    {
        StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)]).ModelLightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(1, "the control");
        StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)], Wall).ModelLightingAt(0f, 0f, 100f).Locals.ShouldBeEmpty();
    }

    /// <summary>Four brighter lamps take the slots, so the walled lamp would be the fifth, folded; occluded, it is not.</summary>
    [Test]
    public void ModelLightingAt_AFifthLampBehindAWall_IsNotFoldedIntoTheCube()
    {
        List<BspWorldLight> four =
        [
            .. new (float X, float Y, float Z)[] { (20f, 0f, 100f), (-20f, 0f, 100f), (0f, 20f, 100f), (0f, -20f, 100f) }
                .Select(at => Lamp(0) with { Origin = at, Intensity = (800f, 800f, 800f) }),
        ];

        AmbientCube withoutIt = StaticPlusDynamicLightingConformanceTests.Map(four).ModelLightingAt(0f, 0f, 100f).Cube;

        StaticPlusDynamicLightingConformanceTests.Map([.. four, Lamp(0)]).ModelLightingAt(0f, 0f, 100f).Cube
            .ShouldNotBe(withoutIt, "the control: seen, the fifth lamp is folded");

        PointLighting walled = StaticPlusDynamicLightingConformanceTests.Map([.. four, Lamp(0)], Wall).ModelLightingAt(0f, 0f, 100f);

        walled.Locals.Count.ShouldBe(4);
        walled.Cube.ShouldBe(withoutIt);
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

    /// <summary>
    /// An entry relit on a style change traces nothing again: the world and its point have not moved, so the answer is
    /// remembered. The lights it is relit with are the same.
    /// </summary>
    [Test]
    public void ModelLightingAt_AnEntryRelitAfterAStyleChange_TracesNoLightTwice()
    {
        int traces = 0;
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map(
            [Lamp(0), Lamp(Style) with { Origin = (0f, 20f, 100f) }],
            (from, to) =>
            {
                traces++;
                return Wall(from, to);
            });

        IReadOnlyList<LocalLight> first = lighting.ModelLightingAt(0f, 0f, 100f).Locals;
        traces.ShouldBe(2, "the control: both lamps traced once");

        lighting.StylesChanged([Style]);
        IReadOnlyList<LocalLight> again = lighting.ModelLightingAt(0f, 0f, 100f).Locals;

        traces.ShouldBe(2);
        again.ShouldBe(first);
        again.ShouldHaveSingleItem().Y.ShouldBe(20f, "the walled lamp stays out");
    }

    /// <summary>
    /// The trace starts at the light cache's point, not the model's: a model at (1 1 100) is lit at its cell's centre
    /// (16 16 64), so a wall between THAT point and the lamp drops it (`LightcacheGet` passes the entry's point; B666).
    /// </summary>
    [Test]
    public void ModelLightingAt_ACellPointBehindAWallFromTheLamp_DropsTheLampTheModelPointWouldSee()
    {
        BspWorldLight lamp = Lamp(0) with { Origin = (16f, 16f, 84f) };

        // A wall only between the cell's centre and the lamp; every other ray, the model's own included, is clear.
        static float WallAtTheCell((float X, float Y, float Z) from, (float X, float Y, float Z) to) =>
            from == (16f, 16f, 64f) && to == (16f, 16f, 84f) ? 0.5f : 1f;

        StaticPlusDynamicLightingConformanceTests.Map([lamp], WallAtTheCell).ModelLightingAt(1f, 1f, 100f).Locals.Count
            .ShouldBe(1, "the control: with no reach test the model is lit where it stands, and sees the lamp");

        StaticPlusDynamicLightingConformanceTests.Map([lamp], WallAtTheCell, reaches: static (_, _) => true)
            .ModelLightingAt(1f, 1f, 100f).Locals.ShouldBeEmpty();
    }

    /// <summary>Without a trace wired, <see cref="LevelLighting.Occlusion"/> says so rather than calling a light seen.</summary>
    [Test]
    public void Occlusion_WithNoTraceWired_IsNull()
    {
        StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)]).Occlusion((0f, 0f, 120f), 0f, 0f, 100f).ShouldBeNull();
        StaticPlusDynamicLightingConformanceTests.Map([Lamp(0)], Wall).Occlusion((0f, 0f, 120f), 0f, 0f, 100f)
            .ShouldBe((10f, false), "the control: wired, the wall blocks 10 of the 20 units");
    }

    /// <summary>
    /// What <see cref="LevelLighting.Occlusion"/> reports is what the ranking read: the remembered answer, not a second
    /// trace — so a probe printing it prints the value production used.
    /// </summary>
    [Test]
    public void Occlusion_AfterTheRankingTracedALight_ReadsTheSameAnswerWithoutTracing()
    {
        int traces = 0;
        LevelLighting lighting = StaticPlusDynamicLightingConformanceTests.Map(
            [Lamp(0)],
            (from, to) =>
            {
                traces++;
                return Wall(from, to);
            });

        lighting.StaticPropLightingAt(0f, 0f, 100f).Lighting.Locals.ShouldBeEmpty("the control: the ranking traced it and dropped it");
        traces.ShouldBe(1);

        lighting.Occlusion((0f, 0f, 120f), 0f, 0f, 100f).ShouldBe((10f, false));
        traces.ShouldBe(1);
    }

    /// <summary>
    /// With every answer remembered, the trace adds no allocation to a ranking: the same call allocates exactly what it does
    /// with no trace wired (the delegate is made once, not per call).
    /// </summary>
    [Test]
    public void LightingAt_TracedWithTheAnswersRemembered_AllocatesNoMoreThanUntraced()
    {
        BspWorldLight[] lamps = [Lamp(0), Lamp(0) with { Origin = (0f, 20f, 100f) }, Lamp(0) with { Origin = (20f, 0f, 100f) }];
        LevelLighting untraced = StaticPlusDynamicLightingConformanceTests.Map(lamps);
        LevelLighting traced = StaticPlusDynamicLightingConformanceTests.Map(lamps, static (_, _) => 1f);

        traced.LightingAt(0f, 0f, 100f).Locals.Count.ShouldBe(3, "the control: every lamp seen");

        Allocated(untraced).ShouldBe(Allocated(traced));

        static long Allocated(LevelLighting lighting)
        {
            lighting.LightingAt(0f, 0f, 100f);
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int call = 0; call < 100; call++)
            {
                lighting.LightingAt(0f, 0f, 100f);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    /// <summary>Half of the ray from (0 0 100) to (0 0 120) is blocked; every other ray is clear.</summary>
    private static float Wall((float X, float Y, float Z) from, (float X, float Y, float Z) to) =>
        from == (0f, 0f, 100f) && to == (0f, 0f, 120f) ? 0.5f : 1f;

    private static BspWorldLight Lamp(int style) => StaticPlusDynamicLightingConformanceTests.Lamp(style, cluster: 0);
}
