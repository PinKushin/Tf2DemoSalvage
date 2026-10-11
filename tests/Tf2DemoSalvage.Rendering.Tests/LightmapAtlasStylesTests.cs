using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A styled face's region of the atlas, rebuilt when one of its styles changes — `R_BuildLightMap`'s job.</summary>
public sealed class LightmapAtlasStylesTests
{
    [Test]
    public void Recompose_AStyleThatChanged_RewritesTheFacesTexelsAndReportsItsRectangle()
    {
        LightmapAtlas atlas = LightmapAtlas.PackAll([Styled()]);
        List<AtlasRegion> dirty = [];

        atlas.Recompose(static style => style == 32 ? 0f : 1f, [32], dirty);

        byte[] expected = new byte[4];

        BspLightmaps.Compose(Styled().Styles, static style => style == 32 ? 0f : 1f, [], expected);

        dirty.Count.ShouldBe(1);
        (int x, int y, int _, int _) = dirty[0];
        atlas.Pixels.AsSpan(((y * atlas.Width) + x) * 4, 4).ToArray().ShouldBe(expected);
    }

    [Test]
    public void Recompose_AStyleNoFaceUses_ReportsNothing()
    {
        LightmapAtlas atlas = LightmapAtlas.PackAll([Styled()]);
        List<AtlasRegion> dirty = [];

        atlas.Recompose(static _ => 1f, [5], dirty);

        dirty.ShouldBeEmpty();
    }

    /// <remarks>
    /// A bump-lit luxel, flat 120 over directional 60 / 90 / 150, with a dynamic light adding 30 to the flat set and to
    /// the first directional one. `R_BuildLightMap` adds it before the page writer normalizes
    /// (`materialsystem.dll FUN_1800249c0`, *read in disassembly*): flat 150, directional 90 / 90 / 150 scaled by
    /// 150 / 110 — 122.73 / 122.73 / 204.55 — where adding it to the stored sets would give 102 / 108 / 180.
    /// </remarks>
    [Test]
    public void Rebuild_ABumpLitFaceWithADynamicLight_NormalizesTheDirectionalSetsAfterAddingIt()
    {
        byte[] luxel = new byte[4];
        BspFaceLighting face = new(new BspLightmap(1, 1, luxel), [new BspLightmap(1, 1, luxel), new BspLightmap(1, 1, luxel), new BspLightmap(1, 1, luxel)])
        {
            Styles = [new BspStyleLayer(0, [[120f, 0f, 0f], [60f, 0f, 0f], [90f, 0f, 0f], [150f, 0f, 0f]])],
        };
        LightmapAtlas atlas = LightmapAtlas.PackAll([face]);
        List<AtlasRegion> dirty = [];

        atlas.Rebuild(0, static _ => 1f, [30f, 0f, 0f, 30f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f], dirty);

        dirty.Count.ShouldBe(1);
        (int x, int y, int width, int _) = dirty[0];
        width.ShouldBe(4);
        Red(atlas, x, y).ShouldBe(150f, 1.5f);
        Red(atlas, x + 1, y).ShouldBe(122.73f, 1f);
        Red(atlas, x + 2, y).ShouldBe(122.73f, 1f);
        Red(atlas, x + 3, y).ShouldBe(204.55f, 2f);
    }

    private static float Red(LightmapAtlas atlas, int x, int y) =>
        BspLightmaps.Load(atlas.Pixels.AsSpan(((y * atlas.Width) + x) * 4, 4)).Red;

    /// <summary>One unbumped luxel lit by style 0 (red 64) and style 32 (red 40).</summary>
    private static BspFaceLighting Styled() =>
        new(new BspLightmap(1, 1, new byte[] { 1, 2, 3, 255 }), [])
        {
            Styles = [new BspStyleLayer(0, [[64f, 0f, 0f]]), new BspStyleLayer(32, [[40f, 0f, 0f]])],
        };
}
