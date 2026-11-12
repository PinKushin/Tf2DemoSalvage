using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary><c>mat_bumpmap 0</c> on a model, drawn on a real device from one hand-built skin material (B720).</summary>
/// <remarks>
/// **What the engine does, read from published source.** `DrawVertexLitGeneric_DX9` draws `DrawSkin_DX9` only while
/// `g_pConfig->UseBumpmapping()` holds (`vertexlitgeneric_dx9_helper.cpp:1515`); otherwise
/// `DrawVertexLitGeneric_DX9_Internal`, whose half-Lambert is `MATERIAL_VAR_HALFLAMBERT` alone (`:504`) and which has no
/// phong and no rim. Neither init loads the bump map under it (`:312-322`, `skin_dx9_helper.cpp:189-203`), so
/// `bHasBump = IsTextureSet( $bumpmap )` (`:389`) is false and the vertex normal lights the model. TF2 reloads every
/// material when the flag changes (`materialsystem.dll` `FUN_180018fd0`, bit 8), so the choice follows the current value.
///
/// **Synthetic** (D38): one 4x4 grey base, one normal map leaning every texel the same way, one `$phong` with a flat
/// Fresnel, so each prediction is exact rather than a direction.
/// </remarks>
public sealed class BumpMapSwitchRenderTests
{
    /// <summary>The normal map's every texel: x 128, y 242, z 182, about 50 degrees off the surface normal.</summary>
    private static readonly byte[] Leaning = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 242, 182, 255 })];

    [Test]
    public void DrawModelPose_WithBumpMapOff_LeavesTheNormalMapUnread()
    {
        // The tangent frame's handedness turns a leaning normal up or down (+n.B or -n.B on z), so with the map read the
        // two pictures differ, and with it unread they are one picture: the vertex normal has no handedness.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapAssets assets = Skin();
        (float X, float Y, float Z) downAndIn = (0f, 0.7071f, -0.7071f);

        int onPlus = Sum(Draw(target, assets, downAndIn, tangentW: 1f, bumpMap: true));
        int onMinus = Sum(Draw(target, assets, downAndIn, tangentW: -1f, bumpMap: true));
        int offPlus = Sum(Draw(target, assets, downAndIn, tangentW: 1f, bumpMap: false));
        int offMinus = Sum(Draw(target, assets, downAndIn, tangentW: -1f, bumpMap: false));

        TestContext.Out.WriteLine($"bump on: +1 {onPlus}, -1 {onMinus}; bump off: +1 {offPlus}, -1 {offMinus}");

        System.Math.Abs(onPlus - onMinus).ShouldBeGreaterThan(8, "the control: with the map read, the frame's sign turns the light");
        offMinus.ShouldBe(offPlus, "mat_bumpmap 0 lights the model from its vertex normal, which has no handedness");
    }

    [Test]
    public void DrawModelPose_SkinMaterialWithBumpMapOff_FallsBackToPlainLambert()
    {
        // A light edge-on to the vertex normal: N·L = 0, so plain Lambert gives nothing and the lit draw is the
        // ambient-only draw exactly. The skin shader's half-Lambert would give a quarter of the light (`skin_ps20b.fxc:222`).
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapAssets assets = Skin();
        (float X, float Y, float Z) down = (0f, 0f, -1f);

        int on = Sum(Draw(target, assets, down, tangentW: 1f, bumpMap: true));
        int onAmbient = Sum(Draw(target, assets, null, tangentW: 1f, bumpMap: true));
        int off = Sum(Draw(target, assets, down, tangentW: 1f, bumpMap: false));
        int offAmbient = Sum(Draw(target, assets, null, tangentW: 1f, bumpMap: false));

        TestContext.Out.WriteLine($"bump on {on} (ambient {onAmbient}), off {off} (ambient {offAmbient})");

        on.ShouldBeGreaterThan(onAmbient, "the control: the skin shader lights an edge-on light");
        off.ShouldBe(offAmbient, "the generic path's plain Lambert gives an edge-on light nothing");
    }

    [Test]
    public void DrawModelPose_PhongMaterialWithBumpMapOff_DrawsNoHighlight()
    {
        // A light from the eye onto a surface facing it: N·L = 1, where half-Lambert and Lambert agree, so the only
        // term `mat_phong` can change is the highlight. With the skin shader gone there is none to change.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapAssets assets = Skin();
        (float X, float Y, float Z) fromTheEye = (0f, 1f, 0f);

        int onPhong = Sum(Draw(target, assets, fromTheEye, tangentW: 1f, bumpMap: true));
        int onNoPhong = Sum(Draw(target, assets, fromTheEye, tangentW: 1f, bumpMap: true, phong: false));
        int offPhong = Sum(Draw(target, assets, fromTheEye, tangentW: 1f, bumpMap: false));
        int offNoPhong = Sum(Draw(target, assets, fromTheEye, tangentW: 1f, bumpMap: false, phong: false));

        TestContext.Out.WriteLine($"bump on: phong {onPhong}, none {onNoPhong}; bump off: phong {offPhong}, none {offNoPhong}");

        onPhong.ShouldBeGreaterThan(onNoPhong + 8, "the control: the skin shader draws a highlight from the eye");
        offPhong.ShouldBe(offNoPhong, "DrawVertexLitGeneric_DX9_Internal has no phong for mat_phong to remove");
    }

    /// <summary>One VertexLitGeneric material the skin shader wants: grey base, the leaning normal map, flat-Fresnel phong.</summary>
    private static MapAssets Skin()
    {
        byte[] grey = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 128, 255 })];

        return MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false, IsSkinShader: true)],
            ["VertexLitGeneric"],
            bumps: [new MapBump(new MapTexture(4, 4, 4, 4, TextureImage.Rgba(Leaning), IsTransparent: false), false)],

            // A Fresnel range of (0, 1, 0) encoded is 1 at every angle, so the highlight is not zero head-on.
            phong: [new MapPhong(5f, 1f, (0f, 1f, 0f), (1f, 1f, 1f), MaskedByBaseAlpha: false)]);
    }

    /// <summary>A quad facing the camera with a tangent frame, drawn as a model under one sun, centre pixel read.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target,
        MapAssets assets,
        (float X, float Y, float Z)? travelling,
        float tangentW,
        bool bumpMap,
        bool phong = true)
    {
        WorldVertex Corner(float x, float z, float u, float v) =>
            new WorldVertex(x, 0f, z, u, v, 0f, 0f, 0f)
            {
                NormalX = 0f,
                NormalY = -1f,
                NormalZ = 0f,
                TangentX = 1f,
                TangentY = 0f,
                TangentZ = 0f,
                TangentW = tangentW,
            };

        List<WorldVertex> vertices =
        [
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, -64f, 1f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, -64f, 0f, 0f),
            Corner(64f, 64f, 1f, 1f),
            Corner(-64f, 64f, 0f, 1f),
        ];

        float[] identity =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];

        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count)],
            camera,
            identity,
            assets,

            // Uniform, so it adds the same whatever the normal; the sun needs a cube to be applied at all.
            light: new AmbientCube((0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f), (0.1f, 0.1f, 0.1f)),
            bothSides: true,
            sun: travelling is { } direction ? new SunLight(1f, 1f, 1f, direction.X, direction.Y, direction.Z) : null,
            phong: phong,
            bumpMap: bumpMap);

        return target.PixelAt(32, 32);
    }

    private static int Sum((int R, int G, int B) pixel) => pixel.R + pixel.G + pixel.B;
}
