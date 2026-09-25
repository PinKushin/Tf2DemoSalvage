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

        BspLightmaps.Compose(Styled().Styles, 0, static style => style == 32 ? 0f : 1f, expected);

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

    /// <summary>One unbumped luxel lit by style 0 (red 64) and style 32 (red 40).</summary>
    private static BspFaceLighting Styled() =>
        new(new BspLightmap(1, 1, new byte[] { 1, 2, 3, 255 }), [])
        {
            Styles = [new BspStyleLayer(0, [[64f, 0f, 0f]]), new BspStyleLayer(32, [[40f, 0f, 0f]])],
        };
}
