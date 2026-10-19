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
