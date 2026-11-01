using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A modulating material fogs toward its neutral grey, in gamma, as Valve's does (B637): hand-built materials, no game
/// install, every expected value Valve's arithmetic written out.
/// </summary>
/// <remarks>
/// **Valve, quoted.** `DecalModulate`: `fogFactor = CalcPixelFogFactor( PIXELFOGTYPE, … )`, then
/// <c>fogFactor = pow( saturate( fogFactor ), 0.4f )</c>, then <c>FinalOutput( result, fogFactor, PIXELFOGTYPE,
/// TONEMAP_SCALE_NONE )</c> (<c>decalmodulate_ps2x.fxc:37-44</c>), with <c>DisableFogGammaCorrection( true )</c> and
/// <c>FogToGrey()</c> in its shadow state (<c>DecalModulate_dx9.cpp:79-80</c>). `Modulate`: the factor unchanged
/// (<c>modulate_ps2x.fxc:36-37</c>), <c>FogToGrey()</c> under `$mod2x`, <c>FogToOOOverbright()</c> without
/// (<c>modulate_dx9.cpp:201-209</c>). `FinalOutput`'s <c>BlendPixelFog</c> lerps toward <c>g_LinearFogColor</c> by the
/// factor SQUARED for range or radial fog and by the factor itself for height fog (<c>common_ps_fxc.h:284-298</c>).
/// Both shaders read and write without sRGB, so the lerp and the blend are on stored (gamma) values: `dst · 2s` for
/// DecalModulate and `$mod2x`, `dst · s` for plain Modulate.
///
/// **The fog colour is decided in the shader API** (<c>shaderapidx9.dll</c> <c>0x180025a50</c>): 128/255 under range fog,
/// the sRGB decode of 128/255 under height fog, times the tone-map scale for Modulate and not for DecalModulate. What is read and
/// what is interpolated: <c>docs/measurements/2026-11-02-modulate-fog-plan.md</c>.
///
/// **The condition makes the factor exact** (<see cref="FogRenderTests"/>' trick): an identity camera puts the quad at
/// clip z 0.9 and the eye at the origin. Range fog over 0..0.9 reaches 1 there, so the max density IS the factor. Height
/// fog with the water surface at z −0.9 puts the whole eye-to-point line under water (the fraction saturates at 1), and a
/// range of 1.8 makes the factor 0.9 / 1.8 = 0.5.
/// </remarks>
[NonParallelizable]
public sealed class ModulateFogConformanceTests
{
    private const int DecalModulateDark = 0;
    private const int PlainModulate = 1;
    private const int ModulateTwice = 2;
    private const int Lit = 3;
    private const int PlainModulate128 = 4;

    /// <summary>The quad's clip depth under the identity camera, which is also its world z.</summary>
    private const float Depth = 0.9f;

    /// <summary>The grey both fog modes fade toward under range fog: 128 of 255, `r = g = b = 128` (plan doc).</summary>
    private const double Grey = 128.0 / 255.0;

    /// <summary>
    /// The same grey as the height-fog path delivers it: shaderapi's sRGB-to-linear of 128/255, 0.2159, with `r_shader_srgb`
    /// at its default 0 (plan doc, B637 review).
    /// </summary>
    private static readonly double LinearGrey = SrgbTarget.Decode(128);

    /// <summary>Red fog, so a lerp toward the scene's fog colour instead of the grey shows as unequal channels.</summary>
    private static readonly SceneFog HalfRange = new(0f, Depth, 1f, 0f, 0f, 0.5f);

