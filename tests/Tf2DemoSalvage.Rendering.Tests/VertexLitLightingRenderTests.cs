using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A VertexLitGeneric material without a bump map, a diffuse warp or the skin shader is lit per VERTEX (B722).</summary>
/// <remarks>
/// **What the engine does, read from published source and confirmed in TF2's shipped bytecode.** VertexLitGeneric takes
/// the bump shaders only for `bHasBump || bHasDiffuseWarp` (vertexlitgeneric_dx9_helper.cpp:611, :692, :1306) and the
/// skin shader for `WantsSkinShader`; every other material takes `vertexlit_and_unlit_generic_vs20/vs30`, whose
/// `o.color.xyz = DoLighting( worldPos, worldNormal, … )` (common_vs_fxc.h:862) sums the ambient cube by the vertex
/// normal and each light's `color * CosineTermInternal * VertexAttenInternal` at the VERTEX, and whose pixel shader takes
/// `diffuseLighting = i.color.rgb` (vertexlit_and_unlit_generic_ps2x.fxc:344-348), interpolated. TF2's
/// `hl2_misc` `vertexlit_and_unlit_generic_vs30.vcs` (static combo 0, dynamic 183) disassembles to that loop:
/// `lerp( normalize( pos - worldPos ), -dir, color.w )`, `max( 0, N·L )`, the attenuation, then the cube, into `o4`.
///
/// **Synthetic** (D38). A 256-unit quad facing the camera, two triangles split along the (−,−)–(+,+) diagonal, grey
/// albedo (128, linear 0.2158605), a uniform 0.1 cube and one point lamp of colour 1 and attenuation exactly 1
/// (`Constant` 1, no falloff), so a pixel is `sRGB( 0.2158605 * ( 0.1 + N·L ) )` and N·L is all that varies. Every
/// vertex is level with the camera, so the perspective-correct interpolation is linear across the screen. Pixel 32 of an
/// even target is the surface point (0, 0, 0) (B686).
/// </remarks>
public sealed class VertexLitLightingRenderTests
{
    private const int Size = 64;

    private const int Centre = 32;

    /// <summary>Half the quad's side.</summary>
    private const float Half = 128f;

    /// <summary>A pixel offset that stays on the quad (about ±14 pixels at 300 units).</summary>
    private const int Side = 8;

    private const float LinearGrey = 0.2158605f;

    private const float Cube = 0.1f;

    /// <summary>The material kinds: the vertex-lit path, the bump pixel shader, and the skin shader without a bump map.</summary>
    public enum Kind
    {
        Plain,
        PlainHalfLambert,
        Bumped,
        SkinWithoutBump,
    }

