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
/// albedo (128, linear 0.2158605), so a pixel is `sRGB( 0.2158605 * light )` per channel. Every vertex is level with
/// the camera, so the perspective-correct interpolation is linear across the screen. Pixel 32 of an even target is the
/// surface point (0, 0, 0) (B686). Where every vertex sees the same light the quad is one value, which is the prediction.
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

    /// <summary>Far enough that N·L from every corner is 1 within 2e-6.</summary>
    private const float Far = 1.0e5f;

    private static readonly (float X, float Y, float Z) Facing = (0f, -1f, 0f);

    private static readonly AmbientCube Uniform =
        new((Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube), (Cube, Cube, Cube));

    /// <summary>The material kinds.</summary>
    public enum Kind
    {
        /// <summary>The vertex-lit path.</summary>
        Plain,

        /// <summary>The vertex-lit path with <c>$halflambert</c>.</summary>
        PlainHalfLambert,

        /// <summary>A flat normal map: the bump pixel shader.</summary>
        Bumped,

        /// <summary>The skin shader without a bump map (<c>$basemapalphaphongmask</c>), <c>$halflambert</c>, boost 0.</summary>
        SkinWithoutBump,

        /// <summary>The skin shader with a flat normal map and <c>$halflambert</c> (combine.y 3), boost 0.</summary>
        SkinWithBumpHalfLambert,

        /// <summary><c>$phong 1</c> with none of WantsSkinShader's three: the generic shader, boost 1.</summary>
        PhongWithoutSkin,
    }

    /// <remarks>
    /// **The lamp 128 units in front of the quad's centre.** Each corner sees it at N·L = 128 / √(3 · 128²) = 1/√3, so
    /// the vertex path draws the whole quad at `0.1 + 0.57735`: 107 at the centre and aside alike. Lit per pixel the
    /// centre is N·L 1, `0.1 + 1`: 134, and the side darker. With `$halflambert` the corners' term is
    /// `CosineTermInternal`'s wrap, `(N·L * 0.5 + 0.5)^2` = 0.62201 (common_vs_fxc.h:826-830): 110.
    /// </remarks>
    [TestCase(Kind.Plain, 107)]
    [TestCase(Kind.PlainHalfLambert, 110)]
    public void DrawModelPose_PlainMaterialWithALampBeforeItsCentre_IsTheCornersLightEverywhere(Kind kind, int arithmetic)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, Material(kind), locals: [Lamp(0f, -Half, 0f)]);

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);
        (int R, int G, int B) aside = target.PixelAt(Centre + Side, Centre);
        float cosine = 1f / MathF.Sqrt(3f);
        float wrapped = (cosine * 0.5f) + 0.5f;
        int expected = Pixel(Cube + (kind == Kind.PlainHalfLambert ? wrapped * wrapped : cosine));

        TestContext.Out.WriteLine($"{kind}: centre {centre}, aside {aside}, predicted {expected}");

        expected.ShouldBe(arithmetic, "the arithmetic the prediction rests on");
        Near(centre, expected, "every vertex is 1/√3 from the lamp's axis: DoLighting at the corners, interpolated");
        Near(aside, expected, "and the same across the quad");
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
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, Material(Kind.Plain), locals: [Lamp(-Half, -64f, 0f)]);

        float left = 64f / MathF.Sqrt((64f * 64f) + (Half * Half));
        float right = 64f / MathF.Sqrt((256f * 256f) + (64f * 64f) + (Half * Half));
        int gouraud = Pixel(Cube + ((left + right) / 2f));
        int perPixel = Pixel(Cube + left);

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"plain beside: centre {centre}, Gouraud {gouraud}, per pixel would be {perPixel}");

        // 86 against 96: ten levels apart, far outside the tolerance of one.
        (perPixel - gouraud).ShouldBeGreaterThanOrEqualTo(10, "the two models are told apart here");
        Near(centre, gouraud, "the mean of the left and right corners' DoLighting");
    }

    /// <remarks>
    /// **`AmbientLight( worldNormal )` by the vertex normal** (common_vs_fxc.h:749-758): the squared normal weights one face
    /// per axis, the negative face where the component is negative. Every vertex's normal is (0.6, −0.8, 0), so the quad
    /// is 0.36 · (+X 0.2) + 0.64 · (−Y 0.15) = 0.168: 54. The opposite faces would give 0.36 · 0.05 + 0.64 · 0.3 = 0.21.
    /// </remarks>
    [Test]
    public void DrawModelPose_PlainMaterialUnderAnUnevenCube_TakesTheFacesTheVertexNormalPicks()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        AmbientCube uneven = new((0.2f, 0.2f, 0.2f), (0.05f, 0.05f, 0.05f), (0.3f, 0.3f, 0.3f), (0.15f, 0.15f, 0.15f), (0.4f, 0.4f, 0.4f), (0.02f, 0.02f, 0.02f));

        Draw(target, Material(Kind.Plain), cube: uneven, normal: (0.6f, -0.8f, 0f));

        int expected = Pixel((0.36f * 0.2f) + (0.64f * 0.15f));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"uneven cube: centre {centre}, predicted {expected}, the opposite faces {Pixel((0.36f * 0.05f) + (0.64f * 0.3f))}");

        expected.ShouldBe(54, "the arithmetic the prediction rests on");
        Near(centre, expected, "+X by x², −Y by y²");
    }

    /// <remarks>
    /// **Each lamp by its own attenuation, and every lamp the count names.** A red lamp attenuated to 0.3 and a green one to
    /// 0.1, both far ahead (N·L 1): red `0.1 + 0.3` = 83, green `0.1 + 0.1` = 59, blue the cube alone, 40. Swapped
    /// attenuations draw (59, 83, 40); a loop that stops at one lamp, (83, 40, 40).
    /// </remarks>
    [Test]
    public void DrawModelPose_PlainMaterialUnderTwoLamps_TakesEachLampsOwnAttenuation()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        LocalLight red = new(0f, -Far, 0f, 1f, 0f, 0f, 1f / 0.3f, 0f, 0f, 0f);
        LocalLight green = new(0f, -Far, 0f, 0f, 1f, 0f, 1f / 0.1f, 0f, 0f, 0f);

        Draw(target, Material(Kind.Plain), locals: [red, green]);

        (int R, int G, int B) expected = (Pixel(Cube + 0.3f), Pixel(Cube + 0.1f), Pixel(Cube));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"two lamps: centre {centre}, predicted {expected}");

        expected.ShouldBe((83, 59, 40), "the arithmetic the prediction rests on");
        Near(centre, expected, "lamp 0 at attenuation 0.3, lamp 1 at 0.1");
    }

    /// <remarks>
    /// **A directional light in a local slot, and the sun, are `-dir` on this path** (common_vs_fxc.h:817): infinitely far,
    /// whatever the lighting origin. Travelling diagonally, (−1, 1, 0)/√2, N·L is 0.7071 at every vertex: `0.1 + 0.7071` =
    /// 116 across the quad. Travelling straight in with the lighting origin 10000 units aside, still N·L 1: 134 — the
    /// per-pixel path's `origin - dir · 10000 - worldPos` would give 116 (B721).
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void DrawModelPose_PlainMaterialUnderADirectionalLight_TakesItInfinitelyFar(bool sun)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        (float X, float Y, float Z) diagonal = (-0.70710678f, 0.70710678f, 0f);
        (float X, float Y, float Z) straight = (0f, 1f, 0f);

        (int R, int G, int B) Lit((float X, float Y, float Z) travelling, (float X, float Y, float Z) origin)
        {
            Draw(
                target,
                Material(Kind.Plain),
                sunLight: sun ? new SunLight(1f, 1f, 1f, travelling.X, travelling.Y, travelling.Z) : null,
                locals: sun ? null : [new LocalLight(0f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, travelling, Directional: true)],
                origin: origin);

            return target.PixelAt(Centre + Side, Centre);
        }

        (int R, int G, int B) slanted = Lit(diagonal, (0f, 0f, 0f));
        (int R, int G, int B) aside = Lit(straight, (10000f, 0f, 0f));

        TestContext.Out.WriteLine($"{(sun ? "sun" : "directional lamp")}: diagonal {slanted}, straight with the origin aside {aside}");

        Near(slanted, Pixel(Cube + 0.70710678f), "N·L 0.7071 at every vertex");
        Near(aside, Pixel(Cube + 1f), "-dir, never the lighting origin");
    }

    /// <remarks>
    /// **No `max( 1, d² )` in the shader's attenuation** (`VertexAttenInternal`, common_vs_fxc.h:762-793; TF2's vs30
    /// bytecode: `dp3`, `rsq`, no `max`). A 0.6-unit quad seen from 2 units, a lamp 0.5 units before its centre with pure
    /// quadratic falloff 2: each corner is at d² = 2 · 0.3² + 0.5² = 0.43, N·L 0.5/√0.43, attenuation 1/(2 · 0.43), so
    /// `0.1 + 0.88662`: 127. Clamped at d² = 1 the attenuation would be 0.5: 91.
    /// </remarks>
    [Test]
    public void DrawModelPose_PlainMaterialWithALampWithinOneUnit_AttenuatesByTheRawDistance()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, Material(Kind.Plain), locals: [new LocalLight(0f, -0.5f, 0f, 1f, 1f, 1f, 0f, 0f, 2f, 0f)], half: 0.3f, cameraDistance: 2f);

        const float DistanceSquared = (2f * 0.3f * 0.3f) + (0.5f * 0.5f);
        float cosine = 0.5f / MathF.Sqrt(DistanceSquared);
        int expected = Pixel(Cube + (cosine / (2f * DistanceSquared)));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"lamp within a unit: centre {centre}, predicted {expected}, clamped would be {Pixel(Cube + (cosine / 2f))}");

        expected.ShouldBe(127, "the arithmetic the prediction rests on");
        Near(centre, expected, "1 / (q · d²) at d² = 0.43");
    }

    /// <remarks>
    /// **Static plus dynamic** (`DoLighting`'s first `+=`, common_vs_fxc.h:868-876): a baked colour mesh of 0.2 linear at
    /// every vertex, the uniform cube and the lamp before the centre: `0.2 + 0.1 + 1/√3` = 0.87735 everywhere: 120.
    /// </remarks>
    [Test]
    public void DrawModelPose_PlainMaterialWithAColourMeshAndALamp_AddsTheColoursToTheVertexLight()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, Material(Kind.Plain), locals: [Lamp(0f, -Half, 0f)], baked: [.. Enumerable.Repeat(0.2f, 18)]);

        int expected = Pixel(0.2f + Cube + (1f / MathF.Sqrt(3f)));
        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);
        (int R, int G, int B) aside = target.PixelAt(Centre + Side, Centre);

        TestContext.Out.WriteLine($"colours + lamp: centre {centre}, aside {aside}, predicted {expected}");

        expected.ShouldBe(120, "the arithmetic the prediction rests on");
        Near(centre, expected, "colours + cube + lamp");
        Near(aside, expected, "the same across the quad");
    }

    /// <remarks>
    /// **The control, and the per-pixel path kept**: a flat normal map takes the bump shader, which lights per pixel
    /// (`PixelShaderDoLighting`), and so does the skin shader without one (`$basemapalphaphongmask`). Under the lamp
    /// before the centre both draw the centre at N·L 1: 134, and darker aside.
    /// </remarks>
    [TestCase(Kind.Bumped)]
    [TestCase(Kind.SkinWithoutBump)]
    public void DrawModelPose_PerPixelMaterialWithALampBeforeItsCentre_IsTheCentresOwnLight(Kind kind)
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, Material(kind), locals: [Lamp(0f, -Half, 0f)]);

        (int R, int G, int B) centre = target.PixelAt(Centre, Centre);
        (int R, int G, int B) aside = target.PixelAt(Centre + Side, Centre);
        int expected = Pixel(Cube + 1f);

        TestContext.Out.WriteLine($"{kind}: centre {centre}, aside {aside}, predicted centre {expected}");

        expected.ShouldBe(134, "the arithmetic the prediction rests on");
        Near(centre, expected, "N·L 1 at the centre, lit per pixel");
        (centre.G - aside.G).ShouldBeGreaterThan(5, "and darker aside, where the lamp is at an angle");
    }

    /// <remarks>
    /// **`mat_bumpmap 0` on a skin material that states `$halflambert`** (combine.y 3): `DrawSkin_DX9` only while
    /// `UseBumpmapping()` (vertexlitgeneric_dx9_helper.cpp:1515), so the generic vertex-lit shader draws it, with
    /// `$halflambert` (`:504`): the corners' wrapped term, 110 everywhere. With the map on it is the skin shader: 134 at the
    /// centre. Without its `$halflambert` it would be 107.
    /// </remarks>
    [Test]
    public void DrawModelPose_HalfLambertSkinMaterialWithBumpMapOff_IsTheGenericHalfLambertVertexLight()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        MapAssets assets = Material(Kind.SkinWithBumpHalfLambert);

        Draw(target, assets, locals: [Lamp(0f, -Half, 0f)]);
        (int R, int G, int B) on = target.PixelAt(Centre, Centre);

        Draw(target, assets, locals: [Lamp(0f, -Half, 0f)], bumpMap: false);
        (int R, int G, int B) off = target.PixelAt(Centre, Centre);

        float wrapped = (1f / MathF.Sqrt(3f) * 0.5f) + 0.5f;

        TestContext.Out.WriteLine($"half-Lambert skin: bump on {on}, off {off}");

        Near(on, Pixel(Cube + 1f), "the control: the skin shader, per pixel");
        Near(off, Pixel(Cube + (wrapped * wrapped)), "the generic shader with $halflambert, per vertex");
    }

    /// <remarks>
    /// **`$phong 1` alone stays on the generic shader** (`WantsSkinShader`, vertexlitgeneric_dx9_helper.cpp:38-59), which
    /// has no highlight: under a lamp along the mirrored view, boost 1, mask 1, the centre is the diffuse alone, 107. The
    /// same phong as a skin material draws a highlight there (the control).
    /// </remarks>
    [Test]
    public void DrawModelPose_PhongMaterialOffTheSkinShader_DrawsNoHighlight()
    {
        using OffscreenTarget? target = Target();

        if (target is null)
        {
            return;
        }

        Draw(target, Material(Kind.PhongWithoutSkin), locals: [Lamp(0f, -Half, 0f)]);
        (int R, int G, int B) generic = target.PixelAt(Centre, Centre);

        Draw(target, Skin(boost: 1f, halfLambert: false, bump: false), locals: [Lamp(0f, -Half, 0f)]);
        (int R, int G, int B) skin = target.PixelAt(Centre, Centre);

        TestContext.Out.WriteLine($"phong: generic {generic}, skin {skin}");

        (skin.G - Pixel(Cube + 1f)).ShouldBeGreaterThan(20, "the control: the skin shader draws a highlight here");
        Near(generic, Pixel(Cube + (1f / MathF.Sqrt(3f))), "the vertex-lit diffuse, nothing added");
    }

    private static OffscreenTarget? Target()
    {
        OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
        }

        return target;
    }

    private static int Pixel(float light)
    {
        float linear = LinearGrey * light;
        float encoded = linear <= 0.0031308f ? 12.92f * linear : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (int)MathF.Round(encoded * 255f);
    }

    private static LocalLight Lamp(float x, float y, float z) =>
        new(x, y, z, 1f, 1f, 1f, 1f, 0f, 0f, 0f);

    private static byte[] Grey() => [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 128, 255 })];

    private static MapBump FlatBump()
    {
        byte[] flat = [.. Enumerable.Range(0, 16).SelectMany(static _ => new byte[] { 128, 128, 255, 255 })];
        return new MapBump(new MapTexture(4, 4, 4, 4, TextureImage.Rgba(flat), IsTransparent: false), false);
    }

    private static MapAssets Material(Kind kind) => kind switch
    {
        Kind.Plain or Kind.PlainHalfLambert => MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(Grey()), IsTransparent: false, IsHalfLambert: kind == Kind.PlainHalfLambert)],
            ["VertexLitGeneric"]),
        Kind.Bumped => MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(Grey()), IsTransparent: false)],
            ["VertexLitGeneric"],
            bumps: [FlatBump()]),
        Kind.SkinWithoutBump => Skin(boost: 0f, halfLambert: true, bump: false),
        Kind.SkinWithBumpHalfLambert => Skin(boost: 0f, halfLambert: true, bump: true),
        _ => MapAssets.ForMaterials(
            [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(Grey()), IsTransparent: false)],
            ["VertexLitGeneric"],
            phong: [new MapPhong(5f, 1f, (0f, 1f, 0f), (1f, 1f, 1f), MaskedByBaseAlpha: false)]),
    };

    /// <summary>A skin-shader material; a boost of 0 leaves the diffuse alone to be predicted. Fresnel (0, 1, 0) encoded is 1.</summary>
    private static MapAssets Skin(float boost, bool halfLambert, bool bump) => MapAssets.ForMaterials(
        [new MapTexture(4, 4, 4, 4, TextureImage.Rgba(Grey()), IsTransparent: false, IsHalfLambert: halfLambert, IsSkinShader: true)],
        ["VertexLitGeneric"],
        bumps: bump ? [FlatBump()] : null,
        phong: [new MapPhong(5f, boost, (0f, 1f, 0f), (1f, 1f, 1f), MaskedByBaseAlpha: !bump)]);

    private static void Draw(
        OffscreenTarget target,
        MapAssets assets,
        AmbientCube? cube = null,
        (float X, float Y, float Z)? normal = null,
        SunLight? sunLight = null,
        IReadOnlyList<LocalLight>? locals = null,
        (float X, float Y, float Z)? origin = null,
        float[]? baked = null,
        bool bumpMap = true,
        float half = Half,
        float cameraDistance = 300f)
    {
        (float X, float Y, float Z) n = normal ?? Facing;

        WorldVertex Corner(float x, float z, float u, float v) =>
            new(x, 0f, z, u, v, 0f, 0f, 0f)
            {
                NormalX = n.X,
                NormalY = n.Y,
                NormalZ = n.Z,
                TangentX = 1f,
                TangentY = 0f,
                TangentZ = 0f,
                TangentW = 1f,
            };

        List<WorldVertex> vertices =
        [
            Corner(-half, -half, 0f, 0f),
            Corner(half, -half, 1f, 0f),
            Corner(half, half, 1f, 1f),
            Corner(-half, -half, 0f, 0f),
            Corner(half, half, 1f, 1f),
            Corner(-half, half, 0f, 1f),
        ];

        float[] identity =
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        ];

        float[] camera = new FreeCamera
        {
            Origin = (0f, -cameraDistance, 0f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
            NearZ = MathF.Min(FreeCamera.WorldNearPlane, cameraDistance / 8f),
        }.ToMatrix();

        target.Clear(0f, 0f, 0f);
        target.DrawModelPose(
            vertices,
            [new WorldBatch(0, 0, vertices.Count)],
            camera,
            identity,
            assets,
            light: cube ?? Uniform,
            bothSides: true,
            sun: sunLight,
            origin: origin,
            bakedColours: baked,
            locals: locals,
            bumpMap: bumpMap);
    }

    private static void Near((int R, int G, int B) actual, int expected, string because) =>
        Near(actual, (expected, expected, expected), because);

    private static void Near((int R, int G, int B) actual, (int R, int G, int B) expected, string because)
    {
        Math.Abs(actual.R - expected.R).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.G - expected.G).ShouldBeLessThanOrEqualTo(1, because);
        Math.Abs(actual.B - expected.B).ShouldBeLessThanOrEqualTo(1, because);
    }
}