    /// <summary>The water surface and range that make the height fog factor exactly one half (the remarks).</summary>
    private static readonly VolumeFog HalfHeight = new(HeightFog: true, FogZ: -Depth, Color: (1f, 0f, 0f), Start: 0f, End: 2f * Depth, MaxDensity: 1f);

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [
            ModulateToneMapConformanceTests.Grey(32, modulate: true, twice: true, decal: true),
            ModulateToneMapConformanceTests.Grey(64, modulate: true),
            ModulateToneMapConformanceTests.Grey(64, modulate: true, twice: true),
            ModulateToneMapConformanceTests.Grey(100),
            ModulateToneMapConformanceTests.Grey(128, modulate: true),
        ],
        ["DecalModulate", "Modulate", "Modulate", "LightmappedGeneric", "Modulate"]);

    /// <remarks>
    /// **The unfogged control**: the same draws with no fog land on the plain blend, so every fogged case below differs
    /// from its own control by the fog alone. Texel 32 over 188: 188 · 2 · 32/255 ≈ 47.
    /// </remarks>
    [TestCase(DecalModulateDark, 32, true)]
    [TestCase(PlainModulate, 64, false)]
    [TestCase(ModulateTwice, 64, true)]
    public void Draw_AModulatingMaterialWithNoFog_IsThePlainGammaBlend(int material, int texel, bool twice)
    {
        float surface = SurfaceFor(material);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(material, surface) ?? Skip();

        Near(drawn, Predict(under.Item2, texel / 255.0, 0.0, twice), $"material {material} unfogged drew {drawn} over {under}");
    }

    /// <remarks>
    /// **Range fog, factor 0.5.** DecalModulate: `pow( 0.5, 0.4 )` = 0.7579, squared by `BlendPixelFog` = 0.5743, so texel
    /// 32 becomes 0.3417 and stores 188 · 2 · 0.3417 ≈ 128. Plain Modulate, no exponent: 0.25, texel 64 becomes 0.3137 and
    /// stores 188 · 0.3137 ≈ 59. `$mod2x` the same lerp, doubled over its own surface. The wrong readings land far off: no
    /// fog 47; DecalModulate without its exponent 83; a fade toward the red fog colour unequal channels.
    /// </remarks>
    [TestCase(DecalModulateDark, 32, true, 0.8)]
    [TestCase(PlainModulate, 64, false, 2.0)]
    [TestCase(ModulateTwice, 64, true, 2.0)]
    public void Draw_AModulatingMaterialUnderHalfRangeFog_FadesTowardGreyInGamma(int material, int texel, bool twice, double exponent)
    {
        float surface = SurfaceFor(material);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(material, surface, fog: HalfRange) ?? Skip();

        // The lerp weight is 0.5 to `exponent`: `pow( f, 0.4 )` squared is f^0.8 for DecalModulate, f squared for Modulate.
        Near(drawn, Predict(under.Item2, texel / 255.0, Math.Pow(0.5, exponent), twice), $"material {material} under half range fog drew {drawn} over {under}");
    }

    /// <remarks>
    /// **Height fog, factor 0.5, unsquared** (<c>common_ps_fxc.h:297</c>), toward the grey the height-fog path makes
    /// LINEAR: the sRGB decode of 128/255 = 0.2159 (shaderapidx9 <c>0x180025b9a</c> under `r_shader_srgb 0`, plan doc).
    /// DecalModulate: 0.5^0.4 = 0.7579, texel 32 becomes 0.1940 and stores 188 · 2 · 0.1940 ≈ 73 (unfogged 47; toward
    /// 128/255 it would be 154). Plain Modulate of 128: weight 0.5, 0.3589, ≈ 67 (unfogged 94). The branch used to be
    /// unreachable for these materials, and its exit is the gamma one.
    /// </remarks>
    [TestCase(DecalModulateDark, 32, true, 0.4)]
    [TestCase(PlainModulate128, 128, false, 1.0)]
    [TestCase(ModulateTwice, 64, true, 1.0)]
    public void Draw_AModulatingMaterialUnderHalfHeightFog_FadesTowardTheLinearGreyUnsquared(int material, int texel, bool twice, double exponent)
    {
        float surface = SurfaceFor(material);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(material, surface, heightFog: HalfHeight) ?? Skip();

        Near(drawn, Predict(under.Item2, texel / 255.0, Math.Pow(0.5, exponent), twice, LinearGrey), $"material {material} under half height fog drew {drawn} over {under}");
    }

    /// <remarks>
    /// **Modulate's grey takes the tone-map scale; DecalModulate's does not.** Only DecalModulate calls
    /// `DisableFogGammaCorrection( true )`, and shaderapi scales the pixel fog colour by the tone-map scale under integer
    /// HDR unless that flag is set (<c>0x180025bec</c>/<c>0x180025c8b</c>, plan doc). Full range fog at a scale of 0.5:
    /// plain Modulate outputs 128/255 · 0.5 and stores 188 · 0.251 ≈ 47; DecalModulate outputs 128/255 and stores ≈ 189.
    /// **Texel 128 for Modulate, on purpose** (review): with texel 64 the correct answer (47) equals the unfogged control
    /// (188 · 64/255 ≈ 47), so a Modulate that skipped fog whenever the scale was not 1 passed. At 128 the unfogged draw
    /// and the unscaled grey both store 94.
    /// </remarks>
    [TestCase(PlainModulate128, false, 0.5)]
    [TestCase(DecalModulateDark, true, 1.0)]
    public void Draw_AModulatingMaterialUnderFullRangeFogAtHalfToneMapScale_FadesTowardItsGrey(int material, bool twice, double greyScale)
    {
        float surface = SurfaceFor(material);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(material, surface, fog: HalfRange with { MaxDensity = 1f }, toneMapScale: 0.5f) ?? Skip();

        Near(drawn, Predict(under.Item2, 0.0, 1.0, twice, Grey * greyScale), $"material {material} under full fog at scale 0.5 drew {drawn} over {under}");
    }

    /// <remarks>
    /// **Radial fog reaches DecalModulate but not Modulate.** DecalModulate calls the float3 <c>CalcPixelFogFactor</c>
    /// (<c>decalmodulate_ps2x.fxc:37</c>), which takes radial fog; Modulate calls the z-only legacy overload
    /// (<c>modulate_ps2x.fxc:36</c>), which turns radial into range (<c>common_ps_fxc.h:272-273</c>). At the left edge
    /// pixel (x ≈ −0.94) the wall is 0.9 deep and about 1.30 from the eye, so with fog over 0..1 range gives 0.9 and radial
    /// saturates at 1 (<see cref="FogRenderTests"/>' edge trick). Modulate 64: weight 0.81, 188 · 0.4543 ≈ 85 (radial would
    /// be ≈ 94). DecalModulate 32: weight 1, ≈ 189 (range would be ≈ 177).
    /// </remarks>
    [TestCase(PlainModulate, 64, false, 0.81)]
    [TestCase(DecalModulateDark, 32, true, 1.0)]
    public void Draw_AModulatingMaterialUnderRadialFogAtTheViewsEdge_FogsByItsShadersOverload(int material, int texel, bool twice, double weight)
    {
        float surface = SurfaceFor(material);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(material, surface, fog: new SceneFog(0f, 1f, 1f, 0f, 0f, 1f) with { Radial = true }, pixelX: 0) ?? Skip();

        Near(drawn, Predict(under.Item2, texel / 255.0, weight, twice), $"material {material} under radial fog at the edge drew {drawn} over {under}");
    }

    /// <remarks>
    /// **The height-fog branch keeps the tone-map exemption** (B635), now that a modulating material reaches it: the same
    /// draw at a scale of 0.5 and of 2 stores what it stores at 1. Replaces the B637 tripwire that pinned fog mode 2.
    /// </remarks>
    [TestCase(0.5f)]
    [TestCase(2f)]
    public void Draw_DecalModulateUnderHeightFogAtAnyToneMapScale_IsUnscaled(float scale)
    {
        float surface = SurfaceFor(DecalModulateDark);
        (int, int, int) atOne = Draw(DecalModulateDark, surface, heightFog: HalfHeight) ?? Skip();
        (int, int, int) scaled = Draw(DecalModulateDark, surface, heightFog: HalfHeight, toneMapScale: scale) ?? Skip();

        Near(scaled, atOne, $"the fogged scorch drew {atOne} at scale 1 and {scaled} at {scale}");
    }

    /// <remarks>
    /// **Modulate under height fog at a scale of 0.5: the scale is on the GREY, not on the result** (review). From the
    /// shader's own formula, `lerp( texel, FogTarget(…) = LinearGrey · 0.5, waterFog ) · ToneMapScale(1)`: texel 128/255,
    /// grey 0.2159 · 0.5 = 0.1079, weight 0.5, gives 0.3050 and stores 188 · 0.3050 ≈ 57. Moving the scale to the outer
    /// multiply gives 0.1795, ≈ 34; dropping it gives 0.3589, ≈ 67.
    /// </remarks>
    [Test]
    public void Draw_ModulateUnderHalfHeightFogAtHalfToneMapScale_ScalesOnlyTheGrey()
    {
        float surface = SurfaceFor(PlainModulate128);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(PlainModulate128, surface, heightFog: HalfHeight, toneMapScale: 0.5f) ?? Skip();

        Near(drawn, Predict(under.Item2, Grey, 0.5, false, LinearGrey * 0.5), $"Modulate 128 under half height fog at scale 0.5 drew {drawn} over {under}");
    }

    /// <remarks>
    /// **The instrument's control:** the same fog reaches a lit draw and pulls it toward RED, so the grey above is the
    /// modulating materials' own target and not fog that never arrived. Full red range fog makes a lit surface the fog
    /// colour itself.
    /// </remarks>
    [Test]
    public void Draw_ALitMaterialUnderFullRedFog_IsTheFogColour()
    {
        (int, int, int) drawn = Draw(Lit, 0.5f, fog: HalfRange with { MaxDensity = 1f }) ?? Skip();

        drawn.ShouldBe((255, 0, 0), "full red range fog over a lit surface");
    }

    /// <remarks>
    /// **Full fog fades a scorch to "change nothing"**: DecalModulate at factor 1 outputs the grey, 188 · 2 · 128/255 ≈
    /// 189. Plain Modulate fogs to the same grey under `FogToOOOverbright`, which for its `dst · s` blend HALVES the
    /// surface (≈ 94): Valve's, not neutral, kept as read (plan doc).
    /// </remarks>
    [TestCase(DecalModulateDark, true)]
    [TestCase(PlainModulate, false)]
    public void Draw_AModulatingMaterialUnderFullRangeFog_IsTheGrey(int material, bool twice)
    {
        float surface = SurfaceFor(material);
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) drawn = Draw(material, surface, fog: HalfRange with { MaxDensity = 1f }) ?? Skip();

        Near(drawn, Predict(under.Item2, 0.0, 1.0, twice), $"material {material} under full fog drew {drawn} over {under}");
    }

    /// <remarks>
    /// **A target with no gamma twin fogs in gamma first, then decodes** (B641's fallback): the shader's single exit decodes
    /// the fogged STORED value, so under full fog the grey 128 decodes to 0.2158 and the linear blend stores
    /// sRGB(0.5 · 2 · 0.2158) ≈ 128. Fog applied after the decode, toward a linear 0.5, would store 188 instead.
    /// </remarks>
    [Test]
    public void Draw_DecalModulateUnderFullFogOnATargetWithNoTwin_FogsBeforeTheDecode()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Skip();
            return;
        }

        const float Surface = 0.5f;

        target.Clear(Surface, Surface, Surface);
        target.ForgetGammaTwin();
        target.DrawWorld(
            Wall(),
            [new WorldBatch(DecalModulateDark, 0, 0)],
            Identity,
            Assets,
            decals: [new WorldBatch(DecalModulateDark, 0, 6, Category: SurfaceCategory.Overlay)],
            translucent: false,
            fog: HalfRange with { MaxDensity = 1f },
            toneMapScale: 1f);

        (int Red, int Green, int Blue) drawn = target.PixelAt(8, 8);
        int expected = SrgbTarget.Encode(Surface * 2.0 * SrgbTarget.Decode(128));

        target.MissingGammaTwins.ShouldBe(1, "the control: the run found no twin, so this is the fallback");
        drawn.Green.ShouldBeInRange(expected - 2, expected + 2, $"with no twin, full fog drew {drawn}; fogged in gamma then decoded {expected}");
    }

    /// <remarks>
    /// **The same fallback on the height-fog path** (review): the height branch also lerps before the single exit decodes.
    /// Half height fog takes texel 32 to o = 0.1255 + (0.2159 − 0.1255) · 0.7579 = 0.1940 in gamma; decoded and drawn
    /// through the sRGB view over 0.5 linear, the store is sRGB( 0.5 · 2 · decode(o) ) = o, ≈ 49 (unfogged fallback ≈ 32).
    /// </remarks>
    [Test]
    public void Draw_DecalModulateUnderHalfHeightFogOnATargetWithNoTwin_FogsBeforeTheDecode()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Skip();
            return;
        }

        const float Surface = 0.5f;

        target.Clear(Surface, Surface, Surface);
        target.ForgetGammaTwin();
        target.DrawWorld(
            Wall(),
            [new WorldBatch(DecalModulateDark, 0, 0)],
            Identity,
            Assets,
            decals: [new WorldBatch(DecalModulateDark, 0, 6, Category: SurfaceCategory.Overlay)],
            translucent: false,
            toneMapScale: 1f,
            heightFog: HalfHeight);

        (int Red, int Green, int Blue) drawn = target.PixelAt(8, 8);
        double texel = 32.0 / 255.0;
        double fogged = texel + ((LinearGrey - texel) * Math.Pow(0.5, 0.4));
        double decoded = fogged <= 0.04045 ? fogged / 12.92 : Math.Pow((fogged + 0.055) / 1.055, 2.4);
        int expected = SrgbTarget.Encode(Surface * 2.0 * decoded);

        target.MissingGammaTwins.ShouldBe(1, "the control: the run found no twin, so this is the fallback");
        drawn.Green.ShouldBeInRange(expected - 2, expected + 2, $"with no twin, half height fog drew {drawn}; fogged in gamma then decoded {expected}");
    }

    /// <summary>The surface each case is drawn over: `$mod2x` over 0.8 linear, where a multiply and a cover differ.</summary>
    private static float SurfaceFor(int material) => material == ModulateTwice ? 0.8f : 0.5f;

    /// <summary>Valve's stored result: the texel lerped toward the grey by the weight, times the destination (twice for mod2x).</summary>
    private static int Predict(int under, double texel, double weight, bool twice, double grey = Grey)
    {
        double source = texel + ((grey - texel) * weight);

        return (int)Math.Round(Math.Min(255.0, under * (twice ? 2.0 : 1.0) * source));
    }

    private static void Near((int Red, int Green, int Blue) actual, int expected, string because) =>
        Near(actual, (expected, expected, expected), because);

    /// <remarks>±2 levels on every channel: one for the 8-bit source, one for the blend's rounding.</remarks>
    private static void Near((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected, string because)
    {
        actual.Red.ShouldBeInRange(expected.Red - 2, expected.Red + 2, because);
        actual.Green.ShouldBeInRange(expected.Green - 2, expected.Green + 2, because);
        actual.Blue.ShouldBeInRange(expected.Blue - 2, expected.Blue + 2, because);
    }

    private static (int, int, int)? Under(float surface)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(surface, surface, surface);

        return target.PixelAt(8, 8);
    }

    /// <summary>
    /// One full-screen quad of one material over the cleared surface: DecalModulate as a decal in the overlay pass, the
    /// others as brushwork (the opaque pass, then the translucent world). Null without a device.
    /// </summary>
    private static (int, int, int)? Draw(
        int material, float surface, SceneFog? fog = null, VolumeFog? heightFog = null, float toneMapScale = 1f, int pixelX = 8)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(surface, surface, surface);

        if (material == DecalModulateDark)
        {
            target.DrawWorld(
                Wall(),
                [new WorldBatch(material, 0, 0)],
                Identity,
                Assets,
                decals: [new WorldBatch(material, 0, 6, Category: SurfaceCategory.Overlay)],
                translucent: false,
                fog: fog,
                toneMapScale: toneMapScale,
                heightFog: heightFog);
        }
        else
        {
            target.DrawWorld(Wall(), [new WorldBatch(material, 0, 6)], Identity, Assets, fog: fog, toneMapScale: toneMapScale, heightFog: heightFog);
        }

        if (material != Lit)
        {
            target.MissingGammaTwins.ShouldBe(0, "every modulating run drew through the target's gamma twin (B641)");
        }

        return target.PixelAt(pixelX, 8);
    }

    /// <summary>The whole view at clip depth 0.9, every corner on one texel, white corners (FogRenderTests' winding).</summary>
    /// <param name="offset">How far to the side, to put a model outside the view.</param>
    private static List<WorldVertex> Wall(float offset = 0f) =>
    [
        new(offset - 1f, -1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(offset + 1f, 1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(offset + 1f, -1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(offset - 1f, -1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(offset - 1f, 1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(offset + 1f, 1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
    ];

    /// <summary>
    /// Modulate 128 as brushwork drawn the production way: the opaque pass, a model at another scale off to the side, then
    /// the translucent world (B426), under the given fog. Null without a device.
    /// </summary>
    private static (int, int, int)? DrawTranslucentAfterAModel(SceneFog? fog, VolumeFog? heightFog, float scale)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(0.5f, 0.5f, 0.5f);
        target.DrawWorld(
            Wall(), [new WorldBatch(PlainModulate128, 0, 6)], Identity, Assets, translucent: false, fog: fog, toneMapScale: scale, heightFog: heightFog);
        target.DrawModelPose(
            Wall(offset: 50f), [new WorldBatch(Lit, 0, 6)], Identity, Identity, Assets, bothSides: true, clearDepth: false, toneMapScale: 1f);
        target.DrawTranslucentWorld();

        return target.PixelAt(8, 8);
    }

    /// <remarks>
    /// **The fog survives the translucent world being drawn after a model** (review). A Modulate brush run is in the
    /// translucent world (B635), so production draws it after the models, through <c>ReapplyCamera</c>, and the harness
    /// through <c>_reapplyWorldCamera</c>. That must put back the scene fog's tone-map scale (the grey takes it) and the
    /// water view's height fog: here both draws must equal the same run drawn in one go. Before the harness kept the water
    /// view, the height case drew the unfogged 94 against 67.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawTranslucentWorld_AModulateRunAfterAModelAtScaleOne_KeepsItsFogAndScale(bool height)
    {
        SceneFog? fog = height ? null : HalfRange with { MaxDensity = 1f };
        VolumeFog? heightFog = height ? HalfHeight : null;
        (int, int, int) together = Draw(PlainModulate128, 0.5f, fog: fog, heightFog: heightFog, toneMapScale: 0.5f) ?? Skip();
        (int, int, int) apart = DrawTranslucentAfterAModel(fog, heightFog, 0.5f) ?? Skip();
        (int, int, int) unfogged = Draw(PlainModulate128, 0.5f, toneMapScale: 0.5f) ?? Skip();

        // The control: the fog moves this draw, so matching it is not two unfogged surfaces agreeing.
        Math.Abs(together.Item2 - unfogged.Item2).ShouldBeGreaterThan(10, $"fogged {together} against unfogged {unfogged}");

        Near(apart, together, $"the Modulate run drew {together} in one go and {apart} after a model at scale 1");
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
