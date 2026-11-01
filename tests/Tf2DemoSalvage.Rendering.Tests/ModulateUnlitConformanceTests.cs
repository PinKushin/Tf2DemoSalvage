using System.Collections.Generic;

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
    /// **The world route for the same neutral texel** (B639 review): <c>combine.w</c> is the material's, so every route reads
    /// the one diffuse, but the brush route had no test at 128, where only 2^2.2/2 is neutral. At 2.0 it draws 177.
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

    private const int UnlitGeneric = 0;

    /// <summary>One opaque UnlitGeneric of texel 100, far from the 188 surface and from grey's 128.</summary>
    private static readonly MapAssets Unlit = MapAssets.ForMaterials([ModulateToneMapConformanceTests.Grey(100)], ["UnlitGeneric"]);

    /// <summary>188 · 128/255 ≈ 94 in gamma; the linear blend lands within three levels of it.</summary>
    private static void PlainModulateIsHalf((int, int, int) under, (int Red, int Green, int Blue) drawn, string where)
    {
        under.Item2.ShouldBe(188, "the control: the cleared surface reads 188");
        drawn.Green.ShouldBeInRange(91, 97, $"a plain Modulate of 128 {where} drew {drawn} over {under}; Valve's arithmetic 94");
    }

    /// <remarks>
    /// ±2 levels. **For the neutral texel this has about one level of headroom, deliberately.** The sRGB curve puts
    /// 0.5 · 4.595 · sRGB⁻¹(128/255) at 186.8 against the surface's 187.5 (*arithmetic*). 2^2.2/2 is exact only on the 2.2
    /// curve. The B639 fault drew 177, far outside.
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

        return target.PixelAt(8, 8);
    }

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
