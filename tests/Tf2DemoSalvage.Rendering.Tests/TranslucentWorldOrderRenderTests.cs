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

    /// <remarks>
    /// **The per-leaf interleave, drawn** (B426): glass in leaf position 0, the nearest; a TRANSLUCENT model in
    /// position 1 behind it, or in position 0 with it. Neither writes depth, so only the order decides the pixel.
    /// The engine draws a farther leaf's entity before the nearer leaf's glass, and a same-leaf entity after its
    /// leaf's glass (<c>viewrender.cpp:4583</c>-<c>4635</c>). Each case is predicted exactly by drawing its order
    /// by hand, and the two hand orders must differ or the test cannot fail.
    /// </remarks>
    [Test]
    public void DrawTranslucentLeaf_ATranslucentModelBehindWorldGlass_IsCoveredAndOneInFrontIsNot()
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

        if (TranslucentMaterials(assets) is not [int glassMaterial, int modelMaterial, ..])
        {
            Assert.Ignore("fewer than two translucent materials in this map");
            return;
        }

        (List<WorldVertex> wall, WorldBatch wallBatch) = Quad(0.9f, 0, 0, (0f, 0f, 1f));
        (List<WorldVertex> glass, WorldBatch glassBatch) = Quad(0.3f, glassMaterial, 6, (1f, 1f, 1f));
        (List<WorldVertex> model, WorldBatch modelBatch) = Quad(0.5f, modelMaterial, 0, (0f, 1f, 0f));

        List<WorldVertex> world = [.. wall, .. glass];

        // Glass at position 0 (nearest); position 1 holds no translucent surface.
        TranslucentLeafRuns runs = new([glassBatch], [0, 1, 1]);

        (int red, int green, int blue) Draw(Action translucents)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawWorld(world, [wallBatch, glassBatch], Identity, assets, translucent: false);
            target.SetTranslucentLeaves(runs);
            translucents();
            return target.PixelAt(32, 32);
        }

        void Model() =>
            target.DrawModelPose(model, [modelBatch], Identity, Identity, assets, bothSides: true, clearDepth: false);

        (int red, int green, int blue) Planned(int modelLeaf) => Draw(() =>
        {
            List<InterleaveStep> steps = [];

            TranslucentInterleave.Plan(runs.LeafCount, [modelLeaf], steps);

            foreach (InterleaveStep step in steps)
            {
                if (step.IsEntity)
                {
                    Model();
                }
                else
                {
                    target.DrawTranslucentLeaf(step.Index);
                }
            }
        });

        (int red, int green, int blue) glassOver = Draw(() => { Model(); target.DrawTranslucentLeaf(0); });
        (int red, int green, int blue) modelOver = Draw(() => { target.DrawTranslucentLeaf(0); Model(); });

        TestContext.Out.WriteLine($"GLASS OVER {glassOver}  MODEL OVER {modelOver}");
        Distance(glassOver, modelOver).ShouldBeGreaterThan(6, "the two orders draw the same pixel, so this test cannot fail");

        (int red, int green, int blue) behind = Planned(1);
        (int red, int green, int blue) inFront = Planned(0);

        TestContext.Out.WriteLine($"BEHIND {behind}  IN FRONT {inFront}");
        Distance(behind, glassOver).ShouldBeLessThanOrEqualTo(3, "a model in a farther leaf drew over the nearer glass");
        Distance(inFront, modelOver).ShouldBeLessThanOrEqualTo(3, "a model in the glass's own leaf drew under it");
    }

    /// <remarks>
    /// **Detail sprites in the translucent pass, drawn** (B434). An opaque model in front of a sprite hides it by
    /// depth, since the sprite now draws after it and tests against its depth. A TRANSLUCENT model in the same leaf
    /// is split around: a sprite farther than it draws before it (<c>viewrender.cpp:4607</c>), a nearer one after
    /// it (<c>:4639</c>) — the second is what drawing the grass before the models got wrong. Each case is predicted
    /// by its hand order, and the two hand orders must differ or the test cannot fail.
    /// </remarks>
    [Test]
    public void DetailSprites_InTheTranslucentPass_AreHiddenByAnOpaqueModelAndSplitAroundATranslucentOne()
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

        if (TranslucentMaterials(assets) is not [int sheetMaterial, int modelMaterial, ..])
        {
            Assert.Ignore("fewer than two translucent materials in this map");
            return;
        }

        (List<WorldVertex> wall, WorldBatch wallBatch) = Quad(0.9f, 0, 0, (0f, 0f, 1f));
        (List<WorldVertex> solid, WorldBatch solidBatch) = Quad(0.5f, 0, 0, (0f, 1f, 0f));
        (List<WorldVertex> model, WorldBatch modelBatch) = Quad(0.5f, modelMaterial, 0, (0f, 1f, 0f));
        MapTexture sheet = assets.Textures[sheetMaterial] ?? throw new InvalidOperationException("the chosen sheet has no texture");
        const float EntitySquared = 0.5f * 0.5f;

        // One leaf; its runs are empty, so only sprites and the model decide the pixel.
        TranslucentLeafRuns runs = new([], [0, 0]);

        (int red, int green, int blue) Draw(Action opaque, Action translucents)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawWorld(wall, [wallBatch], Identity, assets, translucent: false);
            opaque();
            target.SetTranslucentLeaves(runs);
            translucents();
            return target.PixelAt(32, 32);
        }

        void Nothing()
        {
            // No draw: the pass under test is empty in this case.
        }

        void Solid() =>
            target.DrawModelPose(solid, [solidBatch], Identity, Identity, assets, bothSides: true, clearDepth: false);

        void Model() =>
            target.DrawModelPose(model, [modelBatch], Identity, Identity, assets, bothSides: true, clearDepth: false);

        void Sprite(float depth) => target.DrawDetailSprites(SpriteQuad(depth), sheet, Identity, 0, 1);

        // Runs the planner's steps for one sprite at one depth, the way Device3D does.
        void Planned(float depth, bool entity)
        {
            List<DetailSpriteVertex> corners = SpriteQuad(depth);
            DetailSpriteLeaves leaves = new();
            leaves.Add(0, depth * depth);
            leaves.Begin();

            List<InterleaveStep> steps = [];
            TranslucentInterleave.Plan(runs.LeafCount, entity ? [0] : [], leaves.Has, steps);

            foreach (InterleaveStep step in steps)
            {
                switch (step.Kind)
                {
                    case InterleaveKind.Entity:
                        Model();
                        break;
                    case InterleaveKind.World:
                        target.DrawTranslucentLeaf(step.Index);
                        break;
                    default:
                        (int first, int count) = leaves.Take(
                            step.Kind == InterleaveKind.Detail ? step.Index : 0,
                            step.Kind == InterleaveKind.Detail ? null : EntitySquared);
                        target.DrawDetailSprites(corners, sheet, Identity, first, count);
                        break;
                }
            }
        }

        // An opaque model in front of the sprite: the control is the sprite alone, which must change the wall.
        (int red, int green, int blue) wallOnly = Draw(Nothing, Nothing);
        (int red, int green, int blue) spriteOnly = Draw(Nothing, () => Planned(0.7f, entity: false));
        (int red, int green, int blue) solidOnly = Draw(Solid, Nothing);
        (int red, int green, int blue) solidOverSprite = Draw(Solid, () => Planned(0.7f, entity: false));

        TestContext.Out.WriteLine($"WALL {wallOnly}  SPRITE {spriteOnly}  SOLID {solidOnly}  SOLID+SPRITE {solidOverSprite}");
        Distance(spriteOnly, wallOnly).ShouldBeGreaterThan(6, "the sprite did not draw, so this test cannot fail");
        Distance(solidOverSprite, solidOnly).ShouldBeLessThanOrEqualTo(3, "a sprite behind an opaque model showed over it");

        // A translucent model: the two hand orders, which must differ.
        (int red, int green, int blue) spriteFirst = Draw(Nothing, () => { Sprite(0.7f); Model(); });
        (int red, int green, int blue) modelFirst = Draw(Nothing, () => { Model(); Sprite(0.3f); });
        (int red, int green, int blue) spriteFirstNear = Draw(Nothing, () => { Sprite(0.3f); Model(); });

        TestContext.Out.WriteLine($"SPRITE FIRST {spriteFirst}  MODEL FIRST {modelFirst}  SPRITE(NEAR) FIRST {spriteFirstNear}");
        Distance(modelFirst, spriteFirstNear).ShouldBeGreaterThan(6, "the two orders draw the same pixel, so this test cannot fail");

        (int red, int green, int blue) behind = Draw(Nothing, () => Planned(0.7f, entity: true));
        (int red, int green, int blue) inFront = Draw(Nothing, () => Planned(0.3f, entity: true));

        TestContext.Out.WriteLine($"BEHIND {behind}  IN FRONT {inFront}");
        Distance(behind, spriteFirst).ShouldBeLessThanOrEqualTo(3, "a sprite behind a translucent model drew after it");
        Distance(inFront, modelFirst).ShouldBeLessThanOrEqualTo(3, "a sprite in front of a translucent model drew under it");
    }

    /// <summary>One full-screen detail sprite at a depth, red, sampling the sheet's centre.</summary>
    private static List<DetailSpriteVertex> SpriteQuad(float depth)
    {
        DetailSpriteVertex Corner(float x, float y) => new(x, y, depth, 0.5f, 0.5f, 1f, 0f, 0f, 1f, 0.5f, 0.5f, 0f);

        return [Corner(-1f, -1f), Corner(1f, 1f), Corner(1f, -1f), Corner(-1f, -1f), Corner(-1f, 1f), Corner(1f, 1f)];
    }

    private static int Distance((int red, int green, int blue) a, (int red, int green, int blue) b) =>
        Math.Abs(a.red - b.red) + Math.Abs(a.green - b.green) + Math.Abs(a.blue - b.blue);

    private static List<int> TranslucentMaterials(MapAssets assets)
    {
        List<int> found = [];

        for (int index = 0; index < assets.Textures.Count && found.Count < 2; index++)
        {
            if (IsPartlyOpaqueAtCentre(assets, index))
            {
                found.Add(index);
            }
        }

        return found;
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

    private static int? TranslucentMaterial(MapAssets assets) =>
        TranslucentMaterials(assets) is [int first, ..] ? first : null;

    private static bool IsPartlyOpaqueAtCentre(MapAssets assets, int index)
    {
        if (assets.Textures[index] is not { IsTranslucent: true, IsDecal: false, Width: > 0 } texture)
        {
            return false;
        }

        byte[] pixels;

        try
        {
            pixels = texture.Image.ToRgba(texture.Width, texture.Height);
        }
        catch (NotSupportedException)
        {
            // Not a candidate: this reader cannot expand the format.
            return false;
        }

        // **Partly opaque at the centre texel, where the test samples** — a pane that is clear there
        // blends to the model's own pixel in either order, and the test could not fail.
        int centre = (((texture.Height / 2) * texture.Width) + (texture.Width / 2)) * 4;

        return pixels.Length > centre + 3 && pixels[centre + 3] is >= 64 and <= 224;
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
