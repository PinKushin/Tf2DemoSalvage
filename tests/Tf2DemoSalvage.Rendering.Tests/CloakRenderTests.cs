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

    private static void DrawQuad(OffscreenTarget target, MapAssets assets, CloakBind cloak)
    {
        WorldVertex Corner(float x, float y, float u, float v) =>
            new(x, y, 0.5f, u, v, 0f, 0f, 1f) { NormalZ = -1f };

        WorldVertex[] quad =
        [
            Corner(-1f, -1f, 0f, 1f), Corner(1f, 1f, 1f, 0f), Corner(1f, -1f, 1f, 1f),
            Corner(-1f, -1f, 0f, 1f), Corner(-1f, 1f, 0f, 0f), Corner(1f, 1f, 1f, 0f),
        ];

        target.DrawModelPose(
            quad, [new WorldBatch(SpyRed(assets), 0, quad.Length)], Identity, Identity, assets, bothSides: true, cloak: cloak);
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
