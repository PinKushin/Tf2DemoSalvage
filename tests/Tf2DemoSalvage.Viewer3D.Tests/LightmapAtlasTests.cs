using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Packing a map's baked lighting into one texture.
/// </summary>
/// <remarks>
/// The atlas exists so the whole map's lighting is one bind rather than one per face — a map has
/// thirteen thousand of them. What the tests check is that each face still gets back the light that
/// belongs to it, since a packer that loses track of a rectangle produces a map that is lit, with
/// the wrong light on some surfaces.
/// </remarks>
public sealed class LightmapAtlasTests
{
    [Test]
    public void Pack_NoLightmaps_MakesAnEmptyAtlas()
    {
        LightmapAtlas atlas = LightmapAtlas.Pack([]);

        atlas.Width.ShouldBeGreaterThan(0);
        atlas.Rectangles.ShouldBeEmpty();
    }

    [Test]
    public void Pack_AnUnlitFace_GetsAnEmptyRectangle()
    {
        // lightofs of -1 is a real state, not a failure: that face has no baked light and the
        // renderer draws it unlit.
        LightmapAtlas atlas = LightmapAtlas.Pack([default, Lightmap(4, 4, 200)]);

        atlas.Rectangles[0].Width.ShouldBe(0f);
        atlas.Rectangles[1].Width.ShouldBeGreaterThan(0f);
    }

    [Test]
    public void Pack_EachFacesPixelsLandUnderItsOwnRectangle()
    {
        // **The measurement that matters.** Two faces with distinct constant colours: sampling the
        // atlas at the centre of each rectangle must return that face's colour. A packer that
        // mixed up its rectangles would still produce a full atlas and a lit map.
        LightmapAtlas atlas = LightmapAtlas.Pack([Lightmap(8, 8, 40), Lightmap(8, 8, 200)]);

        Sample(atlas, atlas.Rectangles[0]).ShouldBe((byte)40);
        Sample(atlas, atlas.Rectangles[1]).ShouldBe((byte)200);
    }

    [Test]
    public void Pack_MoreThanFitsAcross_StartsANewRow()
    {
        // The shelf packer's whole job. Three 8-wide lightmaps into a 20-wide atlas must wrap, and
        // each must still return its own colour afterwards.
        LightmapAtlas atlas = LightmapAtlas.Pack(
            [Lightmap(8, 8, 10), Lightmap(8, 8, 120), Lightmap(8, 8, 240)],
            maximumWidth: 20);

        atlas.Height.ShouldBeGreaterThan(9, "the third lightmap should have started a second row");

        Sample(atlas, atlas.Rectangles[0]).ShouldBe((byte)10);
        Sample(atlas, atlas.Rectangles[1]).ShouldBe((byte)120);
        Sample(atlas, atlas.Rectangles[2]).ShouldBe((byte)240);
    }

    [Test]
    public void Pack_RectanglesAreInsetFromTheTexelEdges()
    {
        // A lightmap sample sits at its texel's centre, so a rectangle running to the exact edge
        // lets bilinear filtering reach the neighbouring face - a bright seam along every edge in
        // the map. The inset is half a texel, so the rectangle never starts at zero.
        LightmapAtlas atlas = LightmapAtlas.Pack([Lightmap(8, 8, 100)]);
        AtlasRect rectangle = atlas.Rectangles[0];

        rectangle.U.ShouldBeGreaterThan(0f);
        rectangle.V.ShouldBeGreaterThan(0f);
        (rectangle.U + rectangle.Width).ShouldBeLessThan(1f);
        (rectangle.V + rectangle.Height).ShouldBeLessThan(1f);
    }

    [Test]
    public void Pack_LightmapsOfDifferentSizes_AllFitInside()
    {
        // Real maps mix sizes freely. Every rectangle must stay inside the atlas: one that ran off
        // the edge would sample whatever happened to be there.
        LightmapAtlas atlas = LightmapAtlas.Pack(
            [Lightmap(2, 2, 10), Lightmap(16, 16, 20), Lightmap(4, 32, 30), Lightmap(32, 4, 40)]);

        foreach (AtlasRect rectangle in atlas.Rectangles)
        {
            (rectangle.U + rectangle.Width).ShouldBeLessThanOrEqualTo(1f);
            (rectangle.V + rectangle.Height).ShouldBeLessThanOrEqualTo(1f);
        }
    }

    /// <summary>A lightmap of one flat colour, so its identity survives packing.</summary>
    private static BspLightmap Lightmap(int width, int height, byte value)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int index = 0; index < width * height; index++)
        {
            pixels[(index * 4) + 0] = value;
            pixels[(index * 4) + 1] = value;
            pixels[(index * 4) + 2] = value;
            pixels[(index * 4) + 3] = 255;
        }

        return new BspLightmap(width, height, pixels);
    }

    /// <summary>Reads the atlas at the centre of a rectangle, as the shader would.</summary>
    private static byte Sample(LightmapAtlas atlas, AtlasRect rectangle)
    {
        int x = (int)((rectangle.U + (rectangle.Width / 2f)) * atlas.Width);
        int y = (int)((rectangle.V + (rectangle.Height / 2f)) * atlas.Height);

        return atlas.Pixels[(((y * atlas.Width) + x) * 4) + 0];
    }
}
