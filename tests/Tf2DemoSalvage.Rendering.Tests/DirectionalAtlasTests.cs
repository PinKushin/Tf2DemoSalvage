using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Packing a bump-lit face's four lightmaps into the atlas.
/// </summary>
/// <remarks>
/// **The layout is Valve's, not ours.** `lightmappedgeneric_vs20.fxc` builds the three directional
/// coordinates by adding a per-vertex offset to the base one, once, twice and three times — so the
/// four sets sit adjacent in one page and the shader steps along them. Packing them as a strip
/// mirrors that, and means any bilinear bleed at the seams is behaviour the engine already ships
/// rather than something introduced here.
///
/// The step is what the shader needs and what these tests are mostly about: a strip packed
/// correctly with the wrong step reads three copies of set 0, which combines to exactly the flat
/// lighting we have today. That failure looks like "bump mapping does nothing", not like a bug.
/// </remarks>
public sealed class DirectionalAtlasTests
{
    [Test]
    public void PackAll_WithNoBumpedFaces_StepsNowhere()
    {
        // **Not compared against Pack, because Pack now delegates here** - that comparison would
        // be a tautology rather than a control. What matters is that an unbumped face reserves one
        // lightmap's width and asks the shader to step nowhere.
        LightmapAtlas atlas = LightmapAtlas.PackAll(
        [
            new BspFaceLighting(Lightmap(4, 3, 10), []),
            new BspFaceLighting(default, []),
            new BspFaceLighting(Lightmap(2, 2, 40), []),
        ]);

        atlas.DirectionalSteps.ShouldAllBe(step => step == 0f, "no face here is bump lit");
        atlas.Rectangles[0].Width.ShouldBe(3f / atlas.Width, 0.0000001);
        Red(atlas, atlas.Rectangles[2].U, atlas.Rectangles[2].V).ShouldBe((byte)40);
    }

    [Test]
    public void PackAll_ABumpedFace_StepsExactlyOneLightmapAlong()
    {
        // **The number the shader multiplies by one, two and three.** A step of zero reads set 0
        // three times; a step of one whole atlas width reads off the end. Both draw something.
        LightmapAtlas atlas = LightmapAtlas.PackAll(
        [
            new BspFaceLighting(
                Lightmap(4, 2, 10),
                [Lightmap(4, 2, 40), Lightmap(4, 2, 70), Lightmap(4, 2, 100)]),
        ]);

        atlas.DirectionalSteps[0].ShouldBe(4f / atlas.Width, 0.0000001);
        atlas.Rectangles[0].Width.ShouldBe(3f / atlas.Width, 0.0000001,
            "the rectangle covers set 0 alone, not the whole strip");
    }

    [Test]
    public void PackAll_ABumpedFace_PutsEachSetWhereItsStepPointsTo()
    {
        // **The measurement that a wrong step cannot pass.** Reading the pixel one step along must
        // find set 1's data, not set 0's again. The fixtures differ by construction - 10, 40, 70,
        // 100 - so a packer that wrote the flat set four times gives 10 every time.
        LightmapAtlas atlas = LightmapAtlas.PackAll(
        [
            new BspFaceLighting(
                Lightmap(4, 2, 10),
                [Lightmap(4, 2, 40), Lightmap(4, 2, 70), Lightmap(4, 2, 100)]),
        ]);

        AtlasRect rect = atlas.Rectangles[0];
        float step = atlas.DirectionalSteps[0];

        // The top-left texel of each set, read back out of the packed image.
        byte[] found =
        [
            .. Enumerable.Range(0, 4).Select(set => Red(atlas, rect.U + (step * set), rect.V)),
        ];

        found.ShouldBe(new byte[] { 10, 40, 70, 100 });
    }

    [Test]
    public void PackAll_ABumpedFaceAndAnUnbumpedOne_DoNotOverlap()
    {
        // The bystander. A strip is four times as wide as the packer used to expect, so the face
        // packed after it is exactly what a stale width would land on top of.
        LightmapAtlas atlas = LightmapAtlas.PackAll(
        [
            new BspFaceLighting(
                Lightmap(4, 2, 10),
                [Lightmap(4, 2, 40), Lightmap(4, 2, 70), Lightmap(4, 2, 100)]),
            new BspFaceLighting(Lightmap(4, 2, 200), []),
        ]);

        Red(atlas, atlas.Rectangles[1].U, atlas.Rectangles[1].V).ShouldBe(
            (byte)200, "the second face must not be overwritten by the first face's strip");

        Red(atlas, atlas.Rectangles[0].U, atlas.Rectangles[0].V).ShouldBe(
            (byte)10, "nor the first by the second");
    }

    /// <summary>A lightmap whose every texel carries one known value.</summary>
    private static BspLightmap Lightmap(int width, int height, byte value)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int at = 0; at < pixels.Length; at += 4)
        {
            pixels[at] = value;
            pixels[at + 1] = value;
            pixels[at + 2] = value;
            pixels[at + 3] = 255;
        }

        return new BspLightmap(width, height, pixels);
    }

    /// <summary>The red channel at a texture coordinate.</summary>
    private static byte Red(LightmapAtlas atlas, float u, float v)
    {
        // Truncated, not rounded: a rectangle starts half a texel in, so truncation lands on the
        // texel it names and rounding lands on whichever neighbour is nearer.
        int x = (int)(u * atlas.Width);
        int y = (int)(v * atlas.Height);

        return atlas.Pixels[(((y * atlas.Width) + x) * 4) + 0];
    }
}
