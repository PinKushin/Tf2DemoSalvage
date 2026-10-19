using System.Linq;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Where a <see cref="VguiQuad"/> lands in clip space, and which alpha each corner carries.</summary>
public sealed class VguiRendererTests
{
    [Test]
    public void BuildVertices_AQuadInTheTopLeftQuarter_SpansMinusOneToZeroAndCarriesItsCornerAlphas()
    {
        VguiQuad quad = new(null, 0f, 0f, 50f, 25f, 0f, 0f, 1f, 1f, 255, 0, 0, 10, 20, 30, 40);

        float[] data = VguiRenderer.BuildVertices([quad], 100, 50);

        // (tl, tr, br) and (tl, br, bl), eight floats each: x y s t r g b a.
        data.Length.ShouldBe(6 * 8);
        (data[0], data[1], data[7]).ShouldBe((-1f, 1f, 10f / 255f), "top-left");
        (data[8], data[9], data[15]).ShouldBe((0f, 1f, 20f / 255f), "top-right");
        (data[16], data[17], data[23]).ShouldBe((0f, 0f, 30f / 255f), "bottom-right");
        (data[40], data[41], data[47]).ShouldBe((-1f, 0f, 40f / 255f), "bottom-left");
        data[4].ShouldBe(1f, "red");
    }

    /// <remarks>
    /// `o.color.rgb = GammaToLinear( v.vColor.rgb )` (vertexlit_and_unlit_generic_vs20.fxc:238), `pow( x, 2.2 )`
    /// (common_fxc.h), before the frame's sRGB write. Measured against TF2 on cp_process_f12, 2026-10-17: TanLight
    /// (235 226 202) text reads back 236 227 203 — what pow 2.2 then the sRGB curve gives — where ours, written straight
    /// to the sRGB view, read 246 242 230.
    /// </remarks>
    [Test]
    public void BuildVertices_AGammaColour_IsLinearisedByPowTwoPointTwo()
    {
        VguiQuad quad = new(null, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 235, 128, 0, 255, 255, 255, 255);

        float[] data = VguiRenderer.BuildVertices([quad], 1, 1);

        data[4].ShouldBe(System.MathF.Pow(235f / 255f, 2.2f), 1e-6f);
        data[5].ShouldBe(System.MathF.Pow(128f / 255f, 2.2f), 1e-6f);
        data[6].ShouldBe(0f);
        data[7].ShouldBe(1f, "alpha is not converted (:239)");
    }

    [Test]
    public void ShadingConstants_NoShading_AreZeroButAUnitDetailScale()
    {
        float[] data = VguiRenderer.ShadingConstants(null, 1280, 720);

        data[..28].ShouldAllBe(value => value == 0f);
        data[28..].ShouldBe([1f, 1f, 0f, 0f]);
    }

    [Test]
    public void ShadingConstants_TheAmmoBackground_PacksC5ToC9ThenItsSwitches()
    {
        VguiDistanceAlpha shape = new(
            FromDetail: true,
            SoftMask: true, SoftStart: 0.45f, SoftEnd: 0.3f, ScaleEdges: false,
            Outline: true, OutlineColor: (0.7f, 0.7f, 0.5f), OutlineAlpha: 1f,
            OutlineStart0: 0.3f, OutlineStart1: 0.49f, OutlineEnd0: 0.49f, OutlineEnd1: 0.73f, ScaleOutline: false,
            Glow: false, GlowColor: (0f, 0f, 0f), GlowAlpha: 0.45f, GlowStart: 0.03f, GlowEnd: 0.5f, GlowX: -0.001f, GlowY: -0.001f);

        float[] data = VguiRenderer.ShadingConstants(new VguiShading(null, shape), 1024, 768);

        data[..20].ShouldBe(shape.Constants(1024, 768));
        data[20..].ShouldBe([1f, 1f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f, 0f], "distance alpha, soft, outline, no glow; no detail texture, from detail");
    }

    /// <remarks>A `$detail` with no `$distancealpha`: the combine's switch, mode, factor and scale, and no distance alpha.</remarks>
    [Test]
    public void ShadingConstants_ADetailWithoutDistanceAlpha_PacksTheDetailAlone()
    {
        Tf2DemoSalvage.Content.Assets.TextureImage texel = Tf2DemoSalvage.Content.Assets.TextureImage.Rgba(new byte[4]);
        MapDetail detail = new(new MapTexture(1, 1, 1, 1, texel, IsTransparent: true), (4f, 2f), 0.5f, 5, (1f, 1f, 1f));

        float[] data = VguiRenderer.ShadingConstants(new VguiShading(detail, null), 1024, 768);

        data[..24].ShouldAllBe(value => value == 0f);
        data[24..].ShouldBe([1f, 0f, 5f, 0.5f, 4f, 2f, 0f, 0f]);
    }

    /// <remarks>The packer and the HLSL `Shading` cbuffer must agree on its size, or the upload over- or under-runs it.</remarks>
    [Test]
    public void ShadingConstants_TheirCount_IsTheShadersCbufferSize()
    {
        string source = VguiRenderer.ShaderSourceText;
        int open = source.IndexOf("cbuffer Shading", System.StringComparison.Ordinal);
        string body = source[open..source.IndexOf("};", open, System.StringComparison.Ordinal)];
        int float4s = body.Split('\n').Count(line => line.TrimStart().StartsWith("float4 ", System.StringComparison.Ordinal));

        (float4s * 4).ShouldBe(VguiRenderer.ConstantFloats);
        VguiRenderer.ShadingConstants(null, 1, 1).Length.ShouldBe(VguiRenderer.ConstantFloats);
    }

    [Test]
    public void Steps_AModelBetweenQuadsOfOneTexture_SplitsTheRunAtItsPlaceInPaintOrder()
    {
        // Three quads of one texture would be one run; a 3D paint recorded after the first cuts it in two around itself.
        VguiQuad quad = new("t", 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 255, 255, 255, 255, 255, 255, 255);
        VguiModelDraw panel = new(1, 0, 0, 10, 10, new float[16], []);

        VguiRenderer.Steps([quad, quad, quad], [panel]).ShouldBe([(-1, 0, 1), (0, 0, 0), (-1, 1, 3)]);
    }

    [Test]
    public void Steps_AModelAfterEveryQuad_ComesLast()
    {
        VguiQuad quad = new("t", 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 255, 255, 255, 255, 255, 255, 255);

        VguiRenderer.Steps([quad], [new VguiModelDraw(1, 0, 0, 10, 10, new float[16], [])]).ShouldBe([(-1, 0, 1), (0, 0, 0)]);
    }

    [Test]
    public void Steps_OnlyAModel_DrawsIt() =>
        VguiRenderer.Steps([], [new VguiModelDraw(0, 0, 0, 10, 10, new float[16], [])]).ShouldBe([(0, 0, 0)]);
}
