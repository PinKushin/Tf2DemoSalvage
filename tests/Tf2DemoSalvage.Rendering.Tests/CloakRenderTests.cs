using System;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// VertexLitGeneric's cloak pass on the shipped spy, and the screen overlay — both drawn over a frame they copy.
/// </summary>
/// <remarks>
/// **The subject is `spy_red`**, loaded through the production model path from the shipped VPKs: `$cloakPassEnabled 1`
/// and the proxies `spy_invis` then `invis`. The geometry is a quad over the whole target, so the picture is the pass.
/// </remarks>
public sealed class CloakRenderTests
{
    private const string SpyModel = "models/player/spy.mdl";
    private const int Size = 64;

    [Test]
    public void Load_TheSpysBody_ResolvesACloakPassAndBothInvisibilityProxies()
    {
        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);
        int index = SpyRed(assets);

        assets.Textures[index]!.Value.Cloak.ShouldBe(new CloakPass((1f, 1f, 1f), 0.1f, 0f), "only $cloakPassEnabled is declared");
        assets.Proxies[index].Select(proxy => proxy.Name)
            .Where(name => name.Contains("invis", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(["spy_invis", "invis"], "an empty proxy block is still a proxy");
    }

    /// <remarks>
    /// **Fully cloaked, a cloak-pass material draws nothing at all**: at `$cloakfactor` 1 the standard pass is skipped
    /// (past 4/9) and the cloak pass too (not strictly below 1) — which is how a cloaked enemy's hat vanishes with him.
    /// The control draws the same quad uncloaked, which paints the spy's suit. **Nearly cloaked, 0.99, the spy is the
    /// frame** within a step — but that cannot tell the cloak pass from no draw, since at 0.99 the pass reproduces its
    /// own copy; the half-cloaked test below is what sees the pass.
    /// </remarks>
    [Test]
    public void DrawModelPose_FullyAndNearlyCloakedOverAUniformFrame_IsTheFrame()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);

        target.Clear(0.5f, 0.5f, 0.5f);
        (int, int, int) grey = target.PixelAt(32, 32);

        DrawQuad(target, assets, default);
        target.PixelAt(32, 32).ShouldNotBe(grey, "the control: uncloaked, the quad paints the suit");

        target.Clear(0.5f, 0.5f, 0.5f);
        DrawQuad(target, assets, new CloakBind(1f, 1f));

        target.PixelAt(32, 32).ShouldBe(grey);
        target.PixelAt(6, 57).ShouldBe(grey);

        target.Clear(0.5f, 0.5f, 0.5f);
        DrawQuad(target, assets, new CloakBind(0.99f, 0.99f));

