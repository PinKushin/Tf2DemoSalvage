using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A <c>DecalModulate</c> draw is not scaled by the tone map (B635).</summary>
/// <remarks>
/// **What Valve's shader does, quoted** (<c>decalmodulate_ps2x.fxc:35-44</c>):
///
/// <code>
/// float4 result = tex2D( TexSampler, i.baseTexCoord );
/// …
/// return FinalOutput( result, fogFactor, PIXELFOGTYPE, TONEMAP_SCALE_NONE );
/// </code>
///
/// and <c>FinalOutput</c> under <c>TONEMAP_SCALE_NONE</c> is <c>result.rgb = vShaderColor.rgb</c>
/// (<c>common_ps_fxc.h:356-358</c>), where every lit material takes <c>TONEMAP_SCALE_LINEAR</c>, the colour times
/// <c>LINEAR_LIGHT_SCALE</c> (<c>:348-350</c>). The blend is <c>BlendFunc( DST_COLOR, SRC_COLOR )</c>
/// (<c>DecalModulate_dx9.cpp:78</c>), so the framebuffer becomes <c>dst · 2·src</c> and a texel of 128 leaves it alone
/// under any exposure. <c>Modulate</c> is the same (<c>modulate_ps2x.fxc:37</c>).
///
/// **The defect this pins** (measured, `docs/measurements/2026-10-31-explosive-decals-vs-tf2.md`): the scale reached the
/// decal, so under an exposure below one the neutral surround of every scorch's 256-texel square darkened the floor and
/// the squares showed as straight-edged blocks.
///
/// **The subject is the shipped atlas** (<c>decals/decals_mod2x</c>, the material every TF2 scorch and bullet hole draws),
/// sampled at one point per draw: all six corners carry the same texture coordinate, so no filter mixes texels. The surface
/// below is a cleared target, so the decal is the only thing the exposure could reach.
/// </remarks>
[NonParallelizable]
public sealed class DecalModulateConformanceTests
{
    /// <summary>The atlas, as the decal pass draws it.</summary>
    private const string Atlas = "decals/decals_mod2x";

    /// <summary>Loaded at 256 so the 1024x512 atlas keeps its scorch windows 64 texels across.</summary>
    private const int TextureSize = 256;

    /// <summary>A texel in <c>scorch1_subrect</c>'s corner (<c>$Pos 512 256</c>), outside its alpha mask: grey 128.</summary>
    /// <remarks>Window texel (8, 8) at a quarter size; the texel-png probe measured the full-size corner's mean at
    /// 127.9 127.7 127.8.</remarks>
    private static readonly (float U, float V) Neutral = ((128f + 8f + 0.5f) / 256f, (64f + 8f + 0.5f) / 128f);

    /// <summary>The middle of <c>scorch1_subrect</c>, its darkest region.</summary>
    private static readonly (float U, float V) Scorch = ((128f + 32f + 0.5f) / 256f, (64f + 32f + 0.5f) / 128f);

    /// <summary>The grey the surface is cleared to, in the target's own terms.</summary>
    private const float Surface = 0.5f;

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void Draw_NeutralTexelUnderAnyToneMapScale_LeavesTheSurfaceUnchanged(float scale)
    {
        ((int Red, int Green, int Blue) under, (int Red, int Green, int Blue) drawn) = DrawOnce(Neutral, scale) ?? Skip();

        // dst · 2 · 128/255 is dst · 1.004 in Valve's gamma blend; ours blends in linear, within two levels of that.
        string moved = $"at scale {scale} the neutral texel moved the surface from {under} to {drawn}";

        drawn.Red.ShouldBeInRange(under.Red - 2, under.Red + 2, moved);
        drawn.Green.ShouldBeInRange(under.Green - 2, under.Green + 2, moved);
        drawn.Blue.ShouldBeInRange(under.Blue - 2, under.Blue + 2, moved);
    }

    [Test]
    public void Draw_ScorchTexelUnderHalfAndUnitToneMapScale_DarkensTheSurfaceTheSameAmount()
    {
        ((int Red, int Green, int Blue) under, (int Red, int Green, int Blue) atOne) = DrawOnce(Scorch, 1f) ?? Skip();
        (_, (int Red, int Green, int Blue) atHalf) = DrawOnce(Scorch, 0.5f) ?? Skip();

        // The control: the decal draws at all, so the neutral test above can fail.
        atOne.Green.ShouldBeLessThan(under.Green - 40, $"the scorch's centre must darken the surface, or nothing drew: {under} became {atOne}");

        string moved = $"the scorch drew {atOne} at scale 1 and {atHalf} at scale 0.5, over {under}";

        atHalf.Red.ShouldBeInRange(atOne.Red - 2, atOne.Red + 2, moved);
        atHalf.Green.ShouldBeInRange(atOne.Green - 2, atOne.Green + 2, moved);
        atHalf.Blue.ShouldBeInRange(atOne.Blue - 2, atOne.Blue + 2, moved);
    }

    /// <summary>The cleared surface's centre pixel, then the same pixel with one decal quad drawn over it.</summary>
    private static ((int, int, int) Under, (int, int, int) Drawn)? DrawOnce((float U, float V) texel, float scale)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null || !MapCache.Exists(MapCache.DefaultMap))
        {
            return null;
        }

        MapAssets assets = MapCache.Load(TextureSize, decalMaterials: [Atlas]);

        assets.DecalMaterials.TryGetValue(Atlas, out int material).ShouldBeTrue("the control: the atlas loaded into the table");
        assets.Shaders[material].ShouldBe("DecalModulate", StringCompareShould.IgnoreCase);

        target.Clear(Surface, Surface, Surface);

        (int, int, int) under = target.PixelAt(8, 8);

        // The corners an unlit decal carries (DecalMesh): the lightmap atlas's reserved white texel and the mod2x light.
        float light = Device3D.DecalModulateLight;
        List<WorldVertex> quad =
        [
            new(-64f, 0f, -64f, texel.U, texel.V, 0f, 0f, 0f, light, light, light),
            new(64f, 0f, 64f, texel.U, texel.V, 0f, 0f, 0f, light, light, light),
            new(64f, 0f, -64f, texel.U, texel.V, 0f, 0f, 0f, light, light, light),
            new(-64f, 0f, -64f, texel.U, texel.V, 0f, 0f, 0f, light, light, light),
            new(-64f, 0f, 64f, texel.U, texel.V, 0f, 0f, 0f, light, light, light),
            new(64f, 0f, 64f, texel.U, texel.V, 0f, 0f, 0f, light, light, light),
        ];

        target.WorldToneMapScale = scale;
        // One EMPTY world run: the world pass returns before its decals when it has no runs at all (WorldRenderer.Draw).
        target.DrawWorld(
            quad,
            [new WorldBatch(material, 0, 0)],
            new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix(),
            assets,
            decals: [new WorldBatch(material, 0, 6, Category: SurfaceCategory.Overlay)]);

        return (under, target.PixelAt(8, 8));
    }

    private static ((int, int, int), (int, int, int)) Skip()
    {
        Assert.Ignore("no Direct3D, or the game or the map is not installed");
        return default;
    }
}
