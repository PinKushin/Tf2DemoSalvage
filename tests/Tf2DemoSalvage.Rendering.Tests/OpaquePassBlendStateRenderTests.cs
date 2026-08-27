using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// An opaque pass must establish that it is opaque, not inherit it from the pass before.
/// </summary>
/// <remarks>
/// **Written after the defect, because nothing existed that could fail on it.** `DrawDecals` turned
/// alpha blending on and never turned it off; the next reset lived two passes later inside
/// `DrawTranslucent`. Under the old pass order — world, props, decals — nothing ran in that gap. Then
/// `e7b95cf` moved static props to draw AFTER the overlays, correctly, matching
/// `CBaseWorldView::DrawExecute` (`game/client/viewrender.cpp:5487`), and from that commit every
/// static prop in every map was alpha-blended against whatever its base texture's alpha channel
/// happened to hold.
///
/// **What made it survive two days of hunting is which channel that is.** In a TF2 model material
/// the base alpha is usually an ENVMAP MASK — `$basealphaenvmapmask` — not opacity. Shiny metal
/// masks to near zero and dull surfaces to near one, so the same bug rendered pipes as glass tubes,
/// an observatory dome as a soap bubble, a sign with the wall showing through it, and a silo's
/// collar not at all, while every wooden crate and concrete wall looked perfect. It reads as four
/// unrelated art faults.
///
/// **Why the existing suites could not catch it.** Every render test in this assembly draws a wall,
/// a marking and an occluder built by hand, all with an alpha of one — and blending against an
/// alpha of one is indistinguishable from not blending. The condition and the correct behaviour
/// predict the same pixel, which is this project's first named way for a test to be unable to fail.
/// So the fixture here is chosen from the MAP, by looking for a material that actually carries a
/// masking alpha, and the test skips loudly rather than passing when it cannot find one.
/// </remarks>
public sealed class OpaquePassBlendStateRenderTests
{
    /// <summary>The map, for real materials with real alpha channels.</summary>
    private static MapAssets? Assets
    {
        get
        {
            string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
            string map = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            if (!System.IO.Directory.Exists(tf) || !System.IO.File.Exists(map))
            {
                return null;
            }

            return MapAssets.Load(
                System.IO.File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);
        }
    }

    [Test]
    public void Draw_AnOpaquePropAfterADecalPass_IgnoresItsTexturesAlphaChannel()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // **The condition, and the test is worthless without it.** An opaque material whose base
        // texture carries a LOW alpha: blending it produces a different pixel from not blending it,
        // and a material with an opaque alpha channel produces the same pixel either way.
        if (MaskedButOpaque(assets) is not { } masked)
        {
            Assert.Ignore(
                "no opaque material in this map carries a masking alpha, so a blend leak would " +
                "be invisible here and this test could not fail");

            return;
        }

        // A blue wall at the back, a marking on it, and a prop standing in front of both. The wall
        // is the thing that shows through if the prop blends, so its colour is the measurement.
        (List<WorldVertex> wall, WorldBatch wallBatch) =
            Quad(0.9f, material: 0, firstVertex: 0, colour: (0f, 0f, 1f));

        (List<WorldVertex> mark, WorldBatch markBatch) =
            Quad(0.9f, material: Decal(assets), firstVertex: 6, colour: (1f, 0f, 0f));

        (List<WorldVertex> prop, WorldBatch propBatch) =
            Quad(0.5f, material: masked, firstVertex: 12, colour: (0f, 1f, 0f));

        List<WorldVertex> all = [.. wall, .. mark, .. prop];

        target.Clear(0f, 0f, 0f);

        // **The decal list is what arms this.** Passing none skips DrawDecals entirely, the blend
        // state is never turned on, and the prop draws correctly for the wrong reason.
        target.DrawWorld(
            all, [wallBatch], Identity, assets, decals: [markBatch], props: [propBatch]);

        (int red, int green, int blue) = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"PROP PIXEL {red},{green},{blue}");

        // **The wall must not be visible through an opaque prop.** With the leak the fragment is
        // `alpha * prop + (1 - alpha) * wall`, and the wall is pure blue, so blue arriving at the
        // centre pixel is the wall showing through. Without it the blue channel is whatever the
        // prop's own texture has, which is not a full-strength wall.
        blue.ShouldBeLessThan(
            red + green + 40,
            "the wall is showing through an opaque prop, so a pass left alpha blending on");

        // The control: the prop has to have drawn at all, or "no wall visible" would be satisfied
        // by a prop that never appeared — which is exactly the picture the defect produced.
        (red + green + blue).ShouldBeGreaterThan(
            30, "the prop did not draw, so the assertion above measured an empty pixel");
    }

    /// <summary>
    /// An opaque material whose base texture carries a masking alpha, or null if the map has none.
    /// </summary>
    /// <remarks>
    /// **Neither translucent nor alpha-tested**, because those two are SUPPOSED to read their alpha
    /// channel — only a material that declares itself opaque and still carries a low alpha can tell
    /// a blend leak from correct behaviour.
    /// </remarks>
    private static int? MaskedButOpaque(MapAssets assets)
    {
        for (int index = 0; index < assets.Textures.Count; index++)
        {
            if (assets.Textures[index] is not { } texture ||
                texture.IsTranslucent ||
                texture.IsTransparent ||
                texture.Width <= 0)
            {
                continue;
            }

            byte[] pixels;

            try
            {
                pixels = texture.Image.ToRgba(texture.Width, texture.Height);
            }
            catch (NotSupportedException)
            {
                // A format this reader cannot expand is not a finding here; it is simply not a
                // candidate. Swallowing anything wider would hide a real decode fault.
                continue;
            }

            if (pixels.Length < 4)
            {
                continue;
            }

            // Sampled across the image rather than at one texel: a masking alpha varies, and the
            // corner of a texture is as likely to be opaque as not.
            int transparent = 0;
            int sampled = 0;

            for (int at = 3; at < pixels.Length; at += 4 * 97)
            {
                sampled++;

                if (pixels[at] < 96)
                {
                    transparent++;
                }
            }

            if (sampled > 0 && transparent * 2 > sampled)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>A material the map marks as a decal, so the overlay pass has something to draw.</summary>
    private static int Decal(MapAssets assets)
    {
        for (int index = 0; index < assets.Textures.Count; index++)
        {
            if (assets.Textures[index] is { IsDecal: true })
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>A full-view quad at one depth, carrying a vertex colour that names it.</summary>
    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(
        float depth,
        int material,
        int firstVertex,
        (float Red, float Green, float Blue) colour,
        float half = 1f)
    {
        (float r, float g, float b) = colour;

        // Wound front-facing: the overlay pass culls back faces, so the obvious anticlockwise
        // order makes the marking vanish and the test fails on its control rather than its claim.
        List<WorldVertex> vertices =
        [
            new(-half, -half, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(half, half, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
            new(half, -half, depth, 1f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, -half, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, half, depth, 0f, 1f, 0f, 0f, 0f, r, g, b),
            new(half, half, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
        ];

        return (vertices, new WorldBatch(material, firstVertex, vertices.Count));
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