        Near(target.PixelAt(32, 32), grey);
        Near(target.PixelAt(6, 57), grey);
    }

    /// <remarks>
    /// **Half cloaked, the frame comes back tinted by the team**: at 0.5 the standard pass is skipped, the tint strength
    /// `saturate( ( 0.5 − 0.75 ) · 4 )` is 0, so every pixel is the grey times the Fresnel dim times RED's
    /// (1, 0.5, 0.4). The dim is common to all three channels, so in linear light green over red is exactly 0.5 and blue
    /// over red 0.4 — whatever the view angle.
    /// </remarks>
    [Test]
    public void DrawModelPose_HalfCloakedRedSpy_IsTheFrameTimesTheTeamTint()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);

        target.Clear(0.8f, 0.8f, 0.8f);
        DrawQuad(target, assets, new CloakBind(0.5f, 0.5f, CloakPass.TeamTint(2)));

        (int red, int green, int blue) = target.PixelAt(32, 32);

        (SrgbTarget.Decode(green) / SrgbTarget.Decode(red)).ShouldBe(0.5, 0.02);
        (SrgbTarget.Decode(blue) / SrgbTarget.Decode(red)).ShouldBe(0.4, 0.02);
    }

    /// <remarks>
    /// **The refract direction is the bump through the MESH's tangent frame** (cloak_blended_pass_ps2x.fxc:56, B508's
    /// leftover). Over a frame painted on its left half, a half-cloaked quad on one texel of `spy_red`'s normal map shifts
    /// the edge by `lerp( $refractamount, 0, 0.5 ) · (clip-space normal).x` of the target — the camera scales clip space by
    /// four so the shift is pixels, not fractions of one. N = −Z and T = +X put the texel's x straight onto clip x, so
    /// turning T to −X mirrors the shift, and no frame (w zero, one texel so no derivative either) leaves the edge put.
    /// </remarks>
    [Test]
    public void DrawModelPose_HalfCloakedOverAnEdge_ShiftsItAlongTheMeshTangent()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);
        int material = SpyRed(assets);

        assets.Textures[material]!.Value.Cloak.ShouldNotBeNull();
        MapTexture map = assets.Bumps[material]!.Value.Texture;
        byte[] rgba = map.Image.ToRgba(map.Width, map.Height).ToArray();
        int texel = Enumerable.Range(0, rgba.Length / 4).MaxBy(at => Math.Abs(rgba[at * 4] - 127.5f));
        float tangentX = (rgba[texel * 4] / 255f * 2f) - 1f;
        (float U, float V) uv = (((texel % map.Width) + 0.5f) / map.Width, ((texel / map.Width) + 0.5f) / map.Height);

        float Edge((float X, float W) tangent)
        {
            target.Clear(0.8f, 0.8f, 0.8f);
            DrawQuad(target, assets, default, uv, left: true, scaled: true);
            DrawQuad(target, assets, new CloakBind(0.5f, 0.5f), uv, tangent: tangent, scaled: true);

            float Brightness(int x)
            {
                (int r, int g, int b) = target.PixelAt(x, Size / 2);
                return r + g + b;
            }

            float suit = Brightness(2);
            float frame = Brightness(Size - 3);
            float half = (suit + frame) / 2f;

            for (int x = 1; x < Size; x++)
            {
                if ((Brightness(x - 1) - half) * (Brightness(x) - half) <= 0f &&
                    Math.Abs(Brightness(x) - Brightness(x - 1)) > 0.5f)
                {
                    return x - 1 + ((half - Brightness(x - 1)) / (Brightness(x) - Brightness(x - 1))) + 0.5f;
                }
            }

            throw new InvalidOperationException("no edge in the row");
        }

        float predicted = 0.05f * 4f * tangentX * Size;
        float along = Edge((1f, 1f));
        float against = Edge((-1f, 1f));
        float none = Edge((1f, 0f));

        TestContext.Out.WriteLine(
            $"texel x {tangentX:0.###}, predicted shift {predicted:0.##} px: T +X edge {along:0.##}, T -X {against:0.##}, no frame {none:0.##}");

        none.ShouldBe(Size / 2f, 1f, "the control: no frame, no shift");
        Math.Abs(predicted).ShouldBeGreaterThan(4f, "the texel leans far enough to measure");
        (along - none).ShouldBe(-predicted, 1.5f, "the edge moves against the sampled offset");
        (against - none).ShouldBe(predicted, 1.5f, "and mirrors with the tangent");
    }

    /// <remarks>
    /// **The jarate overlay over a uniform frame keeps red and darkens blue**: Refract multiplies the copy by
    /// `$refracttint` {255 225 155}, linear, and blends by the normal map's alpha — so red, tinted by 1, is the grey
    /// whatever the alpha, and blue is the most reduced. The control is the frame without it, every channel equal.
    /// </remarks>
    [Test]
    public void DrawScreenOverlay_JarateOverAUniformFrame_KeepsRedAndDarkensBlue()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load();

        ScreenOverlayMaterial jarate = assets.ScreenOverlays[ScreenOverlay.Urine];

        target.Clear(0.5f, 0.5f, 0.5f);
        (int red, int green, int blue) grey = target.PixelAt(32, 32);

        grey.red.ShouldBe(grey.blue, "the control: an untinted frame");

        target.DrawScreenOverlay(jarate, 0.0).ShouldBeTrue();

        (int red, int green, int blue) = target.PixelAt(32, 32);

        Math.Abs(red - grey.red).ShouldBeLessThanOrEqualTo(1, $"red {red} against {grey.red}");
        blue.ShouldBeLessThan(grey.blue);
        blue.ShouldBeLessThanOrEqualTo(green);
    }

    [Test]
    public void ScreenOverlays_ImCookin_LoadsAndDrawsNothing()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load();

        assets.ScreenOverlays.Keys.ShouldBe(ScreenOverlay.All, ignoreOrder: true, "every overlay the slot can hold ships");
        target.DrawScreenOverlay(assets.ScreenOverlays[ScreenOverlay.Burning], 1.0).ShouldBeFalse();
    }

    /// <remarks>
    /// **The Halloween stealth overlay darkens, evenly**: `ViewDrawFade` draws `effects/stealth_overlay`, a translucent
    /// VertexLitGeneric over the all-black `effects/grey` — so each pixel is the frame × ( 1 − α ), the same factor on all
    /// three channels. The control is the frame before it.
    /// </remarks>
    [Test]
    public void DrawScreenOverlay_StealthOverAUniformFrame_DarkensEveryChannelAlike()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load();
        ScreenOverlayMaterial stealth = assets.ScreenOverlays[ScreenOverlay.Stealth];

        stealth.Refract.ShouldBeNull("as shipped, its Refract block is commented out");
        stealth.Fade.ShouldNotBeNull();

        target.Clear(0.8f, 0.8f, 0.8f);
        (int red, int green, int blue) grey = target.PixelAt(32, 32);

        target.DrawScreenOverlay(stealth, 0.0).ShouldBeTrue();

        (int red, int green, int blue) = target.PixelAt(32, 32);

        red.ShouldBeLessThan(grey.red);
        green.ShouldBe(red);
        blue.ShouldBe(red);
    }

    /// <remarks>
    /// **The animated overlays carry every frame of their normal map**: jarate's `water/tfwater001_normal` is an animated
    /// VTF; invulnerability's is a still one.
    /// </remarks>
    [Test]
    public void ScreenOverlays_JarateAndInvuln_LoadTheirNormalFrames()
    {
        MapAssets assets = MapCache.Load();

        assets.ScreenOverlays[ScreenOverlay.Urine].NormalFrames.Count.ShouldBeGreaterThan(1);
        assets.ScreenOverlays[ScreenOverlay.InvulnRed].NormalFrames.Count.ShouldBe(1);
    }

    private static int SpyRed(MapAssets assets)
    {
        int index = Enumerable.Range(0, assets.Materials.Count)
            .FirstOrDefault(at => assets.Materials[at].Name.EndsWith("spy/spy_red", StringComparison.OrdinalIgnoreCase), -1);

        index.ShouldBeGreaterThanOrEqualTo(0, "the spy model names spy_red");

        return index;
    }

    /// <param name="target">Where to draw.</param>
    /// <param name="assets">The spy's materials.</param>
    /// <param name="cloak">The cloak bind.</param>
    /// <param name="texel">One texture coordinate for every corner, or null for the whole texture.</param>
    /// <param name="tangent">The corners' tangent, along X, and its sign; none by default.</param>
    /// <param name="left">Cover only the left half of the target.</param>
    /// <param name="scaled">Draw through a camera that scales clip x and y by four, the quad shrunk to match.</param>
    private static void DrawQuad(
        OffscreenTarget target,
        MapAssets assets,
        CloakBind cloak,
        (float U, float V)? texel = null,
        (float X, float W) tangent = default,
        bool left = false,
        bool scaled = false)
    {
        float extent = scaled ? 0.25f : 1f;
        float right = left ? 0f : extent;

        WorldVertex Corner(float x, float y, float u, float v) =>
            new(x, y, 0.5f, texel?.U ?? u, texel?.V ?? v, 0f, 0f, 1f)
            {
                NormalZ = -1f,
                TangentX = tangent.X,
                TangentW = tangent.W,
            };

        WorldVertex[] quad =
        [
            Corner(-extent, -extent, 0f, 1f), Corner(right, extent, 1f, 0f), Corner(right, -extent, 1f, 1f),
            Corner(-extent, -extent, 0f, 1f), Corner(-extent, extent, 0f, 0f), Corner(right, extent, 1f, 0f),
        ];

        float[] camera = scaled
            ? [4f, 0f, 0f, 0f, 0f, 4f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f]
            : Identity;

        target.DrawModelPose(
            quad, [new WorldBatch(SpyRed(assets), 0, quad.Length)], camera, Identity, assets, bothSides: true, cloak: cloak);
    }

    private static void Near((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected)
    {
        Math.Abs(actual.Red - expected.Red).ShouldBeLessThanOrEqualTo(1, $"red {actual} against {expected}");
        Math.Abs(actual.Green - expected.Green).ShouldBeLessThanOrEqualTo(1, $"green {actual} against {expected}");
        Math.Abs(actual.Blue - expected.Blue).ShouldBeLessThanOrEqualTo(1, $"blue {actual} against {expected}");
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
