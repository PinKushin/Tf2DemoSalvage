using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The world's glass is drawn after the opaque models, so a model behind it shows THROUGH it (B426).
/// </summary>
/// <remarks>
/// <c>CRendering3dView::DrawTranslucentRenderables</c> (<c>viewrender.cpp:4465</c>) draws the translucent world
/// after <c>DrawOpaqueRenderables</c>. When <c>WorldRenderer.Draw</c> drew it first, a glass pane does not write
/// depth, so a model behind it overwrote it and the pixel was the model's alone.
/// </remarks>
public sealed class TranslucentWorldOrderRenderTests
{
    [Test]
    public void DrawTranslucentWorld_AModelBehindWorldGlass_IsCoveredByTheGlass()
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

        if (TranslucentMaterial(assets) is not { } glassMaterial)
        {
            Assert.Ignore("no translucent material in this map");
            return;
        }

        (List<WorldVertex> wall, WorldBatch wallBatch) = Quad(0.9f, 0, 0, (0f, 0f, 1f));
        (List<WorldVertex> glass, WorldBatch glassBatch) = Quad(0.3f, glassMaterial, 6, (1f, 1f, 1f));
        (List<WorldVertex> model, WorldBatch modelBatch) = Quad(0.5f, 0, 0, (0f, 1f, 0f));

        List<WorldVertex> world = [.. wall, .. glass];

        // The instrument's control: the glass must change the wall it stands in front of, or it did not draw.
        target.Clear(0f, 0f, 0f);
        target.DrawWorld(wall, [wallBatch], Identity, assets);
        (int red, int green, int blue) wallOnly = target.PixelAt(32, 32);

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(world, [wallBatch, glassBatch], Identity, assets);
        (int red, int green, int blue) wallGlass = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"WALL {wallOnly}  WALL+GLASS {wallGlass}");
        wallGlass.ShouldNotBe(wallOnly, "the glass did not draw over the wall, so this test cannot fail");

        // The control: the model with no glass in the world at all.
        target.Clear(0f, 0f, 0f);
        target.DrawWorld(wall, [wallBatch], Identity, assets);
        target.DrawModelPose(model, [modelBatch], Identity, Identity, assets, bothSides: true, clearDepth: false);
        (int red, int green, int blue) bare = target.PixelAt(32, 32);

        // The engine's order: opaque world, opaque model, then the translucent world.
        target.Clear(0f, 0f, 0f);
        target.DrawWorld(world, [wallBatch, glassBatch], Identity, assets, translucent: false);
        target.DrawModelPose(model, [modelBatch], Identity, Identity, assets, bothSides: true, clearDepth: false);
        target.DrawTranslucentWorld();
        (int red, int green, int blue) covered = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"BARE {bare}  COVERED {covered}");

        (bare.red + bare.green + bare.blue).ShouldBeGreaterThan(30, "the model did not draw");

        int difference =
            Math.Abs(covered.red - bare.red) + Math.Abs(covered.green - bare.green) + Math.Abs(covered.blue - bare.blue);

        difference.ShouldBeGreaterThan(6, "the glass in front of the model left its pixel unchanged, so it drew before it");
    }

    private static MapAssets? Assets
    {
        get
        {
            string tf = GameInstall.Root ?? string.Empty;
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

    private static int? TranslucentMaterial(MapAssets assets)
    {
        for (int index = 0; index < assets.Textures.Count; index++)
        {
            if (assets.Textures[index] is not { IsTranslucent: true, IsDecal: false, Width: > 0 } texture)
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
                // Not a candidate: this reader cannot expand the format.
                continue;
            }

            // **Partly opaque at the centre texel, where the test samples** — a pane that is clear there
            // blends to the model's own pixel in either order, and the test could not fail.
            int centre = (((texture.Height / 2) * texture.Width) + (texture.Width / 2)) * 4;

            if (pixels.Length > centre + 3 && pixels[centre + 3] is >= 64 and <= 224)
            {
                return index;
            }
        }

        return null;
    }

    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(
        float depth,
        int material,
        int firstVertex,
        (float Red, float Green, float Blue) colour)
    {
        (float r, float g, float b) = colour;

        List<WorldVertex> vertices =
        [
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(1f, 1f, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
            new(1f, -1f, depth, 1f, 0f, 0f, 0f, 0f, r, g, b),
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(-1f, 1f, depth, 0f, 1f, 0f, 0f, 0f, r, g, b),
            new(1f, 1f, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
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
