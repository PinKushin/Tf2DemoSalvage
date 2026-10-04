using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The render mode's depth state, read off the pixels with a real map's real glow (B391).
/// </summary>
/// <remarks>
/// **The output-level half of `EntitySpriteBatchesTests`.** Those prove each batch CARRIES the mode's
/// depth state; this proves the sprite pass obeys it, through the same renderer the viewer uses, with
/// `light_glow03` loaded from `cp_process_f12` as the viewer loads it. A batch that carried the state
/// and a renderer that ignored it would pass the unit tests and fail here.
///
/// **The camera is the identity**, so positions are clip space and z is depth. The glow is scaled in
/// world space to one unit across — ±0.5 about the centre, so it covers the middle half of the view —
/// at depth 0.5, with an eye at the origin, half a unit away, where `GlowBlend`'s fade is one.
/// </remarks>
public sealed class EntitySpriteDepthRenderTests
{
    private const string Glow = "materials/sprites/light_glow03.vmt";

    /// <remarks>
    /// **A world glow draws over a wall in front of it** — <c>EnableDepthTest( false )</c>
    /// (`sprite_dx9.cpp:265`). The occluder at depth 0.3 hides the glow's centre from a depth-tested
    /// pass; here the glow's light must add to it. The control below is the same picture at
    /// `kRenderTransAdd`, the same blend with the test on.
    /// </remarks>
    [Test]
    public void Render_AWorldGlowBehindAnOccluder_AddsOverIt()
    {
        if (Setup() is not var (target, assets))
        {
            return;
        }

        using (target)
        {
            DrawOccluderThenGlow(target, assets, RenderModes.WorldGlow);

            target.PixelAt(32, 32).Red.ShouldBeGreaterThan(
                64, "a glow is not depth-tested, so its centre adds over the green occluder");
        }
    }

    /// <remarks>
    /// **The control.** `kRenderTransAdd` is additive too, but depth-tested (`sprite_dx9.cpp:341-343`),
    /// so the occluder keeps the centre green — and just outside the occluder the same sprite does
    /// draw, which separates "hidden by the test" from "never drew at all".
    /// </remarks>
    [Test]
    public void Render_ATransAddSpriteBehindAnOccluder_IsHiddenByIt()
    {
        if (Setup() is not var (target, assets))
        {
            return;
        }

        using (target)
        {
            DrawOccluderThenGlow(target, assets, RenderModes.TransAdd);

            // `light_glow03` falls off steeply: measured along the middle row, red is 29 one pixel
            // left of the occluder's edge and 6 four pixels further out, so the control reads there.
            target.PixelAt(32, 32).Red.ShouldBeLessThan(4, "the occluder hides a depth-tested sprite");
            target.PixelAt(28, 32).Red.ShouldBeGreaterThan(16, "outside the occluder the sprite draws");
        }
    }

    /// <remarks>
    /// **`kRenderNormal` writes depth** (`sprite_dx9.cpp:229-241`, no <c>EnableDepthWrites( false )</c>),
    /// so a green quad drawn after it and BEHIND it is hidden. The control is `kRenderTransColor`,
    /// which does not write: there the quad behind shows through where the sprite is transparent and
    /// wins the depth test everywhere, so the centre is green.
    /// </remarks>
    [TestCase(RenderModes.Normal, false)]
    [TestCase(RenderModes.TransColor, true)]
    public void Render_ASpriteThenAQuadBehindIt_TheQuadShowsOnlyWhereNothingWroteDepth(
        int renderMode, bool quadShows)
    {
        if (Setup() is not var (target, assets))
        {
            return;
        }

        using (target)
        {
            // A far blue wall first: `DrawWorld` is what binds the target and clears its depth.
            (List<WorldVertex> wall, WorldBatch wallBatch) = Quad(0.9f, (0f, 0f, 1f), half: 1f);

            target.Clear(0f, 0f, 0f);
            target.DrawWorld(wall, [wallBatch], Identity, assets, surfaceColours: true, translucent: false);
            DrawSprite(target, assets, renderMode, depth: 0.3f);

            (List<WorldVertex> quad, WorldBatch batch) = Quad(0.5f, (0f, 1f, 0f), half: 0.25f);

            target.DrawModelPose(
                quad, [batch], Identity, Identity, assets, bothSides: true, surfaceColours: true,
                clearDepth: false);

            (int red, int green, int _) = target.PixelAt(32, 32);

            (green > 128 && red < 64).ShouldBe(
                quadShows, $"centre pixel ({red}, {green}) at render mode {renderMode}");
        }
    }

    private static (OffscreenTarget Target, MapAssets Assets)? Setup()
    {
        OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return null;
        }

        if (Assets is not { } assets)
        {
            target.Dispose();
            Assert.Ignore("the map or the game is not installed");
            return null;
        }

        if (!assets.SpriteMaterials.ContainsKey(Glow))
        {
            target.Dispose();
            throw new InvalidOperationException(
                "light_glow03 ships with TF2; without it this test measures nothing");
        }

        return (target, assets);
    }

    private static void DrawOccluderThenGlow(OffscreenTarget target, MapAssets assets, int renderMode)
    {
        (List<WorldVertex> occluder, WorldBatch batch) = Quad(0.3f, (0f, 1f, 0f), half: 0.1f);

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(occluder, [batch], Identity, assets, surfaceColours: true, translucent: false);

        DrawSprite(target, assets, renderMode, depth: 0.5f);
    }

    /// <summary>One `light_glow03` at the centre, built by the production pass and drawn by the real renderer.</summary>
    private static void DrawSprite(OffscreenTarget target, MapAssets assets, int renderMode, float depth)
    {
        SceneProp prop = new(
            EntityIndex: 1,
            ModelPath: Glow,
            Kind: SceneModelKind.Sprite,
            Pose: new ScenePose
            {
                Z = depth,
                RenderMode = renderMode,
                RenderColor = ((byte)255, (byte)255, (byte)255),
                Sprite = SceneSprite.Default with { Scale = 1f, ScaleIsWorldSpace = true },
            });

        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = sprites.Build(
            [prop],
            eye: Vector3.Zero,
            viewRight: Vector3.UnitX,
            viewUp: Vector3.UnitY,
            viewForward: Vector3.UnitZ,
            sprites: assets.SpriteMaterials,
            visible: _ => true);

        sprites.Drawn.ShouldBe(1, "the glow must be built for the picture to mean anything");

        foreach (ParticleBatch batch in batches)
        {
            target.DrawSprites(batch, Identity);
        }
    }

    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(
        float depth, (float Red, float Green, float Blue) colour, float half)
    {
        (float r, float g, float b) = colour;

        List<WorldVertex> vertices =
        [
            new(-half, -half, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(half, half, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
            new(half, -half, depth, 1f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, -half, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, half, depth, 0f, 1f, 0f, 0f, 0f, r, g, b),
            new(half, half, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
        ];

        return (vertices, new WorldBatch(0, 0, vertices.Count));
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

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
                System.IO.File.ReadAllBytes(map),
                GameArchives.Open(tf),
                maximumTextureSize: 256,
                spriteMaterials: [Glow]);
        }
    }
}
