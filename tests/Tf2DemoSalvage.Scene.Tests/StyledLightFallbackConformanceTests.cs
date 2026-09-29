using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>B424: a baked static prop takes full lighting in a frame where a light in its handle's list animates.</summary>
/// <remarks>
/// **Read from `engine.dll`.** `R_AnimateLight` (`0x1800d3ec0`) stores each style's pattern LENGTH in `DAT_18069dd40`
/// (zero for an empty pattern). The cache creator `FUN_1801b8350` (from `CStaticPropMgr::PrecacheLighting`
/// `0x180205b20`, at the prop's lighting origin) fills the handle's light list through `FUN_1801b6bf0`: every
/// `dworldlight_t` whose style (`+0x2c`) is NONZERO, whose cluster (`+0x24`) is in the PVS of the handle's leaf, and
/// whose contribution at the handle's origin (`FUN_1801b99a0`, flags `9|2` — no style scale, no trace) is positive.
/// Each frame `FUN_1801bb830` sends the prop to full lighting (`FUN_1801ba590(…, 7)`, `*param_7 = 0`) when any listed
/// light's style has `DAT_18069dd40[style] > 1`.
/// </remarks>
public sealed class StyledLightFallbackConformanceTests
{
    private const int Style = 5;

    /// <summary>A flicker pattern: more than one letter.</summary>
    internal const string Flicker = "mmamammmmammamamaaamammma";

    [TestCase("", 0, false)]
    [TestCase("m", 1, false)]
    [TestCase("mm", 2, true)]
    [TestCase(Flicker, 25, true)]
    public void PatternLength_OfAPattern_IsItsLetterCountAndAnimatesPastOne(string pattern, int length, bool animates)
    {
        LightStyleValues values = new();
        values.Set(Style, pattern);

        values.PatternLength(Style).ShouldBe(length);
        values.Animates(Style).ShouldBe(animates);
    }

    [Test]
    public void StyledLights_AStyledLampInPvsAndReach_IsListed()
    {
        LevelLighting lighting = Map([Lamp(Style, cluster: 0), Lamp(0, cluster: 0), Lamp(Style, cluster: 1), Lamp(Style, cluster: 0, radius: 10f)]);

        lighting.StyledLights(0f, 0f, 100f).ShouldBe([0], "style 0 is baked, cluster 1 is out of PVS, a radius of 10 does not reach");
    }

    [TestCase(Flicker, 0, true)]
    [TestCase("m", 0, false)]
    [TestCase(Flicker, 1, false)]
    public void TakesFullLighting_ByPatternAndPvs_FollowsTheListedLightsStyle(string pattern, int cluster, bool full)
    {
        LightStyleValues values = new();
        values.Set(Style, pattern);

        LevelLighting lighting = Map([Lamp(Style, cluster)]);
        lighting.StyleAnimates = values.Animates;

        lighting.TakesFullLighting(1000, 0f, 0f, 100f).ShouldBe(full);
    }

    [Test]
    public void TakesFullLighting_AfterThePatternStops_KeepsTheListAndFollowsTheStyle()
    {
        LightStyleValues values = new();
        values.Set(Style, Flicker);

        LevelLighting lighting = Map([Lamp(Style, 0)]);
        lighting.StyleAnimates = values.Animates;

        lighting.TakesFullLighting(1000, 0f, 0f, 100f).ShouldBeTrue();

        values.Set(Style, "m");

        lighting.TakesFullLighting(1000, 0f, 0f, 100f).ShouldBeFalse();
    }

    /// <summary>A lamp 20 units above the prop's lighting origin.</summary>
    internal static BspWorldLight Lamp(int style, int cluster, float radius = 0f) =>
        LevelLightingTests.Lamp((0f, 0f, 120f), 400f) with { Style = style, Cluster = cluster, Radius = radius };

    /// <summary>Two leaves split at z = 0: leaf 2 above in cluster 0, leaf 1 below in cluster 1, each seeing only itself.</summary>
    internal static LevelLighting Map(BspWorldLight[] lights)
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -2 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        byte[] leaves = new byte[96];
        BinaryPrimitives.WriteInt32LittleEndian(leaves.AsSpan(0), 1);
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(4), -1);
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(32 + 4), 1);
        BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(64 + 4), 0);

        // numclusters, then (pvs, pas) offsets per cluster, then one literal row byte each.
        byte[] vis = new byte[22];
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(0), 2);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(4), 20);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(8), 20);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(12), 21);
        BinaryPrimitives.WriteInt32LittleEndian(vis.AsSpan(16), 21);
        vis[20] = 0b01;
        vis[21] = 0b10;

        AmbientSamples samples = new(
            [new AmbientSample(default, 0.5f, 0.5f, 0.5f)], (-512f, -512f, -512f, 512f, 512f, 512f));

        return new LevelLighting(
            BspLeafTree.FromLumps(node, plane, leaves),
            [samples, samples, samples],
            lights,
            null,
            new RecordingLogger(),
            visibility: BspVisibility.FromLump(vis));
    }
}
