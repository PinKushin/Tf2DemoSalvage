using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// `CWeaponSkinProxy` at the bind (B754): a material running `"WeaponSkin"` draws its entity's composite as `$basetexture`
/// (c_tf_player.cpp:3540-3546), and keeps its own where there is none (`TextureVarSetter`, :3474).
/// </summary>
/// <remarks>
/// **Synthetic** (D38): a grey 4x4 base under an ambient-only light, and a recipe of one `combine_multiply` over a 1x1 red
/// texture, so the composite is pure red and the drawn pixel's channels say which texture was bound.
/// </remarks>
public sealed class WeaponSkinRenderTests
{
    [Test]
    public void DrawModelPose_AWeaponSkinMaterialWithAPaint_DrawsTheComposite()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (int R, int G, int B) own = Draw(target, Assets(proxy: true), skin: null);
        (int R, int G, int B) painted = Draw(target, Assets(proxy: true), Skin("patterns/red"));

        TestContext.Out.WriteLine($"WEAPONSKIN own {own}, painted {painted}");

        (own.R, own.G == own.R, own.B == own.R).ShouldBe((own.R, true, true), "the control: the grey base");
        own.R.ShouldBeGreaterThan(0, "the control: the base is drawn at all");
        painted.ShouldBe((255, 0, 0), "under mat_fullbright 1 the pixel is the composite's albedo, pure red");
    }

    [Test]
    public void DrawModelPose_APaintOnAMaterialWithoutTheProxy_DrawsItsOwnBase()
    {
        // Only a material that runs the proxy binds the composite: the entity's paint is the proxy's INPUT.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (int R, int G, int B) own = Draw(target, Assets(proxy: false), skin: null);

        own.R.ShouldBeGreaterThan(0, "the control: the grey base is drawn");
        Draw(target, Assets(proxy: false), Skin("patterns/red")).ShouldBe(own);
    }

    [Test]
    public void DrawModelPose_ACompositeWhoseTextureDidNotLoad_KeepsTheMaterialsOwnBase()
    {
        // `ECRS_Error`: "just show the current base texture forever" (:3586-3592).
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (int R, int G, int B) own = Draw(target, Assets(proxy: true), skin: null);

        own.R.ShouldBeGreaterThan(0, "the control: the grey base is drawn, so a failure binding nothing would show");
        Draw(target, Assets(proxy: true), Skin("patterns/missing")).ShouldBe(own);
    }

    private static WeaponSkin Skin(string texture)
    {
        PaintKitNode recipe = new("wear_level_1");
        PaintKitNode multiply = new("combine_multiply");
        PaintKitNode lookup = new("texture_lookup");
        lookup.Add(new PaintKitNode("texture") { Value = texture });
        multiply.Add(lookup);
        recipe.Add(multiply);

        return new WeaponSkin("1_2_wear_01", recipe, 2, 0ul, FullSize: true);
    }

    private static MapAssets Assets(bool proxy)
    {
        byte[] grey = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 128, 255 })];

        return MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false)],
            ["VertexLitGeneric"],
            proxies: [proxy ? [new MaterialProxy("WeaponSkin")] : []],
            compositorTextures: new Dictionary<string, MapTexture>
            {
                ["patterns/red"] = new(1, 1, 1, 1, TextureImage.Rgba(new byte[] { 255, 0, 0, 255 }), IsTransparent: false),
            });
    }

    private static (int R, int G, int B) Draw(OffscreenTarget target, MapAssets assets, WeaponSkin? skin)
    {
        static WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f) { NormalX = 0f, NormalY = -1f, NormalZ = 0f };

        List<WorldVertex> vertices =
        [
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, -64f, 1f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, 64f, 0f, 1f),
        ];

        float[] identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];
        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();
        float grey = 0.5f;

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count)],
            camera,
            identity,
            assets,
            light: new AmbientCube((grey, grey, grey), (grey, grey, grey), (grey, grey, grey), (grey, grey, grey), (grey, grey, grey), (grey, grey, grey)),
            bothSides: true,

            // `mat_fullbright 1`: the albedo alone, so the pixel is the bound texture's colour.
            fullbright: Fullbright.NoLighting,
            weaponSkin: skin);

        return target.PixelAt(32, 32);
    }
}
