using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A VGUI `$detail` material through the HUD's own pixel shader, read back off the GPU: every `$detailblendmode`'s
/// arithmetic, with and without distance alpha.
/// </summary>
/// <remarks>
/// `#if DETAILTEXTURE` (vertexlit_and_unlit_generic_ps2x.fxc:262-279) runs `TextureCombine` for any `$detail` material
/// before `#if DISTANCEALPHA`, and `TextureCombinePostLighting` (:423-426) after the colour multiply. The expected pixel
/// is <see cref="DetailCombine"/>, the C# transcription of common_ps_fxc.h:698-766 that
/// `DetailCombineConformanceTests` holds against the header, so the shader is held against the same arithmetic. Each
/// draw is translucent over a coloured clear, so a mode that only writes alpha (9) shows in the pixel.
/// </remarks>
public sealed class VguiDetailRenderTests
{
    private const string Material = "vgui/detailtest";
    private const float BlendFactor = 0.75f;

    private static readonly byte[] BaseTexel = [153, 102, 204, 128];
    private static readonly byte[] DetailTexel = [77, 179, 230, 153];
    private static readonly (byte Red, byte Green, byte Blue, byte Alpha) VertexColour = (200, 150, 255, 255);
    private static readonly (float Red, float Green, float Blue) ClearColour = (0.2f, 0.3f, 0.1f);

