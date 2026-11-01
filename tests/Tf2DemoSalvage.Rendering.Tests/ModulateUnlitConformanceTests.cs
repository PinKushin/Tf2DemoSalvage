using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A modulating material reads no light, wherever it is drawn (B639, B638): hand-built materials, no game install.
/// </summary>
/// <remarks>
/// **Valve, quoted.** `DecalModulate` sets `STATIC_LIGHT_VERTEX 0`, `STATIC_LIGHT_LIGHTMAP 0`, `DYNAMIC_LIGHT 0`,
/// `NUM_LIGHTS 0` and `VERTEXCOLOR false` (<c>DecalModulate_dx9.cpp:89</c>, <c>:184-191</c>), and its pixel shader is
/// the texel alone, <c>result = tex2D( TexSampler, i.baseTexCoord )</c> (<c>decalmodulate_ps2x.fxc:35</c>). `Modulate`
/// draws through <c>unlitgeneric_vs20</c> (<c>modulate_dx9.cpp:186</c>). Both read the texture without sRGB and write
/// without it (<c>DecalModulate_dx9.cpp:74-75</c>), so the blend multiplies GAMMA values: `DST_COLOR, SRC_COLOR` gives
/// <c>dst · 2s</c> for DecalModulate and `$mod2x` (<c>:78</c>, <c>modulate_dx9.cpp:141-148</c>), `DST_COLOR, ZERO` gives
/// <c>dst · s</c> for plain Modulate.
///
/// **Arithmetic, on a surface cleared to 188:** a DecalModulate texel of 128 gives 188 · 2 · 128/255 ≈ 189; a plain
/// Modulate texel of 128 gives 188 · 128/255 ≈ 94.
///
/// **What it was:** the brush entity's (`func_brush`) DecalModulate face took the model pass's lightmap light, the
/// atlas's white texel at 2.0, so the neutral 128 multiplied the floor by about 0.86 and drew 175, a visible darker
/// square around every `ctf_crasher` scorch. TF2 itself, live with `ent_fire gatescorch_red enable`, leaves the alpha-0
/// surround exactly unchanged (B639). Plain Modulate took the same 2.0 and drew 129 where Valve draws 94 (B638).
/// </remarks>
[NonParallelizable]
public sealed class ModulateUnlitConformanceTests
{
    [Test]
    public void DrawModel_DecalModulateBrushEntityNeutralTexel_LeavesTheSurfaceUnchanged()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) dark = DrawModel(ModulateToneMapConformanceTests.DecalModulateDark) ?? Skip();
        (int, int, int) neutral = DrawModel(ModulateToneMapConformanceTests.DecalModulateNeutral) ?? Skip();

        // The control: the same entity with a dark texel darkens the surface, so the entity draws.
        dark.Item2.ShouldBeLessThan(under.Item2 - 40, $"the dark scorch entity drew {dark} over {under}: nothing drew");

        Close(neutral, under, $"a DecalModulate brush entity of texel 128 moved the surface from {under} to {neutral}");
    }

    [Test]
    public void DrawModel_PlainModulateTexel128_HalvesTheSurfaceInGamma()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) drawn = DrawModel(ModulateToneMapConformanceTests.PlainModulate) ?? Skip();

        PlainModulateIsHalf(under, drawn, "on a model");
    }

    [Test]
    public void DrawWorld_PlainModulateTexel128_HalvesTheSurfaceInGamma()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Skip();
            return;
        }

        target.Clear(ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface);
        target.DrawWorld(
            ModulateToneMapConformanceTests.Quad(1f),
            [new WorldBatch(ModulateToneMapConformanceTests.PlainModulate, 0, 6)],
            ModulateToneMapConformanceTests.Camera(),
            ModulateToneMapConformanceTests.Assets,
            toneMapScale: 1f);

        PlainModulateIsHalf(under, target.PixelAt(8, 8), "on brushwork");
    }

    /// <remarks>
    /// **`mat_fullbright 1` substitutes the LIGHTMAP** (`TEXTURE_LIGHTMAP_FULLBRIGHT`, BaseVSShader.cpp:1094), which an unlit
    /// shader never samples, so it cannot move a modulating material. Ours forced the light to the overbright 2.0 after the
    /// unlit branch, which would undo it.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateNeutralTexelUnderFullbright_LeavesTheSurfaceUnchanged()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) dark = DrawModel(ModulateToneMapConformanceTests.DecalModulateDark, Fullbright.NoLighting) ?? Skip();
        (int, int, int) neutral = DrawModel(ModulateToneMapConformanceTests.DecalModulateNeutral, Fullbright.NoLighting) ?? Skip();

        dark.Item2.ShouldBeLessThan(under.Item2 - 40, $"the dark scorch under fullbright drew {dark} over {under}: nothing drew");

        Close(neutral, under, $"under mat_fullbright 1 a DecalModulate texel of 128 moved the surface from {under} to {neutral}");
    }

    /// <remarks>
    /// **The world route for the same neutral texel** (B639 review): the brush route had no test at 128. Since B641 the blend
    /// runs on stored values, so 128 is neutral by Valve's own arithmetic (188 · 2 · 128/255 = 188.7). The B639 fault, the
    /// model pass's lightmap light, drew 177.
    /// </remarks>
    [Test]
    public void DrawWorld_DecalModulateNeutralTexel_LeavesTheSurfaceUnchanged()
    {
        float surface = ModulateToneMapConformanceTests.Surface;
        (int, int, int) under = Under(surface) ?? Skip();
        (int, int, int) dark = DrawWorld(ModulateToneMapConformanceTests.DecalModulateDark) ?? Skip();
        (int, int, int) neutral = DrawWorld(ModulateToneMapConformanceTests.DecalModulateNeutral) ?? Skip();

        dark.Item2.ShouldBeLessThan(under.Item2 - 40, $"the dark scorch brush drew {dark} over {under}: nothing drew");

        Close(neutral, under, $"a DecalModulate brush face of texel 128 moved the surface from {under} to {neutral}");
    }

    /// <remarks>
    /// **An unlit shader reads no lightmap, so `mat_fullbright 1` cannot move it.** UnlitGeneric's diffuse is 1 because
    /// `hasDiffuseLighting = bVertexLitGeneric` (vertexlitgeneric_dx9_helper.cpp:394) and the pixel shader keeps
    /// <c>diffuseLighting = float3( 1.0f, 1.0f, 1.0f )</c> (vertexlit_and_unlit_generic_ps2x.fxc:344-348). *Read:* no
    /// UnlitGeneric path binds a lightmap. *Unread:* where `mat_fullbright 1` swaps in `TEXTURE_LIGHTMAP_FULLBRIGHT` is the
    /// closed material system. Before B639 ours forced every material's light to 2.0, which drew this one at 138.
    /// </remarks>
    [Test]
    public void DrawModel_UnlitGenericUnderFullbright_DrawsAsWithout()
    {
        (int, int, int) off = DrawModel(UnlitGeneric, Fullbright.Off, Unlit) ?? Skip();
        (int, int, int) fullbright = DrawModel(UnlitGeneric, Fullbright.NoLighting, Unlit) ?? Skip();

        // The control: the material covers the 188 surface, so the value below is its own.
        off.Item2.ShouldBeInRange(97, 103, $"UnlitGeneric of texel 100 drew {off}, not its own texel");

        Close(fullbright, off, $"UnlitGeneric drew {off} and {fullbright} under mat_fullbright 1");
    }

    /// <remarks>
    /// **`mat_fullbright 2` greys the albedo only where a shader asks it to.** LightmappedGeneric and VertexLitGeneric bind
    /// `TEXTURE_GREY` in its place (lightmappedgeneric_dx9_helper.cpp:763-774; vertexlitgeneric_dx9_helper.cpp:1188, gated
    /// on `bVertexLitGeneric`, so UnlitGeneric keeps its texture). DecalModulate binds its base texture unconditionally
    /// (DecalModulate_dx9.cpp:161) and sets `MATERIAL_VAR_NO_DEBUG_OVERRIDE` (:43), and modulate_dx9.cpp never reads the
    /// cvar. Ours greyed every albedo, so under the B639 diffuse a scorch went neutral and vanished.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateDarkTexelUnderLightingOnly_DrawsAsWithout()
    {
        (int, int, int) off = DrawModel(ModulateToneMapConformanceTests.DecalModulateDark) ?? Skip();
        (int, int, int) lightingOnly = DrawModel(ModulateToneMapConformanceTests.DecalModulateDark, Fullbright.LightingOnly) ?? Skip();

        off.Item2.ShouldBeLessThan(188 - 40, $"the dark scorch drew {off}: nothing drew");

        Close(lightingOnly, off, $"the dark scorch drew {off} and {lightingOnly} under mat_fullbright 2");
    }

    [Test]
    public void DrawModel_UnlitGenericUnderLightingOnly_DrawsAsWithout()
    {
        (int, int, int) off = DrawModel(UnlitGeneric, Fullbright.Off, Unlit) ?? Skip();
        (int, int, int) lightingOnly = DrawModel(UnlitGeneric, Fullbright.LightingOnly, Unlit) ?? Skip();

        off.Item2.ShouldBeInRange(97, 103, $"UnlitGeneric of texel 100 drew {off}, not its own texel");

        Close(lightingOnly, off, $"UnlitGeneric drew {off} and {lightingOnly} under mat_fullbright 2");
    }

    /// <remarks>
    /// **`mat_drawflat` does not reach DecalModulate.** The shader sets `MATERIAL_VAR_NO_DEBUG_OVERRIDE`
    /// (DecalModulate_dx9.cpp:43), the flag that exempts a material from the debug views (imaterial.h:356). Ours replaced
    /// its albedo with white. Under the B639 diffuse the mod2x output then clamps to 1 and doubles the floor where a dark
    /// scorch should be. *Unread:* what the closed shader system draws for plain Modulate, which does not set the flag;
    /// see the shader's note.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateDarkTexelUnderDrawFlat_DrawsAsWithout()
    {
        (int, int, int) off = DrawModel(ModulateToneMapConformanceTests.DecalModulateDark) ?? Skip();
        (int, int, int) flat = DrawModel(ModulateToneMapConformanceTests.DecalModulateDark, debug: new DebugModes { DrawFlat = true }) ?? Skip();

        off.Item2.ShouldBeLessThan(188 - 40, $"the dark scorch drew {off}: nothing drew");

        Close(flat, off, $"the dark scorch drew {off} and {flat} under mat_drawflat");
    }

    /// <remarks>
    /// **DecalModulate alpha-tests, GREATER 0** (`EnableAlphaTest( true )`, `AlphaFunc( SHADER_ALPHAFUNC_GREATER, 0.0f )`,
    /// DecalModulate_dx9.cpp:64-65), so an alpha-0 texel is discarded whatever its colour. A scorch's surround is alpha 0
    /// with RGB 120 to 131 (`texel-png` on `decals/scorch1`). Once the blend became exact (B641) that surround stored
    /// 1.4% bright in a real frame, where TF2 leaves it at exactly 1.000. Texel 140 at alpha 0 would draw 206 over 188;
    /// Valve draws nothing.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateTexelOfAlphaZero_IsDiscarded()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) opaque = DrawModel(BrightOpaque, assets: AlphaPair) ?? Skip();
        (int, int, int) clear = DrawModel(BrightClear, assets: AlphaPair) ?? Skip();

        // The control: the same texel at alpha 255 draws, and brightens the surface.
        opaque.Item2.ShouldBeGreaterThan(under.Item2 + 10, $"the opaque texel drew {opaque} over {under}: nothing drew");

        Close(clear, under, $"an alpha-0 DecalModulate texel moved the surface from {under} to {clear}");
    }

    /// <remarks>
    /// **The override is unconditional** (B641 review). DecalModulate's shadow state sets its own alpha test, GREATER 0
    /// (DecalModulate_dx9.cpp:64-65), whatever the VMT's `$alphatest` says. A texture flagged alpha-tested took the VMT
    /// branch's 0.5 cutoff first, so a dark texel at alpha 64 (0.25) was discarded where Valve draws it.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateFlaggedAlphaTestedAtAlpha64_StillDraws()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) drawn = DrawModel(DarkAlphaTested, assets: AlphaPair) ?? Skip();

        // Valve's arithmetic: 188 · 2 · 32/255 = 47.2. Discarded, the surface would stay at 188.
        drawn.Item2.ShouldBeInRange(46, 48, $"a dark DecalModulate texel at alpha 64, flagged alpha-tested, drew {drawn} over {under}");
    }

    /// <remarks>
    /// **The texture is filtered in gamma, as Valve's sampler filters it** (B641 review). `EnableSRGBRead( SHADER_SAMPLER0,
    /// false )` (DecalModulate_dx9.cpp:74): bilinear filtering averages the STORED texels. Halfway between a black and a
    /// white column that is 127.5, so mod2x leaves the surface at 188. Filtered in linear and re-encoded, the same point is
    /// 0.735, doubled to 1.47, and the surface goes to 255. A texel centre cannot tell the two apart; a midpoint can.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateBetweenABlackAndAWhiteTexel_FiltersInGamma()
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) drawn = DrawModel(BlackWhite, assets: AlphaPair) ?? Skip();

        Close(drawn, under, $"halfway between black and white texels the scorch drew {drawn} over {under}");
    }

    private const int BrightOpaque = 0;
    private const int BrightClear = 1;
    private const int DarkAlphaTested = 2;
    private const int BlackWhite = 3;

    /// <summary>
    /// DecalModulate materials: RGB 140 at alpha 255 and at alpha 0; RGB 32 at alpha 64 flagged alpha-tested; and a 2x2 of
    /// a black column beside a white one, which the quad's U of 0.5 samples halfway between.
    /// </summary>
    private static readonly MapAssets AlphaPair = MapAssets.ForMaterials(
        [DecalTexel(140, 255), DecalTexel(140, 0), DecalTexel(32, 64, alphaTested: true), BlackBesideWhite()],
        ["DecalModulate", "DecalModulate", "DecalModulate", "DecalModulate"]);

    private static MapTexture BlackBesideWhite() =>
        new(
            2,
            2,
            2,
            2,
            TextureImage.Rgba(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255 }),
            IsTransparent: false,
            IsAdditive: false,
            IsTranslucent: true,
            IsModulate: true,
            IsModulateTwice: true,
            IsNoCull: false,
            IsDecal: true);

    private static MapTexture DecalTexel(byte value, byte alpha, bool alphaTested = false) =>
        new(
            4,
            4,
            4,
            4,
            TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { value, value, value, alpha }).ToArray()),
            IsTransparent: alphaTested,
            IsAdditive: false,
            IsTranslucent: !alphaTested,
            IsModulate: true,
            IsModulateTwice: true,
            IsNoCull: false,
            IsDecal: true);

    private const int UnlitGeneric = 0;

    /// <summary>One opaque UnlitGeneric of texel 100, far from the 188 surface and from grey's 128.</summary>
    private static readonly MapAssets Unlit = MapAssets.ForMaterials([ModulateToneMapConformanceTests.Grey(100)], ["UnlitGeneric"]);

    /// <remarks>
    /// **Valve's exact gamma product, on every route** (B641). The blend multiplies STORED values, sRGB read and write off
    /// (DecalModulate_dx9.cpp:74-78): over a stored 188, a DecalModulate texel of 128 gives 188 · 2 · 128/255 = 188.7 and
    /// one of 32 gives 47.2. A linear blend drew 187 and 51, 2 to 6 levels off and worse toward black.
    /// </remarks>
    [TestCase(ModulateToneMapConformanceTests.DecalModulateNeutral, 189)]
    [TestCase(ModulateToneMapConformanceTests.DecalModulateDark, 47)]
    public void Draw_DecalModulateOverAStored188_IsValvesGammaProductOnEveryRoute(int material, int expected)
    {
        (int, int, int) under = Under(ModulateToneMapConformanceTests.Surface) ?? Skip();
        (int, int, int) model = DrawModel(material) ?? Skip();
        (int, int, int) world = DrawWorld(material) ?? Skip();
        (int, int, int) decal = DrawDecal(material) ?? Skip();

        under.Item2.ShouldBe(188, "the control: the cleared surface reads 188");
        model.Item2.ShouldBeInRange(expected - 1, expected + 1, $"on a model it drew {model}; Valve's {expected}");
        world.Item2.ShouldBeInRange(expected - 1, expected + 1, $"on brushwork it drew {world}; Valve's {expected}");
        decal.Item2.ShouldBeInRange(expected - 1, expected + 1, $"as a decal it drew {decal}; Valve's {expected}");
    }

    /// <summary>188 · 128/255 = 94.4 in gamma, which an exact gamma blend reproduces to a level (B641).</summary>
    private static void PlainModulateIsHalf((int, int, int) under, (int Red, int Green, int Blue) drawn, string where)
    {
        under.Item2.ShouldBe(188, "the control: the cleared surface reads 188");
        drawn.Green.ShouldBeInRange(93, 95, $"a plain Modulate of 128 {where} drew {drawn} over {under}; Valve's arithmetic 94");
    }

    /// <summary>One quad as a decal in the overlay pass, with one empty world run (the world pass returns with none).</summary>
    private static (int, int, int)? DrawDecal(int material)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        float surface = ModulateToneMapConformanceTests.Surface;

        target.Clear(surface, surface, surface);
        target.DrawWorld(
            ModulateToneMapConformanceTests.Quad(1f),
            [new WorldBatch(material, 0, 0)],
            ModulateToneMapConformanceTests.Camera(),
            ModulateToneMapConformanceTests.Assets,
            decals: [new WorldBatch(material, 0, 6, Category: SurfaceCategory.Overlay)],

            // The overlay pass alone: the translucent world after it would close a run the overlay list left open.
            translucent: false,
            toneMapScale: 1f);

        DrewThroughTheTwin(target);

        return target.PixelAt(8, 8);
    }

    /// <remarks>
    /// ±2 levels. Since B641 the neutral texel is exact: 188 · 2 · 128/255 = 188.7 stored. The B639 fault drew 177, far
    /// outside.
    /// </remarks>
    private static void Close((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected, string because)
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
    /// One quad of a material as a brush entity reaches the screen: the model pass, white corners, lightmap coordinates on
    /// the atlas's reserved white texel, no ambient cube.
    /// </summary>
    private static (int, int, int)? DrawModel(
        int material, Fullbright fullbright = Fullbright.Off, MapAssets? assets = null, DebugModes debug = default)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        float surface = ModulateToneMapConformanceTests.Surface;

        target.Clear(surface, surface, surface);

        List<WorldVertex> quad = ModulateToneMapConformanceTests.Quad(1f);

        target.DrawModelPose(
            quad,
            [new WorldBatch(material, 0, 6)],
            ModulateToneMapConformanceTests.Camera(),
            ModulateToneMapConformanceTests.Identity,
            assets ?? ModulateToneMapConformanceTests.Assets,
            bothSides: true,
            fullbright: fullbright,
            debug: debug,
            toneMapScale: 1f);

        DrewThroughTheTwin(target);

        return target.PixelAt(8, 8);
    }

    /// <summary>One quad as world brushwork: the opaque pass, then the translucent world a blended run belongs to.</summary>
    private static (int, int, int)? DrawWorld(int material)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        float surface = ModulateToneMapConformanceTests.Surface;

        target.Clear(surface, surface, surface);
        target.DrawWorld(
            ModulateToneMapConformanceTests.Quad(1f),
            [new WorldBatch(material, 0, 6)],
            ModulateToneMapConformanceTests.Camera(),
            ModulateToneMapConformanceTests.Assets,
            toneMapScale: 1f);

        DrewThroughTheTwin(target);

        return target.PixelAt(8, 8);
    }

    /// <summary>
    /// After a draw: every modulating run found its target's gamma twin, and the run is closed, so nothing drawn later
    /// lands in the twin (B641 review).
    /// </summary>
    private static void DrewThroughTheTwin(OffscreenTarget target)
    {
        target.MissingGammaTwins.ShouldBe(0, "every modulating run drew through the target's gamma twin (B641)");
        target.GammaRunOpen.ShouldBeFalse("the draw closed its gamma run");
    }

    /// <remarks>
    /// **A target with no twin draws the linear blend, not a gamma value through an sRGB view** (B641 review). The shader
    /// decodes the stored texel to linear whenever the twin is NOT bound (`gammaOutput`). Texel 32 over 188 is then
    /// 0.5 · 2 · sRGB⁻¹(32/255), about 32 stored (*arithmetic*), against Valve's 47. Left in gamma and written through the
    /// sRGB view, it would be about 99. The miss is counted.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateOnATargetWithNoTwin_DrawsTheLinearBlendAndIsCounted()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Skip();
            return;
        }

        float surface = ModulateToneMapConformanceTests.Surface;

        target.Clear(surface, surface, surface);
        target.ForgetGammaTwin();
        target.DrawModelPose(
            ModulateToneMapConformanceTests.Quad(1f),
            [new WorldBatch(ModulateToneMapConformanceTests.DecalModulateDark, 0, 6)],
            ModulateToneMapConformanceTests.Camera(),
            ModulateToneMapConformanceTests.Identity,
            ModulateToneMapConformanceTests.Assets,
            bothSides: true,
            toneMapScale: 1f);

        (int Red, int Green, int Blue) drawn = target.PixelAt(8, 8);

        target.MissingGammaTwins.ShouldBe(1, "the one modulating run found no twin");
        target.GammaRunOpen.ShouldBeFalse("no run was opened");
        drawn.Green.ShouldBeInRange(28, 36, $"with no twin the dark texel drew {drawn}: the linear blend is about 32, a double encode about 99");
    }

    /// <remarks>
    /// **Replacing a target leaks no twin and leaves no stale entry** (B641 review). The water targets are made on the first
    /// read, each with a gamma twin. Released and made again, the renderer must hold the same two owned twins, and map the
    /// same three views (two water, plus this target's own), not five.
    /// </remarks>
    [Test]
    public void ReleaseWaterTargets_ThenMadeAgain_LeaksNoGammaTwin()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Skip();
            return;
        }

        target.WaterTargetPixel(refraction: true, 0, 0);

        (int Owned, int Mapped) first = target.GammaTwinCounts;

        first.ShouldBe((2, 3), "the control: two water twins owned, mapped beside the target's own");

        target.ReleaseWaterTargets();
        target.GammaTwinCounts.ShouldBe((0, 1), "released: only the target's own twin is mapped");

        target.WaterTargetPixel(refraction: true, 0, 0);
        target.GammaTwinCounts.ShouldBe(first, "made again, the counts are the first ones");
    }

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
