using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A whole-model material override drawn on a real device and measured in pixels (B325).
/// </summary>
/// <remarks>
/// **The last hop, and the only instrument that can see it.** Everything before this asserts that a
/// VMT path travels from a decoded flag to `DrawModel`'s argument list; none of it can say whether
/// the draw then binds anything different. The failure is silent by construction — a path looked up
/// and missed leaves the model's own material, which is the correct fallback and also exactly what
/// an override wired to nothing looks like.
///
/// **The manipulation is the override and nothing else.** Same quad, same camera, same light, same
/// base material, drawn twice; the only difference is the string. That is what makes a pixel
/// difference mean "the override was applied" rather than "the renderer is nondeterministic" —
/// which the first test here also pins, by drawing the null case twice.
/// </remarks>
public sealed class OverrideMaterialRenderTests
{
    /// <remarks>
    /// **Two draws of the same thing, so the comparison below has a floor.** A renderer that
    /// answered a different pixel every frame would make every difference test meaningless, and
    /// nothing else in this file would notice.
    /// </remarks>
    [Test]
    public void DrawModel_TheSameQuadTwiceWithNoOverride_ReturnsTheSamePixel()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Textured(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        Draw(target, assets, material, null).ShouldBe(Draw(target, assets, material, null));
    }

    /// <remarks>
    /// **The whole claim in one row.** The override names a material the quad's own batch never
    /// mentions, so a bind that ignored it would answer the same three numbers.
    /// </remarks>
    [Test]
    public void DrawModel_WithTheGoldOverride_DrawsSomethingOtherThanTheMaterialsOwn()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Textured(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (int R, int G, int B) own = Draw(target, assets, material, null);
        (int R, int G, int B) gold = Draw(target, assets, material, RagdollAppearance.GoldMaterial);

        TestContext.Out.WriteLine($"the material's own {own}, forced to gold {gold}");

        gold.ShouldNotBe(own, "the override replaces every one of the model's materials");
    }

    /// <remarks>
    /// **A direction, not just a difference.** "Something changed" is satisfied by a bind that
    /// forced the magenta chequer, or the white fallback, or the wrong entry entirely — all of which
    /// are failures wearing the same reading as a success.
    ///
    /// `gold_player.vtf` has a mean RGBA of `(57, 42, 21, 158)` and `ice_player.vtf` `(158, 158,
    /// 158, 253)`, both measured by the `vmt` probe reading the VPK directly. So the prediction is
    /// an ORDERING: gold draws strictly warm — red above green above blue — under a white uniform
    /// light, and ice does not.
    ///
    /// **Ice is the base of the comparison rather than the map's own material, and that correction
    /// came from a sabotage.** The first version compared gold's ordering against nothing and drew
    /// the quad's own material as the control; with the override lookup disabled entirely the test
    /// still passed, because the map material it happened to pick draws `(12, 11, 10)` — warm by
    /// the same ordering. An assertion satisfied by both the correct and the broken code measures
    /// nothing, whatever it says (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`, the
    /// "wrong condition" case). Ice cannot be warm, so putting the two overrides on either side of
    /// the ordering makes the manipulation the only thing that decides it.
    /// </remarks>
    [Test]
    public void DrawModel_WithTheGoldOverride_DrawsWarmWhereIceDrawsCold()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Textured(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (int R, int G, int B) gold = Draw(target, assets, material, RagdollAppearance.GoldMaterial);
        (int R, int G, int B) ice = Draw(target, assets, material, RagdollAppearance.IceMaterial);

        TestContext.Out.WriteLine($"forced to gold {gold}, forced to ice {ice}");

        gold.R.ShouldBeGreaterThan(gold.G, "gold is warm: red above green");
        gold.G.ShouldBeGreaterThan(gold.B, "gold is warm: green above blue");

        ice.B.ShouldBeGreaterThan(
            ice.R, "ice is cold, and it is the control: a bind that ignored the path would " +
            "answer the quad's own material for both");
    }

    /// <remarks>
    /// **The two overrides must differ from each other, which the pair above cannot establish.** A
    /// bind that forced ONE material for any non-null path would pass every test above — it would
    /// differ from the model's own, and it would be gold, because gold is the one it forced.
    ///
    /// Ice's swatch is neutral and bright, `(158, 158, 158, 253)` by the same probe, so it fails the
    /// warm ordering gold is asserted to hold.
    /// </remarks>
    [Test]
    public void DrawModel_WithTheIceOverride_DrawsSomethingOtherThanGold()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets || Textured(assets) is not { } material)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        (int R, int G, int B) gold = Draw(target, assets, material, RagdollAppearance.GoldMaterial);
        (int R, int G, int B) ice = Draw(target, assets, material, RagdollAppearance.IceMaterial);

        TestContext.Out.WriteLine($"gold {gold}, ice {ice}");

        ice.ShouldNotBe(gold, "two paths, two materials");
    }

    /// <summary>Draws a quad as a model, with or without an override, and reads the centre.</summary>
    /// <remarks>
    /// **Uniform ambient and no sun**, so the pixel is the material under flat light and nothing
    /// about the geometry can move between two draws. The quad faces the camera and never moves.
    /// </remarks>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target, MapAssets assets, int material, string? overrideMaterial)
    {
        (float X, float Y, float Z) normal = (0f, -1f, 0f);

        List<WorldVertex> vertices =
        [
            Vertex(-64f, 0f, -64f, 0f, 0f, normal),
            Vertex(64f, 0f, -64f, 1f, 0f, normal),
            Vertex(64f, 0f, 64f, 1f, 1f, normal),
            Vertex(-64f, 0f, -64f, 0f, 0f, normal),
            Vertex(64f, 0f, 64f, 1f, 1f, normal),
            Vertex(-64f, 0f, 64f, 0f, 1f, normal),
        ];

        float[] model =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];

        float[] camera = new FreeCamera
        {
            Origin = (0f, -300f, 0f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(material, 0, vertices.Count)],
            camera,
            model,
            assets,
            light: Neutral,
            bothSides: true,
            overrideMaterial: overrideMaterial);

        return target.PixelAt(32, 32);
    }

    /// <summary>A dim, uniform ambient cube — bright enough to light, flat enough not to vary.</summary>
    private static AmbientCube Neutral =>
        new((0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f),
            (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f));

    private static WorldVertex Vertex(
        float x, float y, float z, float u, float v, (float X, float Y, float Z) normal) =>
        new(x, y, z, u, v, 0f, 0f, 0f)
        {
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z,
        };

    /// <summary>The first plain material with a texture, standing in for a model's own.</summary>
    /// <remarks>
    /// **Deliberately not gold or ice.** The subject is a material the override must replace, so a
    /// candidate that already looked like the override would make the comparison vacuous — and the
    /// two override entries are now IN this table, at the end, so "the first textured material" has
    /// to mean the first and not merely any.
    /// </remarks>
    private static int? Textured(MapAssets assets) =>
        Enumerable.Range(0, assets.BrushMaterialCount)
            .Cast<int?>()
            .FirstOrDefault(index => assets.Textures[index!.Value] is not null);

    private static MapAssets? Assets
    {
        get
        {
            if (GameInstall.Root is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load();
        }
    }
}