    /// <remarks>
    /// The control first: the expected pixels of the twelve modes must differ from each other, or a shader that ran
    /// the wrong mode could match. Mode 10 is a no-op in `TextureCombine` (it modulates bumped lighting this shader
    /// does not have), so it is held distinct from the others but stands for "no combine".
    /// </remarks>
    [Test]
    public void Draw_EveryDetailBlendMode_MatchesTextureCombine()
    {
        Dictionary<int, (int Red, int Green, int Blue)> expected = Enumerable.Range(0, DetailCombine.HighestMode + 1)
            .ToDictionary(mode => mode, Expected);

        foreach (int mode in expected.Keys)
        {
            foreach (int other in expected.Keys.Where(other => other > mode))
            {
                Separation(expected[mode], expected[other]).ShouldBeGreaterThanOrEqualTo(
                    3, $"control: modes {mode} and {other} must predict different pixels");
            }
        }

        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        List<string> wrong = [];

        foreach ((int mode, (int Red, int Green, int Blue) predicted) in expected)
        {
            (int Red, int Green, int Blue) drawn = Draw(target, Detail(mode), distanceAlpha: null);

            if (Separation(drawn, predicted) > 1)
            {
                wrong.Add($"mode {mode}: drew {drawn}, TextureCombine gives {predicted}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    /// <remarks>
    /// `#if DISTANCEALPHA &amp;&amp; (DISTANCEALPHAFROMDETAIL == 0)` takes the mask from the base's alpha at :258, before
    /// the combine at :277; mode 8 then rewrites alpha to base × detail = 0.8 × 0.2. With no soft mask and a detail,
    /// alpha is <c>baseColor.a *= distAlphaMask &gt;= 0.5</c> (:310-312): the mask read before the combine (0.8) keeps the
    /// pixel at alpha 0.16, the one read after it (0.16) clears it to the background.
    /// </remarks>
    [Test]
    public void Draw_DistanceAlphaFromTheBase_TakesItsMaskBeforeTheCombine()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(4, 4);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        VguiDistanceAlpha hard = new(
            FromDetail: false, SoftMask: false, SoftStart: 0.5f, SoftEnd: 0.5f, ScaleEdges: false,
            Outline: false, OutlineColor: (0f, 0f, 0f), OutlineAlpha: 1f, OutlineStart0: 0f, OutlineStart1: 0f, OutlineEnd0: 0f,
            OutlineEnd1: 0f, ScaleOutline: false, Glow: false, GlowColor: (0f, 0f, 0f), GlowAlpha: 1f, GlowStart: 0f,
            GlowEnd: 0f, GlowX: 0f, GlowY: 0f);
        MapDetail detail = new(Texture(255, 255, 255, 51), (1f, 1f), 1f, DetailCombine.Multiply, (1f, 1f, 1f));
        byte[] opaqueWhite = [255, 255, 255, 204];

        (int Red, int Green, int Blue) drawn = Draw(target, detail, hard, opaqueWhite, (255, 255, 255, 255));

        // White at alpha 0.8 × 0.2 over the clear.
        float alpha = (204f / 255f) * (51f / 255f);
        (int, int, int) predicted =
            (SrgbTarget.Encode(alpha + (ClearColour.Red * (1f - alpha))),
             SrgbTarget.Encode(alpha + (ClearColour.Green * (1f - alpha))),
             SrgbTarget.Encode(alpha + (ClearColour.Blue * (1f - alpha))));

        Separation(drawn, predicted).ShouldBeLessThanOrEqualTo(1, $"drew {drawn}, predicted {predicted}");
    }

    /// <remarks>
    /// The detail's coordinates are the base's times `$detailscale` (vertexlitgeneric_dx9_helper.cpp:983-986). Pixel 1 of
    /// a sixteen-pixel quad samples u = 0.09375: inside a four-texel detail's first texel at scale 1 (clamped at its
    /// edge), exactly the centre of its second at scale 4. White, black, black, black multiplied into white shows which.
    /// Sixteen pixels square keep the scaled detail under one texel per pixel both ways, so neither scale is minified into
    /// a mip — a one-row quad spans the whole texture in V per pixel and samples the last one.
    /// </remarks>
    [TestCase(1f, 255)]
    [TestCase(4f, 0)]
    public void Draw_DetailScale_ScalesTheDetailsCoordinates(float scale, int red)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        byte[] strip = [255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        MapDetail detail = new(new MapTexture(4, 1, 4, 1, TextureImage.Rgba(strip), IsTransparent: true), (scale, scale), 1f, DetailCombine.Multiply, (1f, 1f, 1f));

        Draw(target, detail, null, [255, 255, 255, 255], (255, 255, 255, 255), width: 16, height: 16, x: 1).Red.ShouldBe(red);
    }

    /// <remarks>
    /// **An edge, pixel by pixel.** The goldens compare 30-pixel cell means, which a one-pixel outline or a reversed soft
    /// edge moves by less than their tolerance. This draws `ammo_blue_bg.vmt`'s distance alpha (its values as written —
    /// the scale flags off, which is what 1024×768 gives) over a synthetic field: a 64-texel detail whose alpha rises
    /// x/63 across the row, so pixel x reads field value x/63 and the row crosses the glow, soft edge, outline band and
    /// interior. Each pixel is predicted from `vertexlit_and_unlit_generic_ps2x.fxc:296-327` directly — outline lerp,
    /// soft mask, then the glow sampled one-thousandth of the texture to the left.
    /// </remarks>
    [Test]
    public void Draw_TheAmmoBackgroundsDistanceAlpha_MatchesTheShaderAcrossAnEdge()
    {
        const int Width = 64;

        using OffscreenTarget? target = OffscreenTarget.TryCreate(Width, Width);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        VguiDistanceAlpha ammo = new(
            FromDetail: true, SoftMask: true, SoftStart: 0.45f, SoftEnd: 0.3f, ScaleEdges: false,
            Outline: true, OutlineColor: (0.7f, 0.7f, 0.5f), OutlineAlpha: 1f,
            OutlineStart0: 0.3f, OutlineStart1: 0.49f, OutlineEnd0: 0.49f, OutlineEnd1: 0.73f, ScaleOutline: false,
            Glow: true, GlowColor: (0f, 0f, 0f), GlowAlpha: 0.45f, GlowStart: 0.03f, GlowEnd: 0.5f, GlowX: -0.001f, GlowY: -0.001f);
        byte[] field = new byte[Width * 4];

        for (int x = 0; x < Width; x++)
        {
            (field[x * 4], field[(x * 4) + 1], field[(x * 4) + 2], field[(x * 4) + 3]) = (255, 255, 255, (byte)Math.Round(x * 255.0 / (Width - 1)));
        }

        MapDetail detail = new(new MapTexture(Width, 1, Width, 1, TextureImage.Rgba(field), IsTransparent: true), (1f, 1f), 1f, DetailCombine.Multiply, (1f, 1f, 1f));

        Draw(target, detail, ammo, [255, 255, 255, 255], (255, 255, 255, 255), width: Width, height: Width);

        float[] c = ammo.Constants(1024, 768);
        float Field(int x) => field[(Math.Clamp(x, 0, Width - 1) * 4) + 3] / 255f;
        List<string> wrong = [];

        for (int x = 0; x < Width; x++)
        {
            float mask = Field(x);

            // OUTLINE (:296-300): smoothstep(c9.xyzw, c9.wzyx) — rising from start0 to start1, falling from end1 to end0.
            float outline = SmoothStep(c[16], c[19], mask) * SmoothStep(c[17], c[18], mask);
            (float r, float g, float b, float a) colour = (Lerp(1f, c[12], outline), Lerp(1f, c[13], outline), Lerp(1f, c[14], outline), Lerp(1f, c[15], outline));

            // SOFT_MASK (:303-307): smoothstep( SOFT_MASK_MIN, SOFT_MASK_MAX ) — c7.y to c7.x.
            float soft = SmoothStep(c[9], c[8], mask);
            colour.a *= soft;

            // OUTER_GLOW (:318-327): the field sampled at u + GLOW_UV_OFFSET, a 0.064-texel shift left, bilinear.
            float shifted = (Field(x - 1) * 0.064f) + (Field(x) * (1f - 0.064f));
            float glow = SmoothStep(c[2], c[3], shifted);
            colour = (Lerp(c[4] * glow, colour.r, soft), Lerp(c[5] * glow, colour.g, soft), Lerp(c[6] * glow, colour.b, soft), Lerp(c[7] * glow, colour.a, soft));

            float alpha = Math.Clamp(colour.a, 0f, 1f);
            (int, int, int) predicted =
                (SrgbTarget.Encode((colour.r * alpha) + (ClearColour.Red * (1f - alpha))),
                 SrgbTarget.Encode((colour.g * alpha) + (ClearColour.Green * (1f - alpha))),
                 SrgbTarget.Encode((colour.b * alpha) + (ClearColour.Blue * (1f - alpha))));
            (int Red, int Green, int Blue) drawn = target.PixelAt(x, Width / 2);

            if (Separation(drawn, predicted) > 2)
            {
                wrong.Add($"x {x} (field {mask:F3}): drew {drawn}, predicted {predicted}");
            }
        }

        wrong.ShouldBeEmpty();
    }

    private static float SmoothStep(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);

        return t * t * (3f - (2f * t));
    }

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);

    private static MapDetail Detail(int mode) =>
        new(Texture(DetailTexel[0], DetailTexel[1], DetailTexel[2], DetailTexel[3]), (1f, 1f), BlendFactor, mode, (1f, 1f, 1f));

    private static MapTexture Texture(byte red, byte green, byte blue, byte alpha) =>
        new(1, 1, 1, 1, TextureImage.Rgba(new[] { red, green, blue, alpha }), IsTransparent: true);

    private static (int Red, int Green, int Blue) Draw(OffscreenTarget target, MapDetail detail, VguiDistanceAlpha? distanceAlpha) =>
        Draw(target, detail, distanceAlpha, BaseTexel, VertexColour);

    private static (int Red, int Green, int Blue) Draw(
        OffscreenTarget target,
        MapDetail detail,
        VguiDistanceAlpha? distanceAlpha,
        byte[] baseTexel,
        (byte Red, byte Green, byte Blue, byte Alpha) colour,
        int width = 4,
        int height = 4,
        int x = 0)
    {
        MapTexture material = Texture(baseTexel[0], baseTexel[1], baseTexel[2], baseTexel[3]) with
        {
            Vgui = new VguiShading(detail, distanceAlpha),
        };
        VguiPanel panel = new(null, "Panel") { Wide = width, Tall = height };
        VguiLayout.InternalSolveTraverse(panel);
        VguiDrawList list = new(_ => (1, 1));

        list.PushMakeCurrent(panel, useInset: false);
        list.DrawSetColor(colour);
        // A name per draw: the renderer uploads a material once and keeps it by name.
        string name = Material + Guid.NewGuid().ToString("N");

        list.DrawSetTexture(name);
        list.DrawTexturedRect(0, 0, width, height);

        target.Clear(ClearColour.Red, ClearColour.Green, ClearColour.Blue);
        target.DrawVgui(list, asked => asked == name ? material : null);

        return target.PixelAt(x, 0);
    }

    /// <summary>The pixel `TextureCombine`, the colour multiply and `TextureCombinePostLighting` predict.</summary>
    private static (int Red, int Green, int Blue) Expected(int mode)
    {
        // The base reads through the sRGB curve; the detail too, except mod2x (vertexlitgeneric_dx9_helper.cpp:607).
        MaterialColour albedo = new(Srgb(BaseTexel[0]), Srgb(BaseTexel[1]), Srgb(BaseTexel[2]), BaseTexel[3] / 255f);
        Func<byte, float> detailRead = mode == 0 ? value => value / 255f : Srgb;
        MaterialColour detail = new(detailRead(DetailTexel[0]), detailRead(DetailTexel[1]), detailRead(DetailTexel[2]), DetailTexel[3] / 255f);

        MaterialColour combined = DetailCombine.Apply(albedo, detail, mode, BlendFactor);

        // The vertex colour is gamma, linearised by pow 2.2 (vertexlit_and_unlit_generic_vs20.fxc:238); alpha 255.
        (float Red, float Green, float Blue) lit = DetailCombine.ApplyAfterLighting(
            (combined.Red * Gamma(VertexColour.Red), combined.Green * Gamma(VertexColour.Green), combined.Blue * Gamma(VertexColour.Blue)),
            detail,
            mode,
            BlendFactor);

        // A UNORM target clamps the shader's output before blending; then SrcAlpha, InvSrcAlpha over the clear.
        float alpha = Math.Clamp(combined.Alpha, 0f, 1f);
        int Blend(float source, float clear) => SrgbTarget.Encode((Math.Clamp(source, 0f, 1f) * alpha) + (clear * (1f - alpha)));

        return (Blend(lit.Red, ClearColour.Red), Blend(lit.Green, ClearColour.Green), Blend(lit.Blue, ClearColour.Blue));
    }

    private static float Srgb(byte value) => (float)SrgbTarget.Decode(value);

    private static float Gamma(byte value) => MathF.Pow(value / 255f, 2.2f);

    private static int Separation((int Red, int Green, int Blue) a, (int Red, int Green, int Blue) b) =>
        Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
}