    /// <remarks>
    /// **The lamp 128 units in front of the quad's centre.** Each corner sees it at N·L = 128 / √(3 · 128²) = 1/√3, so
    /// the vertex path draws the whole quad at `0.1 + 0.57735`: 107 at the centre and aside alike. Lit per pixel the
    /// centre is N·L 1, `0.1 + 1`: 134, and the side darker. With `$halflambert` the corners' term is `CosineTermInternal`'s
    /// wrap, `(N·L * 0.5 + 0.5)^2` = 0.62201 (common_vs_fxc.h:826-830): 110.
    /// </remarks>
    [TestCase(Kind.Plain, 107)]
    [TestCase(Kind.PlainHalfLambert, 110)]
    public void DrawModelPose_PlainMaterialWithALampBeforeItsCentre_IsTheCornersLightEverywhere(Kind kind, int arithmetic)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        Draw(target, Material(kind), Lamp(0f, -Half, 0f));

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);
        (int R, int G, int B) aside = target.PixelAt(Centre + Side, Centre);
        float cosine = 1f / MathF.Sqrt(3f);
        float wrapped = (cosine * 0.5f) + 0.5f;
        int expected = Pixel(Cube + (kind == Kind.PlainHalfLambert ? wrapped * wrapped : cosine));

        TestContext.Out.WriteLine($"{kind}: centre {centre}, aside {aside}, predicted {expected}");

        expected.ShouldBe(arithmetic, "the arithmetic the prediction rests on");
        Near(centre, expected, 1, "every vertex is 1/√3 from the lamp's axis: DoLighting at the corners, interpolated");
        Near(aside, expected, 1, "and the same across the quad");
    }

    /// <remarks>
    /// **The lamp level with the quad's left edge, 64 units out**, so the left corners see N·L = 64 / √(64² + 128²) and
    /// the right ones 64 / √(256² + 64² + 128²). The centre lies on the shared diagonal, halfway between a left and a right
    /// corner, so the vertex path draws the MEAN of the two: `0.1 + (0.44721 + 0.21822) / 2`. Per pixel it would be the
    /// centre's own N·L, 0.44721.
    /// </remarks>
    [Test]
    public void DrawModelPose_PlainMaterialWithALampBesideOneEdge_InterpolatesBetweenTheVertices()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        Draw(target, Material(Kind.Plain), Lamp(-Half, -64f, 0f));

        float left = 64f / MathF.Sqrt((64f * 64f) + (Half * Half));
        float right = 64f / MathF.Sqrt((256f * 256f) + (64f * 64f) + (Half * Half));
        int gouraud = Pixel(Cube + ((left + right) / 2f));
        int perPixel = Pixel(Cube + left);

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"plain beside: centre {centre}, Gouraud {gouraud}, per pixel would be {perPixel}");

        // 86 against 96: ten levels apart, far outside the tolerance of one.
        (perPixel - gouraud).ShouldBeGreaterThanOrEqualTo(10, "the two models are told apart here");
        Near(centre, gouraud, 1, "the mean of the left and right corners' DoLighting");
    }

    /// <remarks>
    /// **The control, and the per-pixel path kept**: a flat normal map takes the bump shader, which lights per pixel
    /// (`PixelShaderDoLighting`), and so does the skin shader without one (`$basemapalphaphongmask`). Under the lamp
    /// before the centre both draw the centre at N·L 1: 134.
    /// </remarks>
    [TestCase(Kind.Bumped)]
    [TestCase(Kind.SkinWithoutBump)]
    public void DrawModelPose_PerPixelMaterialWithALampBeforeItsCentre_IsTheCentresOwnLight(Kind kind)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        Draw(target, Material(kind), Lamp(0f, -Half, 0f));

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);
        (int R, int G, int B) aside = target.PixelAt(Centre + Side, Centre);
        int expected = Pixel(Cube + 1f);

        TestContext.Out.WriteLine($"{kind}: centre {centre}, aside {aside}, predicted centre {expected}");

        expected.ShouldBe(134, "the arithmetic the prediction rests on");
        Near(centre, expected, 1, "N·L 1 at the centre, lit per pixel");
        (centre.G - aside.G).ShouldBeGreaterThan(5, "and darker aside, where the lamp is at an angle");
    }

    private static int Pixel(float light)
    {
        float linear = LinearGrey * light;
        float encoded = linear <= 0.0031308f ? 12.92f * linear : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (int)MathF.Round(encoded * 255f);
    }

    private static LocalLight Lamp(float x, float y, float z) =>
        new(x, y, z, 1f, 1f, 1f, 1f, 0f, 0f, 0f);

    private static MapAssets Material(Kind kind)
    {
        byte[] grey = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 128, 255 })];

        if (kind is Kind.Plain or Kind.PlainHalfLambert)
        {
            return MapAssets.ForMaterials(
                [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false, IsHalfLambert: kind == Kind.PlainHalfLambert)],
                ["VertexLitGeneric"]);
        }

        if (kind == Kind.Bumped)
        {
            byte[] flat = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 255, 255 })];

            return MapAssets.ForMaterials(
                [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false)],
                ["VertexLitGeneric"],
                bumps: [new MapBump(new MapTexture(4, 4, 4, 4, TextureImage.Rgba(flat), IsTransparent: false), false)]);
        }

        // `$phong 1` with `$basemapalphaphongmask 1` and `$halflambert 1`, no bump or warp: WantsSkinShader, and combine.y 3.
        // A boost of 0 leaves the diffuse alone to be predicted; half-Lambert at N·L 1 is `(1 * 0.5 + 0.5)^2`, still 1.
        return MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(grey), IsTransparent: false, IsHalfLambert: true, IsSkinShader: true)],
            ["VertexLitGeneric"],
            phong: [new MapPhong(5f, 0f, (0f, 1f, 0f), (1f, 1f, 1f), MaskedByBaseAlpha: true)]);
    }

    private static void Draw(OffscreenTarget target, MapAssets assets, LocalLight lamp)
    {
        static WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f)
            {
                NormalX = 0f,
                NormalY = -1f,
                NormalZ = 0f,
                TangentX = 1f,
                TangentY = 0f,
                TangentZ = 0f,
                TangentW = 1f,
            };

        List<WorldVertex> vertices =
        [
            Corner(-Half, -Half, 0f, 0f),
            Corner(Half, -Half, 1f, 0f),
            Corner(Half, Half, 1f, 1f),
            Corner(-Half, -Half, 0f, 0f),
            Corner(Half, Half, 1f, 1f),
            Corner(-Half, Half, 0f, 1f),
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
            light: new AmbientCube((Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube)),
            bothSides: true,
            locals: [lamp]);
    }

    private static void Near((int R, int G, int B) actual, int expected, int tolerance, string because)
    {
        Math.Abs(actual.R - expected).ShouldBeLessThanOrEqualTo(tolerance, because);
        Math.Abs(actual.G - expected).ShouldBeLessThanOrEqualTo(tolerance, because);
        Math.Abs(actual.B - expected).ShouldBeLessThanOrEqualTo(tolerance, because);
    }
}
